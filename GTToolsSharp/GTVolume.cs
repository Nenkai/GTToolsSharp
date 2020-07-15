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

using GTToolsSharp.BTree;

namespace GTToolsSharp
{
    public class GTVolume
    {
        public const int BASE_VOLUME_SEED = 1;
        // 160 Bytes
        private const int HeaderSize = 0xA0;
        private readonly static byte[] MAGIC = { 0x5B, 0x74, 0x51, 0x62 };
        private readonly static byte[] SEGMENT_MAGIC = { 0x5B, 0x74, 0x51, 0x6E };

        private const int SEGMENT_SIZE = 2048;
        public const uint ZLIB_MAGIC = 0xFFF7EEC5;

        public Keyset Keyset { get; private set; }
        public static readonly Keyset DefaultKeyset = new Keyset("KALAHARI-37863889", new Key(0x2DEE26A7, 0x412D99F5, 0x883C94E9, 0x0F1A7069));
        public readonly Endian Endian;

        public bool IsPatchVolume { get; set; }
        public string PatchVolumeFolder { get; set; }
        public bool NoUnpack { get; set; }
        public string OutputDirectory { get; private set; }
        public string VolumeHeaderPath { get; private set; }
        public string VolumeTOCOutputPath { get; private set; }

        public Dictionary<string, InputPackEntry> FilesToPack = new Dictionary<string, InputPackEntry>();

        public uint Seed { get; private set; }
        public uint DataSize { get; private set; }
        public ulong Unk { get; private set; }
        public ulong TotalVolumeFileSize { get; private set; }
        public ulong DataOffset { get; private set; }

        public byte[] VolumeHeaderData { get; private set; }
        /// <summary>
        /// Data for the table of contents.
        /// </summary>
        public byte[] TOCData { get; private set; }

        public uint NameTreeOffset;
        public uint FileExtensionTreeOffset;
        public uint NodeTreeOffset;

        public List<uint> RootAndFolderOffsets;

        public string TitleID;

        public FileStream Stream { get; }

        public GTVolume(FileStream sourceStream, Endian endianness)
        {
            Stream = sourceStream;

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
        public static GTVolume Load(Keyset keyset, string path, string volumeHeaderOutPath, string volumeTOCOutPath, bool isPatchVolume, Endian endianness)
        {
            var fs = new FileStream(!isPatchVolume ? path : Path.Combine(path, "K", "4D"), FileMode.Open);

            GTVolume vol;
            if (isPatchVolume)
                vol = new GTVolume(path, Endian.Big);
            else
                vol = new GTVolume(fs, endianness);
            vol.SetKeyset(keyset);
            vol.VolumeHeaderPath = volumeHeaderOutPath;
            vol.VolumeTOCOutputPath = volumeTOCOutPath;

            if (fs.Length < HeaderSize)
                throw new IndexOutOfRangeException($"File size is smaller than expected header size ({HeaderSize}).");

            vol.VolumeHeaderData = new byte[HeaderSize];
            fs.Read(vol.VolumeHeaderData);

            if (!vol.DecryptHeader(vol.VolumeHeaderData, BASE_VOLUME_SEED))
                return null;

            if (!string.IsNullOrEmpty(vol.VolumeHeaderPath))
                File.WriteAllBytes(vol.VolumeHeaderPath, vol.VolumeHeaderData);

            if (!vol.ReadHeader(vol.VolumeHeaderData))
                return null;

            if (!string.IsNullOrEmpty(vol.VolumeTOCOutputPath))
                File.WriteAllBytes(vol.VolumeTOCOutputPath, vol.TOCData);

            if (!vol.ParseTableOfContentsSegment())
                return null;

            return vol;
        }

        private void SetKeyset(Keyset keyset)
            => Keyset = keyset;

        public void SetOutputDirectory(string dirPath)
            => OutputDirectory = dirPath;

        /// <summary>
        /// Unpacks all the files within the volume.
        /// </summary>
        public void UnpackAllFiles()
        {
            Program.Log("[-] Unpacking files.");
            FileEntryBTree rootEntries = new FileEntryBTree(this.TOCData, (int)RootAndFolderOffsets[0]);

            var unpacker = new EntryUnpacker(this, OutputDirectory, "");
            rootEntries.TraverseAndUnpack(unpacker);
        }

        public void PackFiles(string outrepackDir)
        {
            if (FilesToPack.Count == 0)
            {
                Program.Log("[X] Found no files to pack.", forceConsolePrint: true);
                return;
            }

            FileEntryBTree rootEntries = new FileEntryBTree(this.TOCData, (int)RootAndFolderOffsets[0]);

            var packer = new EntryPacker(this, outrepackDir, "");
            rootEntries.TraverseAndPack(packer);

            // Until the entire thing is figured out, don't do anything header wise.
            // It breaks the game.
            // WriteTOC();
            // WriteHeader();
            Program.Log($"[/] Done packing. Note: Could be potentially game breaking, backup your original files!", forceConsolePrint: true);
        }

        public void WriteTOC(string outRepackDir)
        {
            string tocPath = PDIPFSPathResolver.GetPathFromSeed(Seed);
            Program.Log($"[-] Compressing and encrypting TOC file ({tocPath}).", forceConsolePrint: true);

            byte[] newTOC = new byte[TOCData.Length];
            TOCData.AsSpan().CopyTo(newTOC);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(ZLIB_MAGIC);
            bw.Write((uint)(0u - newTOC.Length));
            using var ds = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true);
            ds.Write(newTOC, 0, newTOC.Length);

            byte[] compressedFile = ms.ToArray();
            CryptData(compressedFile, Seed);

            string outPath = Path.Combine(outRepackDir, tocPath).Replace('\\', '/');

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, compressedFile);

