using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.Compression;
using System.Buffers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

using ICSharpCode.SharpZipLib;

using GTToolsSharp.BTree;

namespace GTToolsSharp
{
    public class GTVolume
    {
        public const int BASE_VOLUME_SEED = 1;

        public static Keyset keyset = new Keyset("KALAHARI-37863889", new Key(0x2DEE26A7, 0x412D99F5, 0x883C94E9, 0x0F1A7069));

        // 160 Bytes
        private const int HeaderSize = 0xA0;
        private readonly static byte[] MAGIC = { 0x5B, 0x74, 0x51, 0x62 };
        private readonly static byte[] SEGMENT_MAGIC = { 0x5B, 0x74, 0x51, 0x6E };

        private const int SEGMENT_SIZE = 2048;
        private const uint ZLIB_MAGIC = 0xFFF7EEC5;

        public readonly Endian Endian;

        public bool IsPatchVolume;
        public string PatchVolumeFolder;

        public string OutputDirectory { get; private set; }

        public uint Seed { get; private set; }
        public uint DataSize { get; private set; }
        public ulong Unk { get; private set; }
        public ulong TotalFileSize { get; private set; }
        public ulong DataOffset { get; private set; }

        /// <summary>
        /// Data for the table of contents.
        /// </summary>
        public byte[] TOCData { get; private set; }

        public uint NameTreeOffset;
        public uint ExtTreeOffset;
        public uint NodeTreeOffset;

        public List<uint> EntryOffsets;

        public string TitleID;

        private FileStream _volStream;

        public GTVolume(FileStream sourceStream, Endian endianness)
        {
            _volStream = sourceStream;

            Endian = endianness;
        }

        public GTVolume(string patchVolumeFolder, Endian endianness)
        {
            PatchVolumeFolder = patchVolumeFolder;
            IsPatchVolume = true;

            Endian = endianness;
        }

        /// <summary>
        /// Loads a volume file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isPatchVolume"></param>
        /// <param name="endianness"></param>
        /// <returns></returns>
        public static GTVolume Load(string path, bool isPatchVolume, Endian endianness)
        {
            var fs = new FileStream(!isPatchVolume ? path : Path.Combine(path, "K", "4D"), FileMode.Open);

            GTVolume vol;
            if (isPatchVolume)
                vol = new GTVolume(path, Endian.Big);
            else
                vol = new GTVolume(fs, endianness);

            if (fs.Length < HeaderSize)
                throw new IndexOutOfRangeException($"File size is smaller than expected header size ({HeaderSize}).");

            Span<byte> headerBuffer = new byte[HeaderSize];
            fs.Read(headerBuffer);

            if (!vol.DecryptHeader(headerBuffer, BASE_VOLUME_SEED))
                return null;

            if (!vol.ReadHeader(headerBuffer))
                return null;

            if (!vol.ParseTableOfContentsSegment())
                return null;

            return vol;
        }

        public void SetOutputDirectory(string dirPath)
            => OutputDirectory = dirPath;

        /// <summary>
        /// Unpacks all the files within the volume.
        /// </summary>
        public void UnpackAllFiles()
        {
            EntryBTree rootEntries = new EntryBTree(this.TOCData, (int)this.EntryOffsets[0]);

            var unpacker = new EntryUnpacker(this, OutputDirectory, "");
            rootEntries.Traverse(unpacker);
        }

        public string GetEntryPath(EntryKey key, string prefix)
        {
            string entryPath = prefix;
            StringBTree nameBTree = new StringBTree(TOCData, (int)NameTreeOffset);

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                StringBTree extBTree = new StringBTree(TOCData, (int)this.ExtTreeOffset);
                
            }
            else if (key.Flags.HasFlag(EntryKeyFlags.Directory))
                entryPath += '/';

            return entryPath;
        }

        /// <summary>
        /// Decrypts the header of the main volume file, using a provided seed.
        /// </summary>
        /// <param name="headerData"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        private bool DecryptHeader(Span<byte> headerData, uint seed)
        {
            if (!DecryptData(headerData, seed))
                return false;

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(headerData);
            int end = headerData.Length / sizeof(uint); // 160 / 4

            keyset.CryptBlocks(blocks, blocks);
            return true;
        }

