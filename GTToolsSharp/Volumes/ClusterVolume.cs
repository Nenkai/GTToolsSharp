using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers.Binary;
using System.Buffers;

using Syroot.BinaryData;

using GTToolsSharp.Headers;
using GTToolsSharp.Encryption;

using GTToolsSharp;
using ZstdNet;

namespace GTToolsSharp.Volumes
{
    /// <summary>
    /// GT7 Side Cluster Volume
    /// </summary>
    public class ClusterVolume
    {
        // Same magic as GTS
        public const ulong Magic = 0x2B26958523AD;

        private FileStream _fs;

        public string Name { get; set; }

        public uint SectorSize { get; set; }
        public uint ClusterSize { get; set; }
        public ulong VolumeSize { get; set; }
        public uint Flags { get; set; }
        public uint Seed { get; set; }

        private ClusterVolume(FileStream fs)
            => _fs = fs;

        public static ClusterVolume Read(string path)
        {
            FileStream fs = new FileStream(path, FileMode.Open);
            BinaryStream bs = new BinaryStream(fs, ByteConverter.Little);

            ulong magic = bs.ReadUInt64();
            if (magic != Magic)
                return null;

            var fileDevice = new ClusterVolume(fs);
            fileDevice.SectorSize = bs.ReadUInt32();
            fileDevice.ClusterSize = bs.ReadUInt32();
            fileDevice.VolumeSize = bs.ReadUInt64();
            fileDevice.Flags = bs.ReadUInt32();
            fileDevice.Seed = bs.ReadUInt32();

            return fileDevice;
        }

        public bool UnpackFile(MPHNodeInfo nodeKey, string filePath, int nodeIndex)
        {
            long offset = SectorSize * (long)nodeKey.SectorIndex;
            _fs.Position = offset;

            Stream fileStream;

            if (nodeKey.Format != MPHNodeFormat.PLAIN)
            {
                fileStream = new ChaCha20Stream(_fs, KeysetStore.GT7_Volume_Data_Key, GetStreamCryptorIVByNonce(nodeKey.Nonce));   
            }
            else if (nodeKey.Format == MPHNodeFormat.PLAIN && nodeKey.ExtraFlags != 0x85 && nodeKey.ExtraFlags != 0x83)
                fileStream = new ChaCha20Stream(_fs, KeysetStore.GT7_Volume_Data_Key, GetStreamCryptorIVByNonce(nodeKey.Nonce));
            else
                fileStream = _fs;

            Program.Log($"Unpacking: {filePath} (Index{nodeIndex}:{nodeKey.Algo}-{nodeKey.Format}-{nodeKey.Kind}, 0x{nodeKey.UncompressedSize}), Flag: 0x{nodeKey.ExtraFlags:X2}");

            string fileDir = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(fileDir);

            using FileStream outputStream = new FileStream(filePath, FileMode.Create);

            // Also compressed
            if (nodeKey.Format != MPHNodeFormat.PLAIN)
            {
                uint magic = fileStream.ReadUInt32();

                bool fragmented = nodeKey.Kind == MPHNodeKind.FRAG;

                switch (magic)
                {
                    case 0xFFF7ED85: // ZSTD_TINY
                        if (fragmented)
                            throw new InvalidDataException("Got fragmented as ZStd Tiny");

                        HandleZStdTiny(fileStream, outputStream, fragmented);
                        break;

                    case 0xFFF7972F: // ZSTD_REGULAR
                        HandleZStdStandard(fileStream, outputStream, fragmented);
                        break;

                    case 0xFFF7EEC5: // ZLIB
                        throw new NotImplementedException("ZLib decompression not implemented");
                        break;

                }
            }
            else
            {
                HandlePlain(fileStream, outputStream, nodeKey.UncompressedSize);
            }


            if (nodeIndex != -1)
            {
                string asciiMagic = "";

                outputStream.Position = 0;
                var outMagic = outputStream.Length >= 4 ? GetMagic(outputStream, out asciiMagic) : 0;
                outputStream.Close();
                outputStream.Dispose();

                if (outMagic == 0)
                {
                    File.Move(filePath, fileDir + $"/{nodeIndex}_{nodeKey.EntryHash:X8}.bin");
                }
                else
                {
                    string ext = outMagic switch
                    {
                        0xe0ffd8ff => "jpg",
                        0xE1FFD8FF => "jpg",
                        0x200A0D7B => "json",
                        0x6d783f3c => "xml",
                        0x474e5089 => "png",
                        _ => "",
                    };

                    if (ext == "")
                    {
                        if (asciiMagic != null)
                        {
                            if (asciiMagic == "ADCH")
                                ext = "adc";
                            else
                                ext = asciiMagic;
                        }
                        else
                        {
                            ext = "bin";
                        }
                    }

                    File.Move(filePath, fileDir + $"/{nodeIndex}_{nodeKey.Format}-{nodeKey.Algo}_{nodeKey.EntryHash:X8}.{ext}", overwrite: true);
                }
            }
            return true;
        }

