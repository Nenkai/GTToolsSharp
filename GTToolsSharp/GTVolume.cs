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
using GTToolsSharp.Utils;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents a container for all the files used within Gran Turismo games.
    /// </summary>
    public class GTVolume
    {
        public const int BASE_VOLUME_SEED = 1;
        // 160 Bytes
        private const int HeaderSize = 0xA0;

        /// <summary>
        /// Keyset used to decrypt and encrypt volume files.
        /// </summary>
        public Keyset Keyset { get; private set; }

        /// <summary>
        /// Default keys, Gran Turismo 5
        /// </summary>
        public static readonly Keyset DefaultKeyset = new Keyset("KALAHARI-37863889", new Key(0x2DEE26A7, 0x412D99F5, 0x883C94E9, 0x0F1A7069));


        public readonly Endian Endian;

        public bool IsPatchVolume { get; set; }
        public string PatchVolumeFolder { get; set; }
        public bool NoUnpack { get; set; }
        public string OutputDirectory { get; private set; }

        public Dictionary<string, InputPackEntry> FilesToPack = new Dictionary<string, InputPackEntry>();

        public GTVolumeHeader VolumeHeader { get; set; }
        public byte[] VolumeHeaderData { get; private set; }

        public GTVolumeTOC TableOfContents { get; set; }

        public uint DataOffset;
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
        public static GTVolume Load(Keyset keyset, string path, bool isPatchVolume, Endian endianness)
        {
            var fs = new FileStream(!isPatchVolume ? path : Path.Combine(path, "K", "4D"), FileMode.Open);

            GTVolume vol;
            if (isPatchVolume)
                vol = new GTVolume(path, Endian.Big);
            else
                vol = new GTVolume(fs, endianness);
            vol.SetKeyset(keyset);

            if (fs.Length < HeaderSize)
                throw new IndexOutOfRangeException($"File size is smaller than expected header size ({HeaderSize}).");

            vol.VolumeHeaderData = new byte[HeaderSize];
            fs.Read(vol.VolumeHeaderData);

            if (!vol.DecryptHeader(vol.VolumeHeaderData, BASE_VOLUME_SEED))
                return null;

            if (!vol.ReadHeader(vol.VolumeHeaderData))
                return null;

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            Program.Log("[-] Reading table of contents.", true);

            if (!vol.DecryptTOC())
                return null;

            if (!vol.TableOfContents.LoadTOC())
                return null;

            Program.Log($"[>] File names tree offset: {vol.TableOfContents.NameTreeOffset}", true);
            Program.Log($"[>] File extensions tree offset: {vol.TableOfContents.FileExtensionTreeOffset}", true);
            Program.Log($"[>] Node tree offset: {vol.TableOfContents.NodeTreeOffset}", true);
            Program.Log($"[>] Entry count: {vol.TableOfContents.RootAndFolderOffsets.Count}.", true);

            if (Program.SaveTOC)
                File.WriteAllBytes("VolumeTOC.bin", vol.TableOfContents.Data);

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
            FileEntryBTree rootEntries = new FileEntryBTree(this.TableOfContents.Data, (int)TableOfContents.RootAndFolderOffsets[0]);

            var unpacker = new EntryUnpacker(this, OutputDirectory, "");
            rootEntries.TraverseAndUnpack(unpacker);
        }

        public void PackFiles(string outrepackDir, string[] filesToRemove)
        {
            if (FilesToPack.Count == 0 && filesToRemove.Length == 0)
            {
                Program.Log("[X] Found no files to pack or remove from volume.", forceConsolePrint: true);
                return;
            }

            Program.Log($"[-] Preparing to pack {FilesToPack.Count} files, and remove {filesToRemove.Length} files");
            TableOfContents.PackFilesForPatchFileSystem(FilesToPack, filesToRemove, outrepackDir);

            Program.Log($"[-] Verifying and fixing Table of Contents segment sizes if needed");
            if (!TableOfContents.TryCheckAndFixInvalidSegmentIndexes())
                Program.Log($"[-] Re-ordered segment indexes.");
            else
                Program.Log($"[/] Segment sizes are correct.");

            Program.Log($"[-] Saving Table of Contents ({PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.LastIndex)})");
            TableOfContents.SaveToPatchFileSystem(outrepackDir, out uint compressedSize, out uint uncompressedSize);
            VolumeHeader.CompressedTOCSize = compressedSize;
            VolumeHeader.TOCSize = uncompressedSize;
            VolumeHeader.TotalVolumeSize = TableOfContents.GetTotalPatchFileSystemSize(compressedSize);

            Program.Log($"[-] Saving main volume header ({PDIPFSPathResolver.Default})");
            byte[] header = VolumeHeader.Serialize();

            Span<uint> headerBlocks = MemoryMarshal.Cast<byte, uint>(header);
            Keyset.EncryptBlocks(headerBlocks, headerBlocks);
            Keyset.CryptData(header, BASE_VOLUME_SEED);

            string headerPath = Path.Combine(outrepackDir, PDIPFSPathResolver.Default);
            Directory.CreateDirectory(Path.GetDirectoryName(headerPath));

            File.WriteAllBytes(headerPath, header);

            Program.Log($"[/] Done packing. ", forceConsolePrint: true);
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

        public bool UnpackNode(FileInfoKey nodeKey, string filePath)
        {
            ulong offset = DataOffset + (ulong)nodeKey.SegmentIndex * GTVolumeTOC.SEGMENT_SIZE;
            uint uncompressedSize = nodeKey.UncompressedSize;

            byte[] data;

            if (!IsPatchVolume)
            {
                Program.Log(nodeKey.ToString());
                if (NoUnpack)
                    return false;

                data = new byte[nodeKey.CompressedSize];
                Stream.ReadBytesAt(data, offset, (int)nodeKey.CompressedSize);
                Keyset.CryptData(data, nodeKey.FileIndex);

                byte[] finalData = null;
                if (nodeKey.Flags.HasFlag(FileInfoKey.FileInfoFlags.Compressed) && !MiscUtils.TryInflate(data, uncompressedSize, out finalData))
                {
                    Program.Log($"[X] Failed to decompress file ({filePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
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

                Program.Log($"[:] Unpacking: {patchFilePath} -> {filePath}");

                data = File.ReadAllBytes(localPath);
                if (data.Length >= 7)
                {
                    if (Encoding.ASCII.GetString(data.AsSpan(0, 7)).StartsWith("BSDIFF"))
                    {
                        Program.Log($"[X] Detected BSDIFF file for {filePath} ({patchFilePath}, can not unpack yet. (fileID {nodeKey.FileIndex})", forceConsolePrint: true);
                        return false;
                    }
                }

                Keyset.CryptData(data, nodeKey.FileIndex);

                byte[] finalData = null;

                if (nodeKey.Flags.HasFlag(FileInfoKey.FileInfoFlags.Compressed) && !MiscUtils.TryInflate(data, uncompressedSize, out finalData))
                {
                    Program.Log($"[X] Failed to decompress file {filePath} ({patchFilePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
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
            StringBTree nameBTree = new StringBTree(TableOfContents.Data, (int)TableOfContents.NameTreeOffset);

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                // If it's a file, find the extension aswell
                StringBTree extBTree = new StringBTree(TableOfContents.Data, (int)TableOfContents.FileExtensionTreeOffset);
                
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
            if (!Keyset.CryptData(headerData, seed))
                return false;

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(headerData);
            int end = headerData.Length / sizeof(uint); // 160 / 4

            Keyset.DecryptBlocks(blocks, blocks);
            return true;
        }

        /// <summary>
        /// Reads a buffer containing the header of a main volume file.
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        private bool ReadHeader(Span<byte> header)
        {
            GTVolumeHeader volHeader = GTVolumeHeader.FromStream(header);
            if (volHeader is null)
                return false;

            Program.Log($"[>] Volume Seed: {volHeader.LastIndex}");
            Program.Log($"[>] TOC Size: {volHeader.CompressedTOCSize} bytes ({volHeader.TOCSize} decompressed)");
            Program.Log($"[>] Total Volume Size: {MiscUtils.BytesToString((long)volHeader.TotalVolumeSize)}");
            Program.Log($"[>] Title ID: '{volHeader.TitleID}'");
            VolumeHeader = volHeader;

            return true;
        }

        private bool DecryptTOC()
        {
            if (VolumeHeader is null)
                throw new InvalidOperationException("Header was not yet loaded");

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.LastIndex);

                string localPath = Path.Combine(this.PatchVolumeFolder, path);
                Program.Log($"[!] Volume Patch Path Table of contents located at: {localPath}", true);
                if (!File.Exists(localPath))
                {
                    Program.Log($"[X] Error: Unable to locate PDIPFS main TOC file on local filesystem. ({path})", true);
                    return false;
                }

                using var fs = new FileStream(localPath, FileMode.Open);
                byte[] data = new byte[VolumeHeader.CompressedTOCSize];
                fs.Read(data);

                // Accessing a new file, we need to decrypt the header again
                Program.Log($"[-] Using seed {VolumeHeader.LastIndex} to decrypt TOC file at {path}", true);
                Keyset.CryptData(data, VolumeHeader.LastIndex);

                Program.Log($"[-] Decompressing Table of contents file..", true);
                if (!MiscUtils.TryInflate(data, VolumeHeader.TOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Location = path;
                TableOfContents.Data = deflatedData;
            }
            else
            {
                Stream.Seek(GTVolumeTOC.SEGMENT_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(Stream);
                byte[] data = br.ReadBytes((int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] Using seed {VolumeHeader.LastIndex} to decrypt TOC at offset {GTVolumeTOC.SEGMENT_SIZE}", true);

                // Decrypt it with the seed that the main header gave us
                Keyset.CryptData(data, VolumeHeader.LastIndex);

                Program.Log($"[-] Decompressing Table of contents within volume.. (offset: {GTVolumeTOC.SEGMENT_SIZE})", true);
                if (!MiscUtils.TryInflate(data, VolumeHeader.TOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Data = deflatedData;

                DataOffset = CryptoUtils.AlignUp(GTVolumeTOC.SEGMENT_SIZE + VolumeHeader.CompressedTOCSize, GTVolumeTOC.SEGMENT_SIZE);
            }

            return true;
        }

    }
}
