using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.Compression;
using System.Buffers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Encryption;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents a container for all the files used within Gran Turismo games.
    /// </summary>
    public class GTVolume
    {
        public const int BASE_VOLUME_ENTRY_INDEX = 1;
        // 160 Bytes
        private const int HeaderSize = 0xA0;
        private const int OldHeaderSize = 0x14;

        /// <summary>
        /// Keyset used to decrypt and encrypt volume files.
        /// </summary>
        public Keyset Keyset { get; private set; }

        /// <summary>
        /// Default keys, Gran Turismo 5
        /// </summary>
        public static readonly Keyset Keyset_GT5P_JP_DEMO = new Keyset("GT5P_JP_DEMO", "PDIPFS-071020-02", default, new BufferDecryptManager
        {
            Keys = new()
            {
                { "/car/lod/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE4oAo5c" },
                { "/car/menu/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE6GuajW" },
                { "/car/interior/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE5JUizQ" },
                { "/car/meter/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE5iDFjf" },
                { "/piece/car_thumb_M/gtr_07_01.img", "cjg1NDJzZDVmNGgyNXM0cnQ2eTJkcjg0Z3pkZmJ3ZmEtdwwS" },

                { "/car/lod/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPI1QYGR" },
                { "/car/menu/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPJS5l7P" },
                { "/car/interior/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPKP9PTn" },
                { "/piece/car_thumb_M/impreza_wrx_sti_07_03.img", "cTM0NWgzNTZ5djJoZzRmMTIzNDQ1NjQ1ajZqMjRoNWY4Rqik" },
            }
        });

        public static readonly Keyset Keyset_GT5P_US_SPEC3 = new Keyset("GT5P_US_SPEC_III", "SONORA-550937027", new Key(0x4B0A7FFD, 0xCD1FE36D, 0x504AB1B5, 0x364A172F));
        public static readonly Keyset Keyset_GT5P_EU_SPEC3 = new Keyset("GT5P_EU_SPEC_III", "TOTTORI-562314254", new Key(0x5F29A71B, 0xA80945CF, 0xBECCA74F, 0x07C9800F));
        public static readonly Keyset Keyset_GT5_EU = new Keyset("GT5_EU", "KALAHARI-37863889", new Key(0x2DEE26A7, 0x412D99F5, 0x883C94E9, 0x0F1A7069));
        public static readonly Keyset Keyset_GT5_US = new Keyset("GT5_US", "PATAGONIAN-22798263", new Key(0x5A1A59E5, 0x4D3546AB, 0xF30AF68B, 0x89F08D0D));
        public static readonly Keyset Keyset_GT6 = new Keyset("GT6", "PISCINAS-323419048", new Key(0xAA1B6A59, 0xE70B6FB3, 0x62DC6095, 0x6A594A25));

        public readonly Endian Endian;

        public bool IsPatchVolume { get; set; }
        public string PatchVolumeFolder { get; set; }
        public bool NoUnpack { get; set; }
        public bool UsePackingCache { get; set; }
        public bool NoCompress { get; set; }
        public bool CreateBDMARK { get; set; }
        public bool IsGT5PDemoStyle { get; set; }

        public string OutputDirectory { get; private set; }

        public Dictionary<string, InputPackEntry> FilesToPack = new Dictionary<string, InputPackEntry>();

        /// <summary>
        /// The packing cache to use to speed up packing which ignores already properly packed files.
        /// </summary>
        private PackCache _packCache { get; set; } = new PackCache();

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
            FileStream fs;
            GTVolume vol;
            if (isPatchVolume)
            {
                if (File.Exists(Path.Combine(path, PDIPFSPathResolver.Default)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.Default), FileMode.Open);
                else if (File.Exists(Path.Combine(path, PDIPFSPathResolver.DefaultOld)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.DefaultOld), FileMode.Open);
                else
                    return null;

                vol = new GTVolume(path, endianness);
            }
            else
            {
                fs = new FileStream(path, FileMode.Open);
                vol = new GTVolume(fs, endianness);
            }

            vol.SetKeyset(keyset);

            vol.IsGT5PDemoStyle = fs.Length == OldHeaderSize;
            if (fs.Length < OldHeaderSize)
                throw new IndexOutOfRangeException($"Volume header file size is smaller than expected header size ({HeaderSize} or {OldHeaderSize}). Ensure that your volume file is not corrupt.");


            vol.VolumeHeaderData = new byte[!vol.IsGT5PDemoStyle ? HeaderSize : OldHeaderSize];
            fs.Read(vol.VolumeHeaderData);

            if (!vol.DecryptHeader(vol.VolumeHeaderData, BASE_VOLUME_ENTRY_INDEX))
            {
                fs?.Dispose();
                return null;
            }

            if (!vol.ReadHeader(vol.VolumeHeaderData))
            {
                fs?.Dispose();
                return null;
            }

            if (fs.Length == OldHeaderSize)
            {
                vol.IsGT5PDemoStyle = true;
                Program.Log("[!] Successfully decrypted header - volume appears to be GT5P Demo, slightly different volume.", true);
            }

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            Program.Log("[-] Reading table of contents.", true);

            if (!vol.DecryptTOC())
            {
                fs?.Dispose();
                return null;
            }

            if (!vol.TableOfContents.LoadTOC())
            {
                fs?.Dispose();
                return null;
            }

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
        /// Decrypts the header of the main volume file, using a provided seed.
        /// </summary>
        /// <param name="headerData"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        private bool DecryptHeader(Span<byte> headerData, uint seed)
        {
            if (IsGT5PDemoStyle)
            {
                GT5POldCrypto.DecryptPass(1, headerData, headerData, 0x14);

                byte[] outdata = new byte[0x14];
                GT5POldCrypto.DecryptHeaderSpecific(outdata, headerData);
                outdata.CopyTo(headerData);
                return true;
            }

            if (Keyset.Key.Data is null || Keyset.Key.Data.Length < 4)
                return false;

            CryptoUtils.CryptBuffer(Keyset, headerData, headerData, seed);

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(headerData);
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

            Program.Log($"[>] PFS Version: '{volHeader.PFSVersion}'");
            Program.Log($"[>] Table of Contents Entry Index: {volHeader.TOCEntryIndex}");
            Program.Log($"[>] TOC Size: {volHeader.CompressedTOCSize} bytes ({volHeader.TOCSize} decompressed)");

            if (!IsGT5PDemoStyle)
            {
                Program.Log($"[>] Total Volume Size: {MiscUtils.BytesToString((long)volHeader.TotalVolumeSize)}");
                Program.Log($"[>] Title ID: '{volHeader.TitleID}'");
            }
            VolumeHeader = volHeader;

            return true;
        }

        private bool DecryptTOC()
        {
            if (VolumeHeader is null)
                throw new InvalidOperationException("Header was not yet loaded");

            if (Keyset.Key.Data is null || Keyset.Key.Data.Length < 4)
                return false;

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.TOCEntryIndex, IsGT5PDemoStyle);

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
                Program.Log($"[-] TOC Entry is {VolumeHeader.TOCEntryIndex} which is at {path} - decrypting it", true);
                if (!IsGT5PDemoStyle)
                    CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.TOCEntryIndex);
                else
                    GT5POldCrypto.DecryptPass(VolumeHeader.TOCEntryIndex, data, data, (int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] Decompressing TOC file..", true);
                if (!MiscUtils.TryInflateInMemory(data, VolumeHeader.TOCSize, out byte[] deflatedData))
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

                Program.Log($"[-] TOC Entry is {VolumeHeader.TOCEntryIndex} which is at offset {GTVolumeTOC.SEGMENT_SIZE}", true);

                // Decrypt it with the seed that the main header gave us
                CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.TOCEntryIndex);

                Program.Log($"[-] Decompressing TOC within volume.. (offset: {GTVolumeTOC.SEGMENT_SIZE})", true);
                if (!MiscUtils.TryInflateInMemory(data, VolumeHeader.TOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Data = deflatedData;

                DataOffset = MiscUtils.Align(GTVolumeTOC.SEGMENT_SIZE + VolumeHeader.CompressedTOCSize, GTVolumeTOC.SEGMENT_SIZE);
            }

            return true;
        }

        /// <summary>
        /// Unpacks all the files within the volume.
        /// </summary>
        public void UnpackFiles(IEnumerable<int> fileIndexesToExtract)
        {
            if (fileIndexesToExtract is null)
                fileIndexesToExtract = Enumerable.Empty<int>();

            // Lazy way
            var files = TableOfContents.GetAllRegisteredFileMap();
            foreach (var file in files)
            {
                if (!fileIndexesToExtract.Any() || fileIndexesToExtract.Contains((int)file.Value.EntryIndex))
                    UnpackFile(file.Key, file.Value);
            }
        }

        public void UnpackFile(string volPath, FileEntryKey entry)
        {
            FileInfoKey nodeKey = TableOfContents.FileInfos.GetByFileIndex(entry.EntryIndex);
            UnpackFile(nodeKey, volPath, Path.Combine(OutputDirectory, volPath));
        }

        public void PackFiles(string outrepackDir, List<string> filesToRemove, bool packAllAsNew, string customTitleID)
        {
            if (FilesToPack.Count == 0 && filesToRemove.Count == 0)
            {
                Program.Log("[X] Found no files to pack or remove from volume.", forceConsolePrint: true);
                Console.WriteLine("[?] Continue? (Y/N)");
                if (Console.ReadKey().Key != ConsoleKey.Y)
                    return;
            }

            var sw = Stopwatch.StartNew();

            // Leftover?
            if (Directory.Exists($"{outrepackDir}_temp"))
                Directory.Delete($"{outrepackDir}_temp", true);

            // Create temp to make sure we aren't transfering user leftovers
            Directory.CreateDirectory($"{outrepackDir}_temp");

            if (CreateBDMARK)
                Directory.CreateDirectory("PDIPFS_bdmark");

            Program.Log($"[-] Preparing to pack {FilesToPack.Count} files, and remove {filesToRemove.Count} files");
            PackCache newCache = TableOfContents.PackFilesForPatchFileSystem(FilesToPack, _packCache, filesToRemove, outrepackDir, packAllAsNew);
            if (UsePackingCache)
                newCache.Save(".pack_cache");

            // Delete main one if needed
            if (Directory.Exists(outrepackDir))
                Directory.Delete(outrepackDir, true);

            Directory.Move($"{outrepackDir}_temp", outrepackDir);

            Program.Log($"[-] Verifying and fixing Table of Contents segment sizes if needed");
            if (!TableOfContents.TryCheckAndFixInvalidSegmentIndexes())
                Program.Log($"[-] Re-ordered segment indexes.");
            else
                Program.Log($"[/] Segment sizes are correct.");

            if (packAllAsNew)
                Program.Log($"[-] Packing as new: New TOC Entry Index is {VolumeHeader.TOCEntryIndex}.");

            Program.Log($"[-] Saving Table of Contents ({PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.TOCEntryIndex)})");
            TableOfContents.SaveToPatchFileSystem(outrepackDir, out uint compressedSize, out uint uncompressedSize);

            if (!string.IsNullOrEmpty(customTitleID) && customTitleID.Length <= 128)
            {
                VolumeHeader.HasCustomGameID = true;
                VolumeHeader.TitleID = customTitleID;
            }

            VolumeHeader.CompressedTOCSize = compressedSize;
            VolumeHeader.TOCSize = uncompressedSize;
            VolumeHeader.TotalVolumeSize = TableOfContents.GetTotalPatchFileSystemSize(compressedSize);

            Program.Log($"[-] Saving main volume header ({PDIPFSPathResolver.Default})");
            byte[] header = VolumeHeader.Serialize();

            Span<uint> headerBlocks = MemoryMarshal.Cast<byte, uint>(header);
            Keyset.EncryptBlocks(headerBlocks, headerBlocks);
            CryptoUtils.CryptBuffer(Keyset, header, header, BASE_VOLUME_ENTRY_INDEX);

            string headerPath = Path.Combine(outrepackDir, PDIPFSPathResolver.Default);
            Directory.CreateDirectory(Path.GetDirectoryName(headerPath));

            File.WriteAllBytes(headerPath, header);

            sw.Stop();
            Program.Log($"[/] Done packing in {sw.Elapsed}.", forceConsolePrint: true);
        }

        /// <summary>
        /// Reads the specified packing cache, used to speed up the packing process by ignoring files that are already properly packed..
        /// </summary>
        /// <param name="path"></param>
        public void ReadPackingCache(string path)
        {
            using var ts = File.OpenText(path);
            while (!ts.EndOfStream)
            {
                var entry = new PackedCacheEntry();
                string line = ts.ReadLine();
                string[] args = line.Split("\t");
                entry.VolumePath = args[0];
                entry.FileIndex = uint.Parse(args[1]);
                entry.LastModified = DateTime.Parse(args[2]);
                entry.FileSize = long.Parse(args[3]);
                entry.CompressedFileSize = long.Parse(args[4]);
                _packCache.Entries.Add(entry.VolumePath, entry);
            }
        }

        public void RegisterEntriesToRepack(string inputDir, List<string> filesToIgnore)
        {
            string[] fileNames = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories);
            Array.Sort(fileNames, StringComparer.OrdinalIgnoreCase);
            var files = fileNames.Select(fileName => new FileInfo(fileName))
                //.OrderBy(e => e.FullName)
                //.OrderBy(file => file.LastWriteTime) // For cache purposes, important!
                .ToArray();

            foreach (var file in files)
            {
                var entry = new InputPackEntry();
                entry.FullPath = file.ToString();
                entry.VolumeDirPath = entry.FullPath.AsSpan(inputDir.Length).TrimStart('\\').ToString().Replace('\\', '/');
                if (filesToIgnore.Contains(entry.VolumeDirPath))
                {
                    Program.Log($"[:] Ignoring: '{entry.VolumeDirPath}'");
                    continue;
                }

                entry.FileSize = file.Length;

                entry.LastModified = new DateTime(file.LastWriteTime.Year, file.LastWriteTime.Month, file.LastWriteTime.Day,
                    file.LastWriteTime.Hour, file.LastWriteTime.Minute, file.LastWriteTime.Second, DateTimeKind.Unspecified);
                FilesToPack.Add(entry.VolumeDirPath, entry);
            }
        }

        /// <summary>
        /// Unpacks a file node.
        /// </summary>
        /// <param name="nodeKey">Info of the file.</param>
        /// <param name="entryPath">Entry path of the file.</param>
        /// <param name="filePath">Local file path of the file.</param>
        /// <returns></returns>
        public bool UnpackFile(FileInfoKey nodeKey, string entryPath, string filePath)
        {
            ulong offset = DataOffset + (ulong)nodeKey.SegmentIndex * GTVolumeTOC.SEGMENT_SIZE;
            uint uncompressedSize = nodeKey.UncompressedSize;

            if (!IsPatchVolume)
            {
                if (NoUnpack)
                    return false;

                return UnpackVolumeFile(nodeKey, filePath, offset, uncompressedSize);
            }
            else
                return UnpackPFSFile(nodeKey, entryPath, filePath, uncompressedSize);
        }

        private bool UnpackPFSFile(FileInfoKey nodeKey, string entryPath, string filePath, uint uncompressedSize)
        {
            string patchFilePath = PDIPFSPathResolver.GetPathFromSeed(nodeKey.FileIndex, IsGT5PDemoStyle);
            string localPath = this.PatchVolumeFolder + "/" + patchFilePath;

            if (NoUnpack)
                return false;

            /* I'm really not sure if there's a better way to do this.
            * Volume files, at least nodes don't seem to even store any special flag whether
            * it is located within an actual volume file or a patch volume. The only thing that is different is the sector index.. Sometimes node index when it's updated
            * It's slow, but somewhat works I guess..
            * */
            if (!File.Exists(localPath))
                return false;

            Program.Log($"[:] Unpacking: {patchFilePath} -> {filePath}");
            using var fs = new FileStream(localPath, FileMode.Open);
            if (fs.Length >= 7)
            {
                Span<byte> magic = stackalloc byte[6];
                fs.Read(magic);
                if (Encoding.ASCII.GetString(magic).StartsWith("BSDIFF"))
                {
                    Program.Log($"[X] Detected BSDIFF file for {filePath} ({patchFilePath}), can not unpack yet. (fileID {nodeKey.FileIndex})", forceConsolePrint: true);
                    return false;
                }

                fs.Position = 0;
            }

            if (IsGT5PDemoStyle)
            {
                bool customCrypt = nodeKey.Flags.HasFlag(FileInfoFlags.CustomSalsaCrypt)
                    || entryPath == "piece/car_thumb_M/gtr_07_01.img" // Uncompressed files, but no special flag either...
                    || entryPath == "piece/car_thumb_M/impreza_wrx_sti_07_03.img";

                Salsa20 salsa = default;

                if (customCrypt)
                {
                    if (Keyset.DecryptManager is null || !Keyset.DecryptManager.Keys.TryGetValue(entryPath, out string b64Key) || b64Key.Length < 32)
                    {
                        Program.Log($"[X] Could not find custom decryption key for {filePath}, skipping it.", forceConsolePrint: true);
                        return false;
                    }

                    byte[] key = Convert.FromBase64String(b64Key).AsSpan(0, 32).ToArray();
                    salsa = new Salsa20(key, key.Length);
                    Program.Log($"[/] Attempting to decrypt custom encrypted file {entryPath}..");
                }

                try
                {
                    if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        using (var outCompressedFile = new FileStream(filePath + ".in", FileMode.Create))
                            VolumeCrypto.DecryptOld(Keyset, fs, outCompressedFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);

                        using (var inCompressedFile = new FileStream(filePath + ".in", FileMode.Open))
                        {
                            using var outFile = new FileStream(filePath, FileMode.Create);

                            inCompressedFile.Position = 8;
                            using var ds = new DeflateStream(inCompressedFile, CompressionMode.Decompress);
                            ds.CopyTo(outFile);
                        }

                        File.Delete(filePath + ".in");
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        using (var outFile = new FileStream(filePath, FileMode.Create))
                            VolumeCrypto.DecryptOld(Keyset, fs, outFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);
                    }

                    if (customCrypt)
                        Program.Log($"[/] Successfully decrypted custom encrypted file {entryPath}.");
                }
                catch (Exception e)
                {
                    Program.Log($"[X] Failed to decrypt {entryPath} ({e.Message})");
                }
            }
            else
            {
                if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
                {
                    if (!MiscUtils.DecryptCheckCompression(fs, Keyset, nodeKey.FileIndex, uncompressedSize))
                    {
                        Program.Log($"[X] Failed to decompress file {filePath} ({patchFilePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                        return false;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    fs.Position = 0;
                    CryptoUtils.DecryptAndInflateToFile(Keyset, fs, nodeKey.FileIndex, filePath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    CryptoUtils.CryptToFile(Keyset, fs, nodeKey.FileIndex, filePath);
                }
            }

            return true;
        }

        private bool UnpackVolumeFile(FileInfoKey nodeKey, string filePath, ulong offset, uint uncompressedSize)
        {
            Stream.Position = (long)offset;
            if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                if (!MiscUtils.DecryptCheckCompression(Stream, Keyset, nodeKey.FileIndex, uncompressedSize))
                {
                    Program.Log($"[X] Failed to decompress file ({filePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                Stream.Position -= 8;
                CryptoUtils.DecryptAndInflateToFile(Keyset, Stream, nodeKey.FileIndex, uncompressedSize, filePath, false);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                CryptoUtils.CryptToFile(Keyset, Stream, nodeKey.FileIndex, uncompressedSize, filePath, false);
            }

            return true;
        }

        public static string lastEntryPath = "";
        public string GetEntryPath(FileEntryKey key, string prefix)
        {
            string entryPath = prefix;
            StringBTree nameBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.NameTreeOffset));

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                // If it's a file, find the extension aswell
                StringBTree extBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.FileExtensionTreeOffset));

                if (extBTree.TryFindIndex(key.FileExtensionIndex, out StringKey extKey) && !string.IsNullOrEmpty(extKey.Value))
                    entryPath += extKey.Value;

            }
            else if (key.Flags.HasFlag(EntryKeyFlags.Directory))
                entryPath += '/';

            lastEntryPath = entryPath;

            return entryPath;
        }

    }
}
