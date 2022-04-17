using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using GTToolsSharp.BinaryPatching;
using GTToolsSharp.BTree;
using GTToolsSharp.Headers;
using GTToolsSharp.Utils;

using PDTools.Utils;
using PDTools.Crypto;
using PDTools.GrimPFS;

namespace GTToolsSharp
{
    public class PatchFileSystemBuilder
    {
        private GTVolume _volume { get; set; }
        private GTVolumeTOC _toc { get; set; }
        private VolumeHeaderBase _volumeHeader => _volume.VolumeHeader;

        /// <summary>
        /// Difference of files between a volume and a newer one
        /// </summary>
        public UpdateNodeInfo UpdateNodeInfo { get; private set; }

        /// <summary>
        /// File summary of the patch
        /// </summary>
        public GrimPatch Patch { get; private set; }

        public Dictionary<string, InputPackEntry> FilesToPack = new Dictionary<string, InputPackEntry>();
        public bool PackAllAsNewEntries { get; set; }
        public bool CreateBDMark { get; set; }
        public bool CreateUpdateNodeInfo { get; set; }
        public bool CreatePatchSequence { get; set; }
        public bool UsePackingCache { get; set; }
        public bool NoCompress { get; set; }
        public bool GrimPatch { get; set; }

        public ulong OldSerial { get; set; }
        public ulong NewSerial { get; set; }

        /// <summary>
        /// The packing cache to use to speed up packing which ignores already properly packed files.
        /// </summary>
        private PackCache _packCache { get; set; } = new PackCache();

        public PatchFileSystemBuilder(GTVolume parentVolume)
        {
            _volume = parentVolume;
            _toc = _volume.TableOfContents;
        }

        /// <summary>
        /// Registers all the files that should be packed from an input directory.
        /// </summary>
        /// <param name="inputDir"></param>
        /// <param name="filesToIgnore"></param>
        /// <param name="doMD5"></param>
        public void RegisterFilesToPackFromDirectory(string inputDir, List<string> filesToIgnore, bool doMD5 = false)
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


                Debug.Assert(entry.FileSize < uint.MaxValue, "Max file size must not be above 4gb (max unsigned integer)");
                entry.FileSize = (uint)file.Length;

                entry.LastModified = new DateTime(file.LastWriteTime.Year, file.LastWriteTime.Month, file.LastWriteTime.Day,
                    file.LastWriteTime.Hour, file.LastWriteTime.Minute, file.LastWriteTime.Second, DateTimeKind.Unspecified);
                FilesToPack.Add(entry.VolumeDirPath, entry);

