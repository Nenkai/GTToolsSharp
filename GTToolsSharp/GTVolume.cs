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
using GTToolsSharp.Headers;

using PDTools.Utils;
using PDTools.Compression;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents a container for all the files used within Gran Turismo games.
    /// </summary>
    public class GTVolume
    {
        public const int BASE_VOLUME_ENTRY_INDEX = 1;

        /// <summary>
        /// Keyset used to decrypt and encrypt volume files.
        /// </summary>
        public Keyset Keyset { get; private set; }

        public readonly Endian Endian;

        public string InputPath { get; set; }
        public bool IsPatchVolume { get; set; }
        public string PatchVolumeFolder { get; set; }
        public bool UsePackingCache { get; set; }
        public bool NoCompress { get; set; }
        public bool CreateBDMARK { get; set; }
        public bool IsGT5PDemoStyle { get; set; }

        public Dictionary<string, InputPackEntry> FilesToPack = new Dictionary<string, InputPackEntry>();

        /// <summary>
        /// The packing cache to use to speed up packing which ignores already properly packed files.
        /// </summary>
        private PackCache _packCache { get; set; } = new PackCache();

        public VolumeHeaderBase VolumeHeader { get; set; }
        public byte[] VolumeHeaderData { get; private set; }

        public GTVolumeTOC TableOfContents { get; set; }
        public FileDeviceVol[] SplitVolumes { get; set; }

        public uint DataOffset;
        public FileStream MainStream { get; }

        public GTVolume(FileStream sourceStream, Endian endianness)
        {
            MainStream = sourceStream;
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

            byte[] headerMagic = new byte[4];
            fs.Read(headerMagic);

            if (!vol.DecryptHeader(headerMagic, BASE_VOLUME_ENTRY_INDEX))
            {
                fs?.Dispose();
                return null;
            }

            var headerType = VolumeHeaderBase.Detect(headerMagic);
            if (headerType == VolumeHeaderType.Unknown)
            {
                fs?.Dispose();
                return null;
            }

            fs.Position = 0;
            vol.InputPath = path;
            vol.VolumeHeader = VolumeHeaderBase.Load(fs, vol, headerType);

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            Program.Log($"[>] PFS Version/Serial No: '{vol.VolumeHeader.SerialNumber}'");
            Program.Log($"[>] Table of Contents Entry Index: {vol.VolumeHeader.ToCNodeIndex}");
            Program.Log($"[>] TOC Size: {vol.VolumeHeader.CompressedTOCSize} bytes ({vol.VolumeHeader.ExpandedTOCSize} expanded)");
            if (vol.VolumeHeader is FileDeviceGTFS2Header header2)
            {
                Program.Log($"[>] Total Volume Size: {MiscUtils.BytesToString((long)header2.TotalVolumeSize)}");
                Program.Log($"[>] Title ID: '{header2.TitleID}'");
            }

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

            vol.LoadSplitVolumesIfNeeded();

            return vol;
        }

        public void LoadSplitVolumesIfNeeded()
        {
            if (VolumeHeader is not FileDeviceGTFS3Header header3)
                return;

            string inputDir = Path.GetDirectoryName(InputPath);
            SplitVolumes = new FileDeviceVol[header3.VolList.Length];

            for (int i = 0; i < header3.VolList.Length; i++)
            {
                FileDeviceGTFS3Header.VolEntry volEntry = header3.VolList[i];
                string localPath = Path.Combine(inputDir, volEntry.Name);
                if (!File.Exists(localPath))
                {
                    Console.WriteLine($"[!] Linked volume file '{volEntry.Name}' not found, will be skipped");
                    continue;
                }

                var vol = FileDeviceVol.Read(localPath);
                if (vol is null)
                {
                    Console.WriteLine($"[!] Unable to read vol file '{localPath}'.");
                    continue;
                }

                vol.Name = volEntry.Name;
                SplitVolumes[i] = vol;
            }
        }

        private void SetKeyset(Keyset keyset)
            => Keyset = keyset;

        /// <summary>
        /// Decrypts the header of the main volume file, using a provided seed.
        /// </summary>
        /// <param name="headerData"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public bool DecryptHeader(Span<byte> headerData, uint seed)
        {
            if (VolumeHeader is FileDeviceGTFSHeader)
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

        private bool DecryptTOC()
        {
            if (VolumeHeader is null)
                throw new InvalidOperationException("Header was not yet loaded");

            if (Keyset.Key.Data is null || Keyset.Key.Data.Length < 4)
                return false;

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.ToCNodeIndex, IsGT5PDemoStyle);

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
                Program.Log($"[-] TOC Entry is {VolumeHeader.ToCNodeIndex} which is at {path} - decrypting it", true);
                if (!IsGT5PDemoStyle)
                    CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.ToCNodeIndex);
                else
                    GT5POldCrypto.DecryptPass(VolumeHeader.ToCNodeIndex, data, data, (int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] Decompressing TOC file..", true);
                if (!PS2ZIP.TryInflateInMemory(data, VolumeHeader.ExpandedTOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Location = path;
                TableOfContents.Data = deflatedData;
            }
            else
            {
                MainStream.Seek(GTVolumeTOC.SECTOR_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(MainStream);
                byte[] data = br.ReadBytes((int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] TOC Entry is {VolumeHeader.ToCNodeIndex} which is at offset {GTVolumeTOC.SECTOR_SIZE}", true);

                // Decrypt it with the seed that the main header gave us
                CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.ToCNodeIndex);

                Program.Log($"[-] Decompressing TOC within volume.. (offset: {GTVolumeTOC.SECTOR_SIZE})", true);
                if (!PS2ZIP.TryInflateInMemory(data, VolumeHeader.ExpandedTOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Data = deflatedData;

                DataOffset = MiscUtils.AlignValue(GTVolumeTOC.SECTOR_SIZE + VolumeHeader.CompressedTOCSize, GTVolumeTOC.SECTOR_SIZE);
            }

            return true;
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
            if (!TableOfContents.TryCheckAndFixInvalidSectorIndexes())
                Program.Log($"[-] Re-ordered segment indexes.");
            else
                Program.Log($"[/] Segment sizes are correct.");

            if (packAllAsNew)
                Program.Log($"[-] Packing as new: New TOC Entry Index is {VolumeHeader.ToCNodeIndex}.");

            Program.Log($"[-] Saving Table of Contents ({PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.ToCNodeIndex)})");
            TableOfContents.SaveToPatchFileSystem(outrepackDir, out uint compressedSize, out uint uncompressedSize);

            if (VolumeHeader is FileDeviceGTFS2Header header2)
            {
                if (!string.IsNullOrEmpty(customTitleID) && customTitleID.Length <= 128)
                    header2.TitleID = customTitleID;

                header2.TotalVolumeSize = TableOfContents.GetTotalPatchFileSystemSize(compressedSize);
            }

            VolumeHeader.CompressedTOCSize = compressedSize;
            VolumeHeader.ExpandedTOCSize = uncompressedSize;
            

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

        public static string lastEntryPath = "";
        public string GetEntryPath(FileEntryKey key, string prefix)
        {
            string entryPath = prefix;
            StringBTree nameBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.NameTreeOffset), TableOfContents);

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                // If it's a file, find the extension aswell
                StringBTree extBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.FileExtensionTreeOffset), TableOfContents);

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