        private bool DecryptData(Span<byte> data, uint seed)
        {
            keyset.CryptBytes(data, data, seed);
            return true;
        }

        /// <summary>
        /// Reads a buffer containing the header of a main volume file.
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        private bool ReadHeader(Span<byte> header)
        {
            var sr = new SpanReader(header, this.Endian);
            byte[] magic = sr.ReadBytes(4);
            if (!magic.AsSpan().SequenceEqual(MAGIC.AsSpan()))
            {
                Console.WriteLine($"Volume file Magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2") ) )})");
                return false;
            }

            Seed = sr.ReadUInt32();
            DataSize = sr.ReadUInt32();
            uint decompressedTOCSize = sr.ReadUInt32();
            Unk = sr.ReadUInt64();
            TotalFileSize = sr.ReadUInt64();
            TitleID = sr.ReadString0();

            // Go to the location of the data start

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(Seed);

                string localPath = Path.Combine(this.PatchVolumeFolder, path);
                Console.WriteLine($"Volume Patch path TOC: {localPath}");
                if (!File.Exists(localPath))
                {
                    Console.WriteLine($"Error: Unable to locate PDIPFS main TOC file on local filesystem. ({path})");
                    return false;
                }

                using var fs = new FileStream(localPath, FileMode.Open);
                byte[] data = new byte[DataSize];
                fs.Read(data);

                // Accessing a new file, we need to decrypt the header again
                DecryptData(data, Seed);

                if (!TryInflate(data, decompressedTOCSize, out byte[] deflatedData))
                    return false;

                TOCData = deflatedData;
            }
            else
            {
                _volStream.Seek(SEGMENT_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(_volStream);
                byte[] data = br.ReadBytes((int)DataSize);

                // Decrypt it with the seed that the main header gave us
                DecryptData(data, Seed);

                if (!TryInflate(data, decompressedTOCSize, out byte[] deflatedData))
                    return false;

                TOCData = deflatedData;
                DataOffset = Utils.AlignUp(SEGMENT_SIZE + DataSize, SEGMENT_SIZE);
            }

            return true;
        }

        private bool ParseTableOfContentsSegment()
        {
            var sr = new SpanReader(TOCData, this.Endian);
            byte[] magic = sr.ReadBytes(4);
            if (!magic.AsSpan().SequenceEqual(SEGMENT_MAGIC.AsSpan()))
            {
                Console.WriteLine($"Volume file segment magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2")))})");
                return false;
            }

            NameTreeOffset = sr.ReadUInt32();
            ExtTreeOffset = sr.ReadUInt32();
            NodeTreeOffset = sr.ReadUInt32();
            uint entryTreeCount = sr.ReadUInt32();

            EntryOffsets = new List<uint>((int)entryTreeCount);
            for (int i = 0; i < entryTreeCount; i++)
                EntryOffsets.Add(sr.ReadUInt32());

            return true;
        }

        private unsafe bool TryInflate(Span<byte> data, ulong outSize, out byte[] deflatedData)
        {
            deflatedData = Array.Empty<byte>();
            if (outSize > uint.MaxValue)
                return false;

            // Inflated is always little
            var sr = new SpanReader(data, Endian.Little);
            uint zlibMagic = sr.ReadUInt32();
            uint sizeComplement = sr.ReadUInt32();

            if ((long)zlibMagic != ZLIB_MAGIC)
                return false;

            if (!IsPatchVolume && (uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;
            if (sr.Length <= headerSize) // Header size, if it's under, data is missing
                return false;

            fixed (byte* pBuffer = &sr.Span.Slice(headerSize)[0])
            {
                int decompLen = (int)outSize - headerSize;

                using var ums = new UnmanagedMemoryStream(pBuffer, (long)outSize);
                using var ds = new DeflateStream(ums, CompressionMode.Decompress);

                deflatedData = new byte[(int)outSize];
                ds.Read(deflatedData, 0, (int)outSize);
            }

            return true;
        }

    }
}