                if (doMD5)
                {
                    using (var md5 = MD5.Create())
                    {
                        using (var stream = File.OpenRead(entry.FullPath))
                        {
                            var hash = md5.ComputeHash(stream);
                            entry.MD5Checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Builds the new patch file system.
        /// </summary>
        /// <param name="outrepackDir"></param>
        /// <param name="filesToRemove"></param>
        /// <param name="customTitleID"></param>
        public void Build(string outrepackDir, List<string> filesToRemove, string customTitleID)
        {
            if (FilesToPack.Count == 0 && filesToRemove.Count == 0)
            {
                Program.Log("[X] Found no files to pack or remove from volume.", forceConsolePrint: true);
                Console.WriteLine("[?] Continue? (Y/N)");
                if (Console.ReadKey().Key != ConsoleKey.Y)
                    return;
            }

            OldSerial = _volumeHeader.SerialNumber;
            if (NewSerial == 0)
            {
                var now = DateTimeOffset.UtcNow;
                NewSerial = (ulong)(now - TimeSpan.FromSeconds(978307200)).ToUnixTimeSeconds();
                Program.Log($"[-] PFS Serial set to today's date -> {NewSerial} ({now})");
            }
            else
            {
                Program.Log($"[-] PFS Serial forced as ({NewSerial})");
            }

            _volumeHeader.SerialNumber = NewSerial;

            if (GrimPatch)
            {
                if (_volumeHeader is not FileDeviceGTFS2Header)
                {
                    Program.Log("[X] Grim patch can not be generated for non GT5P/5/6 volumes.", forceConsolePrint: true);
                    return;
                }

                if (!CreateUpdateNodeInfo)
                {
                    Program.Log("[!] Grim patch usually requires UPDATENODEINFO to be created but argument was not provided. Continue? [Y/N]", forceConsolePrint: true);
                    if (Console.ReadKey().Key != ConsoleKey.Y)
                        return;
                }

                if (NewSerial <= OldSerial)
                {
                    Program.Log($"[X] Volume version argument is set but be above the current volume's serial ({OldSerial}).", forceConsolePrint: true);
                    return;
                }
            }

            var sw = Stopwatch.StartNew();

            // Leftover?
            if (Directory.Exists($"{outrepackDir}_temp"))
                Directory.Delete($"{outrepackDir}_temp", true);

            // Create temp to make sure we aren't transfering user leftovers
            Directory.CreateDirectory($"{outrepackDir}_temp");

            if (CreateBDMark)
                Directory.CreateDirectory("PDIPFS_bdmark");


            Program.Log($"[-] Preparing to pack {FilesToPack.Count} file(s), and remove {filesToRemove.Count} file(s)");
            PackCache newCache = PackFilesForPatchFileSystem(FilesToPack, _packCache, filesToRemove, outrepackDir);
            if (UsePackingCache)
                newCache.Save(".pack_cache");

            // Delete main one if needed
            if (Directory.Exists(outrepackDir))
                Directory.Delete(outrepackDir, true);

            Directory.Move($"{outrepackDir}_temp", outrepackDir);

            Program.Log($"[-] Verifying and fixing Table of Contents segment sizes if needed");
            if (!_toc.TryCheckAndFixInvalidSectorIndexes())
                Program.Log($"[-] Re-ordered segment indexes.");
            else
                Program.Log($"[/] Segment sizes are correct.");

            if (PackAllAsNewEntries)
                Program.Log($"[-] Packing as new: New TOC Entry Index is {_volumeHeader.ToCNodeIndex}.");

            Program.Log($"[-] Saving Table of Contents -> {PDIPFSPathResolver.GetPathFromSeed(_volumeHeader.ToCNodeIndex)}");
            string tocMd5 = _toc.SaveToPatchFileSystem(outrepackDir, out uint compressedSize, out uint uncompressedSize);

            if (_volumeHeader is FileDeviceGTFS2Header header2)
            {
                if (!string.IsNullOrEmpty(customTitleID) && customTitleID.Length <= 128)
                    header2.TitleID = customTitleID;

                header2.TotalVolumeSize = _toc.GetTotalPatchFileSystemSize(compressedSize);
            }

            _volumeHeader.CompressedTOCSize = compressedSize;
            _volumeHeader.ExpandedTOCSize = uncompressedSize;

            Program.Log($"[-] Saving Main Volume Header -> {PDIPFSPathResolver.Default}");
            byte[] header = _volumeHeader.Serialize();

            Span<uint> headerBlocks = MemoryMarshal.Cast<byte, uint>(header);
            _volume.Keyset.EncryptBlocks(headerBlocks, headerBlocks);
            CryptoUtils.CryptBuffer(_volume.Keyset, header, header, GTVolume.BASE_VOLUME_ENTRY_INDEX);

            string headerPath = Path.Combine(outrepackDir, PDIPFSPathResolver.Default);
            Directory.CreateDirectory(Path.GetDirectoryName(headerPath));

            File.WriteAllBytes(headerPath, header);

            if (UpdateNodeInfo is not null)
            {
                Program.Log($"[-] Creating UPDATENODEINFO (for {UpdateNodeInfo.Entries.Count} node(s) updated)");
                var tocNode = UpdateNodeInfo.Entries[_volumeHeader.ToCNodeIndex];
                tocNode.CurrentEntryIndex = 0;
                tocNode.NewCompressedFileSize = compressedSize;
                tocNode.NewEntryIndex = _volumeHeader.ToCNodeIndex;
                tocNode.NewFileSize = compressedSize;
                tocNode.MD5Checksum = tocMd5;
                UpdateNodeInfo.WriteNodeInfo(Path.Combine(outrepackDir, "UPDATENODEINFO"));
            }

            if (CreatePatchSequence)
            {
                Program.Log($"[-] Creating PATCHSEQUENCE [{OldSerial} -> {NewSerial}]");
                CreatePatchSequenceFile(Path.Combine(outrepackDir, "PATCHSEQUENCE"), OldSerial, NewSerial);
            }

            if (Patch is not null)
            {
                Program.Log($"[-] Creating Grim Patch");
                Patch.Save(Path.Combine(outrepackDir, "PatchInfo.txt"), GTVolume.BASE_VOLUME_ENTRY_INDEX, _volumeHeader.ToCNodeIndex);
            }

            sw.Stop();
            Program.Log($"[/] Done packing in {sw.Elapsed}.", forceConsolePrint: true);
        }

        /// <summary>
        /// Pack all provided files and edit the table of contents accordingly.
        /// </summary>
        /// <param name="FilesToPack">Files to pack.</param>
        /// <param name="outputDir">Main output dir to use to expose the packed files.</param>
        public PackCache PackFilesForPatchFileSystem(Dictionary<string, InputPackEntry> FilesToPack, PackCache packCache, List<string> filesToRemove, string outputDir)
        {
            if (CreateUpdateNodeInfo)
                UpdateNodeInfo = new UpdateNodeInfo();

            if (GrimPatch)
                Patch = new GrimPatch((_volumeHeader as FileDeviceGTFS2Header).TitleID, OldSerial, NewSerial);

            // If we are packing as new, ensure the TOC is before all the files (that will come after it)
            if (PackAllAsNewEntries)
            {
                _volume.VolumeHeader.ToCNodeIndex = _toc.NextEntryIndex();
                UpdateNodeInfo?.Entries?.Add(_volume.VolumeHeader.ToCNodeIndex, new NodeInfo());
            }
            

            if (filesToRemove.Count > 0)
                _toc.RemoveFiles(filesToRemove);

            var newCache = new PackCache();
            if (FilesToPack.Count > 0)
            {
                // Pick up files we're going to add if there's any
                PreRegisterNewFilesToPack(FilesToPack);

                Dictionary<string, FileEntryKey> tocFiles = _toc.GetAllRegisteredFileMap();

                // Pack Non-Added files first
                foreach (var tocFile in tocFiles)
                {
                    if (FilesToPack.TryGetValue(tocFile.Key, out InputPackEntry file) && !file.IsAddedFile)
                        PackFile(packCache, outputDir, newCache, tocFile.Value, file);
                }

                // Pack then added files
                foreach (var addedFile in FilesToPack.Where(e => e.Value.IsAddedFile))
                {
                    var tocFile = tocFiles[addedFile.Value.VolumeDirPath];
                    PackFile(packCache, outputDir, newCache, tocFile, addedFile.Value);
                }
            }

            return newCache;
        }

        /// <summary>
        /// Packs a single volume file.
        /// </summary>
        /// <param name="packCache"></param>
        /// <param name="outputDir"></param>
        /// <param name="newCache"></param>
        /// <param name="tocFile"></param>
        /// <param name="file"></param>
        private void PackFile(PackCache packCache, string outputDir, PackCache newCache, FileEntryKey tocFile, InputPackEntry file)
        {
            FileInfoKey key = _toc.FileInfos.GetByFileIndex(tocFile.EntryIndex);

            // For potential patching
            var nodeInfo = new NodeInfo();
            nodeInfo.OldFileInfoFlags = TPPSFileState.Overwrite; // Change this when implementing making binary patches?
            nodeInfo.NewFileInfoFlags = TPPSFileState.Overwrite;
            nodeInfo.NewEntryIndex = key.FileIndex;
            nodeInfo.NewFileSize = file.FileSize;
            nodeInfo.MD5Checksum = file.MD5Checksum;

            if (PackAllAsNewEntries && !file.IsAddedFile)
            {
                uint oldEntryFileIndex = key.FileIndex;
                key = _toc.ModifyExistingEntryAsNew(key, file.VolumeDirPath);

                nodeInfo.NewEntryIndex = key.FileIndex;

                Program.Log($"[:] Entry key for {file.VolumeDirPath} changed as new: {oldEntryFileIndex} -> {key.FileIndex}");

                if (nodeInfo.OldFileInfoFlags == TPPSFileState.BinaryPatchBase)
                    nodeInfo.CurrentEntryIndex = oldEntryFileIndex;
            }


            uint newUncompressedSize = file.FileSize;
            uint newCompressedSize = file.FileSize;
            string pfsFilePath = PDIPFSPathResolver.GetPathFromSeed(tocFile.EntryIndex);

            if (CreateBDMark)
            {
                Directory.CreateDirectory(Path.Combine("PDIPFS_bdmark", Path.GetDirectoryName(pfsFilePath)));
                using var bdmarkfile = File.Create(Path.Combine("PDIPFS_bdmark", pfsFilePath));
            }

            // Check for cached file
            if (UsePackingCache && packCache.HasValidCachedEntry(file, key.FileIndex, out PackedCacheEntry validCacheEntry))
            {
                string oldFilePath = Path.Combine(outputDir, pfsFilePath);
                if (File.Exists(oldFilePath))
                {
                    newCache.Entries.Add(file.VolumeDirPath, validCacheEntry);
                    Program.Log($"[:] Pack: {file.VolumeDirPath} found in cache file, does not need compressing/encrypting");

                    string movePath = Path.Combine($"{outputDir}_temp", pfsFilePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(movePath));
                    File.Move(oldFilePath, Path.Combine($"{outputDir}_temp", pfsFilePath));
                    _toc.UpdateKeyAndRetroactiveAdjustSectors(key, (uint)validCacheEntry.CompressedFileSize, (uint)validCacheEntry.FileSize);
                    return;
                }
                else
                {
                    Program.Log($"[:] Pack: {file.VolumeDirPath} found in cache file but actual file is missing ({pfsFilePath}) - recreating it");
                }
            }

            FileStream fs;
            while (true)
            {
                try
                {
                    fs = File.Open(file.FullPath, FileMode.Open);
                    break;
                }
                catch (Exception e)
                {
                    Program.Log($"[!] {file.FullPath} could not be opened: {e.Message}");
                    Program.Log($"[!] Press any key to try again.");
                    Console.ReadKey();
                }
            }


            if (key.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                bool compressable = IsCompressableFile(file.VolumeDirPath);
                if (!compressable || (compressable && NoCompress))
                    key.Flags &= ~FileInfoFlags.Compressed;
            }

            string outputFile = Path.Combine($"{outputDir}_temp", pfsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            if (key.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                Program.Log($"[:] Pack: Compressing + Encrypting {file.VolumeDirPath} -> {pfsFilePath}");
                newCompressedSize = CryptoUtils.EncryptAndDeflateToFile(_volume.Keyset, fs, key.FileIndex, outputFile, closeStream: true);
                nodeInfo.NewCompressedFileSize = newCompressedSize;
            }
            else
            {
                Program.Log($"[:] Pack: Encrypting {file.VolumeDirPath} -> {pfsFilePath}");
                CryptoUtils.EncryptToFile(_volume.Keyset, fs, key.FileIndex, outputFile, closeStream: true);
            }

            // Will also update the ones we pre-registered
            _toc.UpdateKeyAndRetroactiveAdjustSectors(key, newCompressedSize, newUncompressedSize);

            if (UsePackingCache)
            {
                // Add to our new cache
                var newCacheEntry = new PackedCacheEntry()
                {
                    FileIndex = tocFile.EntryIndex,
                    FileSize = newUncompressedSize,
                    LastModified = file.LastModified,
                    VolumePath = file.VolumeDirPath,
                    CompressedFileSize = newCompressedSize,
                };

                newCache.Entries.Add(file.VolumeDirPath, newCacheEntry);
            }

            if (GrimPatch && _volumeHeader is FileDeviceGTFS2Header header)
                Patch.AddFile(file.VolumeDirPath, pfsFilePath, nodeInfo.NewEntryIndex, key.CompressedSize);

            if (UpdateNodeInfo is not null)
                UpdateNodeInfo.Entries.Add(nodeInfo.NewEntryIndex, nodeInfo);
        }


        private void CreatePatchSequenceFile(string outPath, ulong baseSerial, ulong targetSerial)
        {
            using var sw = new StreamWriter(outPath);
            sw.WriteLine(baseSerial);
            sw.WriteLine(targetSerial);
        }

        /// <summary>
        /// Add files to be registered within the table of contents later on and their sizes filled.
        /// </summary>
        /// <param name="FilesToPack"></param>
        private void PreRegisterNewFilesToPack(Dictionary<string, InputPackEntry> FilesToPack)
        {
            Dictionary<string, FileEntryKey> tocFiles = _toc.GetAllRegisteredFileMap();

            // Add Files, these files will have their sizes adjusted later on during repack process
            foreach (var file in FilesToPack)
            {
                if (!tocFiles.ContainsKey(file.Key))
                {
                    Program.Log($"[:] Pack: Adding new file to TOC: {file.Key}");
                    _toc.AddNewFile(file.Key);
                    file.Value.IsAddedFile = true;
                }
            }
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

        /// <summary>
        /// Whether a path is a file that must be compressed.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private bool IsCompressableFile(string path)
        {
            if (path.StartsWith("crs/") && path.EndsWith("stream")) // Stream files should NOT be compressed. Else they don't load.
                return false;

            if (path.EndsWith(".pam")) // Movies
                return false;

            if (path.EndsWith(".mpackage")) // Adhoc Packages - Already compressed
                return false;

            if (path.EndsWith(".png")) // PNG Images - Already compressed
                return false;

            if (path.EndsWith(".vec")) // Vector Fonts - Bit compressed
                return false;

            if (path.StartsWith("car/") && (path.EndsWith("body_s") || path.EndsWith("interior_s")) ) // Car Streams - Zlib compressed
                return false;

            if (path.StartsWith("sound_gt") || path.EndsWith(".sgd") || path.EndsWith(".esgx")) // Music - Bit compressed
                return false;

            if ((path.StartsWith("database") || path.StartsWith("specdb")) && path.EndsWith(".dat")) // Databases - Anything possibly SQLite
                return false;

            if (path.EndsWith(".fgp")) // Game Parameter Caches
                return false;

            if (path.EndsWith(".ted")) // Track Editor files
                return false;

            return true;
        }
    }
}