        public uint GetMagic(Stream stream, out string asciiMagic)
        {
            asciiMagic = "";

            for (var i = 0; i < 4; i++)
            {
                char c = (char)stream.Read1Byte();
                if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                    asciiMagic += c;
                else
                {
                    asciiMagic = null;
                    break;
                }
            }

            stream.Position = 0;
            return stream.ReadUInt32();
        }

        public void HandlePlain(Stream input, Stream outputStream, ulong size)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(0x20000);
            ulong rem = size;
            while (rem > 0)
            {
                Span<byte> current = buffer.AsSpan(0, (int)Math.Min(rem, (ulong)buffer.Length));
                var read = input.Read(current);

                outputStream.Write(current);

                rem -= (uint)read;
            }
        }

        public void HandleZStdTiny(Stream stream, Stream outputStream, bool isFragmented)
        {
            int uncompressed_size = -stream.ReadInt32();

            if (!isFragmented)
            {
                var decompressor = new DecompressionStream(stream);

                var rem = uncompressed_size;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(0x20000);
                while (rem > 0)
                {
                    Span<byte> current = buffer.AsSpan(0, Math.Min(rem, buffer.Length));
                    var read = decompressor.Read(current);


                    outputStream.Write(current);

                    rem -= read;
                }

                ArrayPool<byte>.Shared.Return(buffer);
            }
            else
            {
                var decompressor = new Decompressor();
                while (true)
                {
                    uint magic = stream.ReadUInt32();
                    if (magic != 0xFFF7F32E)
                        break;

                    int chunk_uncompressed_size = stream.ReadInt32();
                    int chunk_compressed_size = stream.ReadInt32();
                    uint crc_checksum = stream.ReadUInt32();

                    byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(chunk_uncompressed_size);
                    byte[] inputBuffer = ArrayPool<byte>.Shared.Rent(chunk_compressed_size);
                    stream.Read(inputBuffer.AsSpan(0, chunk_compressed_size));

                    decompressor.Unwrap(
                        inputBuffer.AsSpan(0, chunk_compressed_size),
                        chunkBuffer.AsSpan(0, chunk_uncompressed_size), bufferSizePrecheck: false);

                    outputStream.Write(chunkBuffer.AsSpan(0, chunk_uncompressed_size));

                    stream.Align(0x10000);

                    ArrayPool<byte>.Shared.Return(chunkBuffer);
                    ArrayPool<byte>.Shared.Return(inputBuffer);
                }
            }
        }

        public void HandleZStdStandard(Stream stream, Stream outputStream, bool isFragmented)
        {
            long startPos = stream.Position;

            uint uncompressed_size_lo = stream.ReadUInt32();
            uint uncompressed_size_hi = stream.ReadUInt16();
            ulong uncompressed_size = (ulong)uncompressed_size_hi << 32 | uncompressed_size_lo;

            uint compressed_size_lo = stream.ReadUInt32();
            uint compressed_size_hi = stream.ReadUInt16();
            ulong compressed_size = (ulong)compressed_size_hi << 32 | compressed_size_lo;

            int[] unk = stream.ReadInt32s(3);

            int flags = stream.ReadInt32();
            
            if (!isFragmented)
            {
                var decompressor = new DecompressionStream(stream);

                var rem = uncompressed_size;
                byte[] buffer = new byte[0x2000];
                while (rem > 0)
                {
                    Span<byte> current = buffer.AsSpan(0, (int)Math.Min(rem, (ulong)buffer.Length));
                    var read = decompressor.Read(current);

                    outputStream.Write(current);

                    rem -= (uint)read;
                }
            }
            else
            {
                var decompressor = new Decompressor();
                
                while (true)
                {
                    uint magic = stream.ReadUInt32();
                    if (magic != 0xFFF7F32E)
                        break;

                    int chunk_uncompressed_size = stream.ReadInt32();
                    int chunk_compressed_size = stream.ReadInt32();
                    uint crc_checksum = stream.ReadUInt32();

                    byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(chunk_uncompressed_size);
                    byte[] inputBuffer = ArrayPool<byte>.Shared.Rent(chunk_compressed_size);
                    stream.Read(inputBuffer.AsSpan(0, chunk_compressed_size));

                    decompressor.Unwrap(
                        inputBuffer.AsSpan(0, chunk_compressed_size), 
                        chunkBuffer.AsSpan(0, chunk_uncompressed_size), bufferSizePrecheck: false);

                    outputStream.Write(chunkBuffer.AsSpan(0, chunk_uncompressed_size));

                    stream.Align(0x10000);
                }
            }
        }

        public void HandleZStdChunks()
        {

        }

        public byte[] GetStreamCryptorIVByNonce(uint nonce)
        {
            byte[] iv = new byte[12];
            BinaryPrimitives.TryWriteUInt32LittleEndian(iv, nonce);
            return iv;
        }

        public override string ToString()
        {
            return $"{Name} (Size: 0x{VolumeSize:X16})";
        }
    }
}