            Program.Log($"[/] Done packing TOC.", forceConsolePrint: true);
        }

        public void WriteHeader(string outRepackDir, uint compressedTOCFileSize)
        {
            Program.Log($"[-] Packing Main Header. (K/4D)", forceConsolePrint: true);

            byte[] newHeader = new byte[HeaderSize];
            VolumeHeaderData.AsSpan().CopyTo(newHeader);

            SpanWriter sw = new SpanWriter(newHeader, Endian.Big);
            sw.Position = 8;
            sw.WriteUInt32(compressedTOCFileSize);
            sw.WriteUInt32((uint)TOCData.Length);

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(newHeader);
            Keyset.EncryptBlocks(blocks, blocks);
            CryptData(newHeader, BASE_VOLUME_SEED);

            Directory.CreateDirectory(outRepackDir + "/K");

            // FIXME: PDIPFSPathResolver.GetPathFromSeed(1);
            File.WriteAllBytes(outRepackDir + "/K/4D", newHeader);
        }

        public void RegisterEntriesToRepack(string inputDir)
        {
            string[] files = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var entry = new InputPackEntry();
                entry.FullPath = file;
                entry.VolumeDirPath = file.AsSpan(inputDir.Length).TrimStart('\\').ToString().Replace('\\', '/');
                entry.FileSize = (uint)new FileInfo(entry.FullPath).Length;
                FilesToPack.Add(entry.VolumeDirPath, entry);
            }
        }

        private const string DirSeparator = "\\";
        public bool UnpackNode(FileInfoKey nodeKey, string filePath)
        {
            uint volumeIndex = nodeKey.VolumeIndex;

            ulong offset = DataOffset + (ulong)nodeKey.SectorIndex * SEGMENT_SIZE;
            uint uncompressedSize = nodeKey.UncompressedSize;

            byte[] data;

            if (!IsPatchVolume)
            {
                Program.Log(nodeKey.ToString());
                if (NoUnpack)
                    return false;

                data = new byte[nodeKey.CompressedSize];
                Stream.ReadBytesAt(data, offset, (int)nodeKey.CompressedSize);
                CryptData(data, nodeKey.FileIndex);

                byte[] finalData = null;
                if ((nodeKey.Flags & 0xF) != 0 && !TryInflate(data, uncompressedSize, out finalData))
                {
                    Program.Log($"[X] Failed to decompress file ({filePath})", forceConsolePrint: true);
                    return false;
                }
                    

                File.WriteAllBytes(filePath, finalData ?? data);
            }
            else
            {
                /* I'm really not sure if there's a better way to do this.
                 * Volume files, at least nodes don't seem to even store any special flag whether
                 * it is located within an actual volume file or a patch volume. The only thing that is different is the sector index.. Sometimes node index when it's updated
                 * It's slow, but somewhat works I guess..
                 * */
                string patchFilePath = PDIPFSPathResolver.GetPathFromSeed(nodeKey.FileIndex);
                string localPath = this.PatchVolumeFolder+"/"+patchFilePath;

                if (NoUnpack)
                {
                    Program.Log(nodeKey.ToString());
                    return false;
                }

                if (!File.Exists(localPath))
                    return false;

                Program.Log($"Unpacking: {patchFilePath} -> {filePath}");

                data = File.ReadAllBytes(localPath);
                CryptData(data, nodeKey.FileIndex);

                byte[] finalData = null;

                if ((nodeKey.Flags & 0xF) != 0 && !TryInflate(data, uncompressedSize, out finalData))
                {
                    Program.Log($"[X] Failed to decompress file {filePath} ({patchFilePath})");
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, finalData ?? data);
            }

            return true;
        }

        public static string lastEntryPath = "";
        public string GetEntryPath(FileEntryKey key, string prefix)
        {
            string entryPath = prefix;
            StringBTree nameBTree = new StringBTree(TOCData, (int)NameTreeOffset);

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                // If it's a file, find the extension aswell
                StringBTree extBTree = new StringBTree(TOCData, (int)FileExtensionTreeOffset);
                
                if (extBTree.TryFindIndex(key.FileExtensionIndex, out StringKey extKey) && !string.IsNullOrEmpty(extKey.Value))
                    entryPath += extKey.Value;

            }
            else if (key.Flags.HasFlag(EntryKeyFlags.Directory))
                entryPath += '/';

            lastEntryPath = entryPath;

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
            if (!CryptData(headerData, seed))
                return false;

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(headerData);
            int end = headerData.Length / sizeof(uint); // 160 / 4

            Keyset.DecryptBlocks(blocks, blocks);
            return true;
        }

        public bool CryptData(Span<byte> data, uint seed)
        {
            Keyset.CryptBytes(data, data, seed);
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
                Console.WriteLine($"[X] Volume file Magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2") ) )})");
                return false;
            }

            Seed = sr.ReadUInt32();
            Program.Log($"[>] Volume Seed: {Seed}");

            DataSize = sr.ReadUInt32();
            uint decompressedTOCSize = sr.ReadUInt32();
            Program.Log($"[>] TOC Size: {DataSize} bytes ({decompressedTOCSize} decompressed)");
            Unk = sr.ReadUInt64();

            TotalVolumeFileSize = sr.ReadUInt64();
            Program.Log($"[>] Total Volume Size: {TotalVolumeFileSize}");

            TitleID = sr.ReadString0();
            Program.Log($"[>] Title ID: {TitleID}");

            // Go to the location of the data start

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(Seed);

                string localPath = Path.Combine(this.PatchVolumeFolder, path);
                Program.Log($"[!] Volume Patch Path Table of contents located at: {localPath}", true);
                if (!File.Exists(localPath))
                {
                    Program.Log($"Error: Unable to locate PDIPFS main TOC file on local filesystem. ({path})", true);
                    return false;
                }

                using var fs = new FileStream(localPath, FileMode.Open);
                byte[] data = new byte[DataSize];
                fs.Read(data);

                // Accessing a new file, we need to decrypt the header again
                Program.Log($"[-] Using seed {Seed} to decrypt TOC file at {path}", true);
                CryptData(data, Seed);

                Program.Log($"[-] Decompressing Table of contents file..", true);
                if (!TryInflate(data, decompressedTOCSize, out byte[] deflatedData))
                    return false;

                TOCData = deflatedData;
            }
            else
            {
                Stream.Seek(SEGMENT_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(Stream);
                byte[] data = br.ReadBytes((int)DataSize);

                Program.Log($"[-] Using seed {Seed} to decrypt TOC at offset {SEGMENT_SIZE}", true);

                // Decrypt it with the seed that the main header gave us
                CryptData(data, Seed);

                Program.Log($"[-] Decompressing Table of contents within volume.. (offset: {SEGMENT_SIZE})", true);
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
                Program.Log($"Volume file segment magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2")))})");
                return false;
            }

            Program.Log("[-] Reading table of contents.", true);

            NameTreeOffset = sr.ReadUInt32();
            Program.Log($"[>] File names tree offset: {NameTreeOffset}", true);

            FileExtensionTreeOffset = sr.ReadUInt32();
            Program.Log($"[>] File extensions tree offset: {FileExtensionTreeOffset}", true);

            NodeTreeOffset = sr.ReadUInt32();
            Program.Log($"[>] Node tree offset: {NodeTreeOffset}", true);

            uint entryTreeCount = sr.ReadUInt32();
            Program.Log($"[>] Entry count: {entryTreeCount}.", true);
            RootAndFolderOffsets = new List<uint>((int)entryTreeCount);
            for (int i = 0; i < entryTreeCount; i++)
                RootAndFolderOffsets.Add(sr.ReadUInt32());

            return true;
        }

        public unsafe bool TryInflate(Span<byte> data, ulong outSize, out byte[] deflatedData)
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

            if ((uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;
            if (sr.Length <= headerSize) // Header size, if it's under, data is missing
                return false;

            deflatedData = new byte[(int)outSize];
            fixed (byte* pBuffer = &sr.Span.Slice(headerSize)[0])
            {
                using var ums = new UnmanagedMemoryStream(pBuffer, sr.Span.Length - headerSize);
                using var ds = new DeflateStream(ums, CompressionMode.Decompress);
                ds.Read(deflatedData, 0, (int)outSize);
            }

            return true;
        }

    }
}
