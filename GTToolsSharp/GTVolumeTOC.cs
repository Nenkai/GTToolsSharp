using System;
using System.Collections.Generic;
using System.Linq;

using System.IO;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Headers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using PDTools.Utils;
using PDTools.Compression;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents multiple B-Trees designing file offsets within an entire volume.
    /// </summary>
    public class GTVolumeTOC
    {
        private readonly static byte[] TOC_MAGIC_BE = { 0x5B, 0x74, 0x51, 0x6E };
        private readonly static byte[] TOC_MAGIC_LE = { 0x6E, 0x51, 0x74, 0x5B };

        public const int SECTOR_SIZE = 2048;

        public string Location { get; set; }
        public byte[] Data { get; set; }

        public uint NameTreeOffset { get; set; }
        public uint FileExtensionTreeOffset { get; set; }
        public uint NodeTreeOffset { get; set; }
        public List<uint> RootAndFolderOffsets { get; set; }

        public StringBTree FileNames { get; private set; }
        public StringBTree Extensions { get; private set; }
        public FileInfoBTree FileInfos { get; private set; }
        public List<FileEntryBTree> Files { get; private set; }

        public VolumeHeaderBase ParentHeader { get; }
        public GTVolume ParentVolume { get; }

        public BTreeEndian TreeEndian { get; private set; }

        public GTVolumeTOC(VolumeHeaderBase parentHeader, GTVolume parentVolume)
        {
            ParentHeader = parentHeader;
            ParentVolume = parentVolume;
        }

        /// <summary>
        /// Loads the table of contents from the underlaying stream.
        /// </summary>
        /// <returns></returns>
        public bool LoadTOC()
        {
            var sr = new SpanReader(Data, Endian.Big);
            byte[] magic = sr.ReadBytes(4);
            if (magic.AsSpan().SequenceEqual(TOC_MAGIC_BE.AsSpan()))
                sr.Endian = Endian.Big;
            else if (magic.AsSpan().SequenceEqual(TOC_MAGIC_LE.AsSpan()))
                sr.Endian = Endian.Little;
            else
            {
                Program.Log($"Volume TOC magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2")))})");
                return false;
            }

            NameTreeOffset = sr.ReadUInt32();
            FileExtensionTreeOffset = sr.ReadUInt32();
            NodeTreeOffset = sr.ReadUInt32();
            uint entryTreeCount = sr.ReadUInt32();

            RootAndFolderOffsets = new List<uint>((int)entryTreeCount);
            for (int i = 0; i < entryTreeCount; i++)
                RootAndFolderOffsets.Add(sr.ReadUInt32());

            FileNames = new StringBTree(Data.AsMemory((int)NameTreeOffset), this);
            if (!ParentVolume.IsGT5PDemoStyle)
                FileNames.LoadEntries();
            else
                FileNames.LoadEntriesOld();

            Extensions = new StringBTree(Data.AsMemory((int)FileExtensionTreeOffset), this);
            if (!ParentVolume.IsGT5PDemoStyle)
                Extensions.LoadEntries();
            else
                Extensions.LoadEntriesOld();

            FileInfos = new FileInfoBTree(Data.AsMemory((int)NodeTreeOffset), this);
            if (!ParentVolume.IsGT5PDemoStyle)
                FileInfos.LoadEntries();
            else
                FileInfos.LoadEntriesOld();

            Files = new List<FileEntryBTree>((int)entryTreeCount);

            for (int i = 0; i < entryTreeCount; i++)
            {
                Files.Add(new FileEntryBTree(Data.AsMemory((int)RootAndFolderOffsets[i]), this));
                if (!ParentVolume.IsGT5PDemoStyle)
                    Files[i].LoadEntries();
                else
                    Files[i].LoadEntriesOld();
            }

            return true;
        }

        /// <summary>
        /// Saves the table of contents to a Patch File System file.
        /// </summary>
        /// <param name="outputDir"></param>
        /// <param name="compressedTocSize"></param>
        /// <param name="uncompressedSize"></param>
        public void SaveToPatchFileSystem(string outputDir, out uint compressedTocSize, out uint uncompressedSize)
        {
            byte[] tocSerialized = Serialize();
            byte[] compressedToc = PS2ZIP.Deflate(tocSerialized);

            uncompressedSize = (uint)tocSerialized.Length;
            compressedTocSize = (uint)compressedToc.Length;

            CryptoUtils.CryptBuffer(ParentVolume.Keyset, compressedToc, compressedToc, ParentHeader.ToCNodeIndex);

            string path = Path.Combine(outputDir, PDIPFSPathResolver.GetPathFromSeed(ParentHeader.ToCNodeIndex));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, compressedToc);
        }

        /// <summary>
        /// Serializes the table of contents and its B-Trees.
        /// </summary>
        public byte[] Serialize()
        {
            var bs = new BitStream(BitStreamMode.Write, 1024);

            bs.WriteByteData(TOC_MAGIC_BE);
            bs.SeekToByte(0x10);
            bs.WriteUInt32((uint)Files.Count);
            bs.Position += sizeof(uint) * Files.Count;

            uint fileNamesOffset = (uint)bs.Position;
            FileNames.Serialize(ref bs, this);

            uint extOffset = (uint)bs.Position;
            Extensions.Serialize(ref bs, this);

            uint fileInfoOffset = (uint)bs.Position;
            FileInfos.Serialize(ref bs, this);

            // The list of file entry btrees mostly consist of the relation between files, folder, extensions and data
            // Thus it is writen at the end
            // Each tree is a subdir
            const int baseListPos = 20;
            for (int i = 0; i < Files.Count; i++)
            {
                FileEntryBTree f = Files[i];
                uint treeOffset = (uint)bs.Position;
                bs.Position = baseListPos + (i * sizeof(uint));
                bs.WriteUInt32(treeOffset);
                bs.Position = (int)treeOffset;

                f.Serialize(ref bs, this);
            }

            // Go back to write the meta data
            bs.Position = 4;
            bs.WriteUInt32(fileNamesOffset);
            bs.WriteUInt32(extOffset);
            bs.WriteUInt32(fileInfoOffset);
            return bs.GetSpan().ToArray();
        }

        public void RemoveFilesFromTOC(List<string> filesToRemove)
        {
            Dictionary<string, FileEntryKey> tocFiles = GetAllRegisteredFileMap();
            foreach (var file in filesToRemove)
            {
                if (tocFiles.TryGetValue(file, out FileEntryKey fileEntry))
                {
                    Program.Log($"[:] Pack: Removing file from TOC: {file}");
                    if (!TryRemoveFile(FileInfos.GetByFileIndex(fileEntry.EntryIndex)))
                        Program.Log($"[X] Pack: Attempted to remove file {file}, but did not exist in volume");
                }
            }
        }

        /// <summary>
        /// Pack all provided files and edit the table of contents accordingly.
        /// </summary>
        /// <param name="FilesToPack">Files to pack.</param>
        /// <param name="outputDir">Main output dir to use to expose the packed files.</param>
        public PackCache PackFilesForPatchFileSystem(Dictionary<string, InputPackEntry> FilesToPack, PackCache packCache, List<string> filesToRemove, string outputDir, bool packAllAsNewEntries)
        {
            // If we are packing as new, ensure the TOC is before all the files (that will come after it)
            if (packAllAsNewEntries)
                ParentHeader.ToCNodeIndex = NextEntryIndex();

            if (filesToRemove.Count > 0)
                RemoveFilesFromTOC(filesToRemove);

            var newCache = new PackCache();
            if (FilesToPack.Count > 0)
            {
                // Pick up files we're going to add if there's any
                PreRegisterNewFilesToPack(FilesToPack);

                Dictionary<string, FileEntryKey> tocFiles = GetAllRegisteredFileMap();

                // Pack Non-Added files first
                foreach (var tocFile in tocFiles)
                {
                    if (FilesToPack.TryGetValue(tocFile.Key, out InputPackEntry file) && !file.IsAddedFile)
                        PackFile(packCache, outputDir, packAllAsNewEntries, newCache, tocFile.Value, file);
                }

                // Pack then added files
                foreach (var addedFile in FilesToPack.Where(e => e.Value.IsAddedFile))
                {
                    var tocFile = tocFiles[addedFile.Value.VolumeDirPath];
                    PackFile(packCache, outputDir, packAllAsNewEntries, newCache, tocFile, addedFile.Value);
                }
            }

            return newCache;
        }

        private void PackFile(PackCache packCache, string outputDir, bool packAllAsNewEntries, PackCache newCache, FileEntryKey tocFile, InputPackEntry file)
        {
            FileInfoKey key = FileInfos.GetByFileIndex(tocFile.EntryIndex);

            if (packAllAsNewEntries && !file.IsAddedFile)
            {
                uint oldEntryFileIndex = key.FileIndex;
                key = ModifyExistingEntryAsNew(key, file.VolumeDirPath);
                Program.Log($"[:] Entry key for {file.VolumeDirPath} changed as new: {oldEntryFileIndex} -> {key.FileIndex}");
            }

            uint newUncompressedSize = (uint)file.FileSize;
            uint newCompressedSize = (uint)file.FileSize;
            string pfsFilePath = PDIPFSPathResolver.GetPathFromSeed(tocFile.EntryIndex);

            if (ParentVolume.CreateBDMARK)
            {
                Directory.CreateDirectory(Path.Combine("PDIPFS_bdmark", Path.GetDirectoryName(pfsFilePath)));
                using var bdmarkfile = File.Create(Path.Combine("PDIPFS_bdmark", pfsFilePath));
            }
            // Check for cached file
            if (ParentVolume.UsePackingCache && packCache.HasValidCachedEntry(file, key.FileIndex, out PackedCacheEntry validCacheEntry))
            {
                string oldFilePath = Path.Combine(outputDir, pfsFilePath);
                if (File.Exists(oldFilePath))
                {
                    newCache.Entries.Add(file.VolumeDirPath, validCacheEntry);
                    Program.Log($"[:] Pack: {file.VolumeDirPath} found in cache file, does not need compressing/encrypting");

                    string movePath = Path.Combine($"{outputDir}_temp", pfsFilePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(movePath));
                    File.Move(oldFilePath, Path.Combine($"{outputDir}_temp", pfsFilePath));
                    UpdateKeyAndRetroactiveAdjustSectors(key, (uint)validCacheEntry.CompressedFileSize, (uint)validCacheEntry.FileSize);
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


            if (ParentVolume.NoCompress)
                key.Flags &= ~FileInfoFlags.Compressed;

            string outputFile = Path.Combine($"{outputDir}_temp", pfsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            if (key.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                Program.Log($"[:] Pack: Compressing + Encrypting {file.VolumeDirPath} -> {pfsFilePath}");
                newCompressedSize = (uint)CryptoUtils.EncryptAndDeflateToFile(ParentVolume.Keyset, fs, key.FileIndex, outputFile, closeStream: true);
            }
            else
            {
                Program.Log($"[:] Pack: Encrypting {file.VolumeDirPath} -> {pfsFilePath}");
                CryptoUtils.EncryptToFile(ParentVolume.Keyset, fs, key.FileIndex, outputFile, closeStream: true);
            }

            // Will also update the ones we pre-registered
            UpdateKeyAndRetroactiveAdjustSectors(key, newCompressedSize, newUncompressedSize);

            if (ParentVolume.UsePackingCache)
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
        }

        /// <summary>
        /// Add files to be registered within the table of contents later on and their sizes filled.
        /// </summary>
        /// <param name="FilesToPack"></param>
        private void PreRegisterNewFilesToPack(Dictionary<string, InputPackEntry> FilesToPack)
        {
            Dictionary<string, FileEntryKey> tocFiles = GetAllRegisteredFileMap();

            // Add Files, these files will have their sizes adjusted later on during repack process
            foreach (var file in FilesToPack)
            {
                if (!tocFiles.ContainsKey(file.Key))
                {
                    Program.Log($"[:] Pack: Adding new file to TOC: {file.Key}");
                    RegisterFilePath(file.Key);
                    file.Value.IsAddedFile = true;
                }
            }
        }

        /// <summary>
        /// Modifies a current key as a new entry. 
        /// </summary>
        /// <param name="infoKey">Entry to modify.</param>
        /// <param name="newEntryPath">Path for the entry.</param>
        /// <returns></returns>
        private FileInfoKey ModifyExistingEntryAsNew(FileInfoKey infoKey, string newEntryPath)
        {
            // Check paths
            string[] pathParts = newEntryPath.Split(Path.AltDirectorySeparatorChar);
            FileEntryBTree currentSubTree = Files[0];

            uint newKeyIndex = NextEntryIndex();

            // Find the entry key and update it
            for (int i = 0; i < pathParts.Length; i++)
            {
                if (i != pathParts.Length - 1)
                {
                    // Check actual folders
                    int keyIndex = FileNames.GetIndexOfString(pathParts[i]);
                    if (keyIndex == -1)
                        throw new ArgumentNullException($"Entry Key for file info key ({infoKey}) has missing file name key: {pathParts[i]}");

                    FileEntryKey subTreeKey = currentSubTree.GetFolderEntryByNameIndex((uint)keyIndex);

                    if (subTreeKey is null)
                        throw new InvalidOperationException($"Tried to modify existing key {newEntryPath} (str index: {keyIndex}), but missing in entries");
                    else if (!subTreeKey.Flags.HasFlag(EntryKeyFlags.Directory))
                        throw new InvalidOperationException($"Tried to modify existing key {newEntryPath} but entry key ({subTreeKey}) is not marked as directory. Is the volume corrupted?");

                    currentSubTree = Files[(int)subTreeKey.EntryIndex];
                }
                else
                {
                    // Got the location for the subtree

                    // Get our actual file entry key
                    FileEntryKey entryKey = currentSubTree.Entries.FirstOrDefault(e => e.EntryIndex == infoKey.FileIndex);
                    if (entryKey is null)
                        throw new ArgumentNullException($"Entry Key for file info key ({infoKey}) is missing while modifying.");

                    // Update it actually
                    entryKey.EntryIndex = newKeyIndex;
                }
            }

            // Find the original entry key, copy from it, add to the tree
            foreach (FileEntryBTree tree in Files)
            {
                foreach (FileEntryKey child in tree.Entries)
                {
                    if (child.EntryIndex == infoKey.FileIndex) // If the entry key exists, add it
                    {
                        var fileInfo = new FileInfoKey(newKeyIndex);
                        fileInfo.CompressedSize = infoKey.CompressedSize;
                        fileInfo.UncompressedSize = infoKey.UncompressedSize;
                        fileInfo.SectorOffset = NextSectorIndex(); // Pushed to the end, so technically the sector is new, will be readjusted at the end anyway
                        fileInfo.Flags = infoKey.Flags;
                        FileInfos.Entries.Add(fileInfo);
                        return fileInfo;
                    }
                }
            }

            // If it wasn't found, then we already have it
            infoKey.FileIndex = newKeyIndex;

            // Move it to the last
            FileInfos.Entries.Remove(infoKey);
            FileInfos.Entries.Add(infoKey);
            return infoKey;
        }

        public bool TryCheckAndFixInvalidSectorIndexes()
        {
            bool valid = true;
            List<FileInfoKey> sectorSortedFiles = FileInfos.Entries.OrderBy(e => e.SectorOffset).ToList();
            for (int i = 0; i < sectorSortedFiles.Count - 1; i++)
            {
                FileInfoKey current = sectorSortedFiles[i];
                FileInfoKey next = sectorSortedFiles[i + 1];
                double sectorsTakenByCurrent = MathF.Ceiling(current.CompressedSize / (float)SECTOR_SIZE);
                if (next.SectorOffset != current.SectorOffset + sectorsTakenByCurrent)
                {
                    valid = false;
                    next.SectorOffset = (uint)(current.SectorOffset + sectorsTakenByCurrent);
                }
            }
            return valid;
        }

        /// <summary>
        /// Updates a file entry, and adjusts all the key sectors if needed.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="newCompressedSize"></param>
        /// <param name="newUncompressedSize"></param>
        private void UpdateKeyAndRetroactiveAdjustSectors(FileInfoKey fileInfo, uint newCompressedSize, uint newUncompressedSize)
        {
            float oldTotalSectors = MathF.Ceiling(fileInfo.CompressedSize / (float)SECTOR_SIZE);
            float newTotalSectors = MathF.Ceiling(newCompressedSize / (float)SECTOR_SIZE);
            fileInfo.CompressedSize = newCompressedSize;
            fileInfo.UncompressedSize = newUncompressedSize;
            if (oldTotalSectors != newTotalSectors)
            {
                List<FileInfoKey> orderedKeySectors = FileInfos.Entries.OrderBy(e => e.SectorOffset).ToList();
                for (int i = orderedKeySectors.IndexOf(fileInfo); i < orderedKeySectors.Count - 1; i++)
                {
                    FileInfoKey currentFileInfo = orderedKeySectors[i];
                    FileInfoKey nextFileInfo = orderedKeySectors[i + 1];
                    float sectorCount = MathF.Ceiling(currentFileInfo.CompressedSize / (float)SECTOR_SIZE);

                    // New file pushes older files beyond sector size? Update them by the amount of sectors that increases
                    if (nextFileInfo.SectorOffset != currentFileInfo.SectorOffset + sectorCount)
                        nextFileInfo.SectorOffset = (uint)(currentFileInfo.SectorOffset + sectorCount);
                }
            }
        }

        /// <summary>
        /// Registers a new global path.
        /// </summary>
        /// <param name="path"></param>
        public void RegisterFilePath(string path)
        {
            path = path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string ext = Path.GetExtension(path);

            FileInfoKey newKey = new FileInfoKey(this.NextEntryIndex());
            newKey.SectorOffset = this.NextSectorIndex();

            newKey.CompressedSize = 1; // Important for sectors to count as at least one
            newKey.UncompressedSize = 1; // Same

            if (!ParentVolume.NoCompress && IsCompressableFile(path))
                newKey.Flags |= FileInfoFlags.Compressed; 

            FileInfos.Entries.Add(newKey);
            string[] folders = path.Split(Path.AltDirectorySeparatorChar);

            uint baseDirEntryIndex = 0;
            for (uint i = 0; i < folders.Length - 1; i++) // Do not include file name
            {
                // If the dir doesn't exist, create a new one - we have a new index that is the last current folder
                if (!DirectoryExists(Files[(int)baseDirEntryIndex], folders[i], out uint parentDirEntryIndex))
                    baseDirEntryIndex = RegisterDirectory(baseDirEntryIndex, folders[i]);
                else
                    baseDirEntryIndex = parentDirEntryIndex; // Already there, update the current folder index
            }

            RegisterFile(baseDirEntryIndex, Path.GetFileNameWithoutExtension(path), ext);
        }

        /// <summary>
        /// Registers a file (not a path).
        /// </summary>
        /// <param name="entryIndex"></param>
        /// <param name="name"></param>
        /// <param name="extension"></param>
        public void RegisterFile(uint entryIndex, string name, string extension)
        {
            uint nameIndex = RegisterFilename(name);
            uint extIndex = RegisterExtension(extension); // No extension is also an entry

            var newEntry = new FileEntryKey();
            if (!string.IsNullOrEmpty(extension))
                newEntry.Flags = EntryKeyFlags.File;
            newEntry.NameIndex = nameIndex;
            newEntry.FileExtensionIndex = extIndex;
            newEntry.EntryIndex = FileInfos.Entries[^1].FileIndex;
            Files[(int)entryIndex].Entries.Add(newEntry);
            Files[(int)entryIndex].ResortByNameIndexes();
        }

        /// <summary>
        /// Registers a directory.
        /// </summary>
        /// <param name="parentDirIndex"></param>
        /// <param name="dirName"></param>
        /// <returns></returns>
        public uint RegisterDirectory(uint parentDirIndex, string dirName)
        {
            uint dirNameIndex = RegisterFilename(dirName);
            uint dirIndex = 0;

            // Grab the next file/folder entry of the one we just added, will be used for sorting
            var subDirsInBaseDir = Files[(int)parentDirIndex].Entries.Where(e => e.Flags.HasFlag(EntryKeyFlags.Directory));
            if (subDirsInBaseDir.Count() > 1)
            {
                // Find the first entry whose names appear after the current folder
                FileEntryKey k = subDirsInBaseDir.FirstOrDefault(e => e.NameIndex > dirNameIndex);
                dirIndex = k?.EntryIndex ?? 0;
            }

            if (dirIndex == 0)
            {
                if (!subDirsInBaseDir.Any())
                    dirIndex = parentDirIndex + 1; // New one
                else
                {
                    uint lastDirIndex = subDirsInBaseDir.Max(e => e.EntryIndex);

                    var subDirs = Files[(int)lastDirIndex].Entries
                        .Where(e => e.Flags.HasFlag(EntryKeyFlags.Directory));


                    lastDirIndex = Math.Max(lastDirIndex, subDirs.Any() ? subDirs.Max(t => t.EntryIndex) : 0);
                    dirIndex = lastDirIndex + 1;
                }
            }

            // Update the trees if needed
            uint currentIndex = dirIndex;
            foreach (var tree in Files)
            {
                foreach (var child in tree.Entries)
                {
                    if (child.Flags.HasFlag(EntryKeyFlags.Directory) && child.EntryIndex >= dirIndex)
                    {
                        currentIndex = child.EntryIndex;
                        child.EntryIndex = currentIndex + 1;
                    }
                }
            }

            var newEntry = new FileEntryKey();
            newEntry.Flags = EntryKeyFlags.Directory;
            newEntry.NameIndex = dirNameIndex;
            newEntry.EntryIndex = dirIndex;
            Files[(int)parentDirIndex].Entries.Add(newEntry);
            Files[(int)parentDirIndex].ResortByNameIndexes();

            // Basically add the new empty folder
            Files.Insert((int)dirIndex, new FileEntryBTree());

            return dirIndex;
        }

        public bool TryRemoveFile(FileInfoKey file)
        {
            int removed = 0;
            foreach (var tree in Files)
                removed += tree.Entries.RemoveAll(e => e.EntryIndex == file.FileIndex);

            if (removed > 0)
            {
                List<FileInfoKey> orderedKeySectors = FileInfos.Entries.OrderBy(e => e.SectorOffset).ToList();
                int fileSectorOffset = orderedKeySectors.IndexOf(file);

                // Sort it all
                FileInfoKey cur = orderedKeySectors[fileSectorOffset];
                FileInfoKey next = orderedKeySectors[fileSectorOffset + 1];
                cur.SectorOffset = next.SectorOffset;
                for (int i = orderedKeySectors.IndexOf(file); i < orderedKeySectors.Count - 1; i++)
                {
                    cur = orderedKeySectors[i];
                    next = orderedKeySectors[i + 1];
                    float sectorCountFromFile = MathF.Ceiling(cur.CompressedSize / (float)SECTOR_SIZE);

                    // Re-update sectors
                    if (next.SectorOffset != cur.SectorOffset + sectorCountFromFile)
                        next.SectorOffset = (uint)(cur.SectorOffset + sectorCountFromFile);
                }

                FileInfos.Entries.Remove(file);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all the relational files within the table of contents.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, FileEntryKey> GetAllRegisteredFileMap()
        {
            var files = new Dictionary<string, FileEntryKey>();
            uint currentIndex = 0;
            for (int i = 0; i < Files[0].Entries.Count; i++)
            {
                var currentEntry = Files[0].Entries[i];
                if (currentEntry.Flags.HasFlag(EntryKeyFlags.Directory))
                {
                    string baseDir = FileNames.GetByIndex(currentEntry.NameIndex).Value + '/';
                    uint dirIndex = currentEntry.EntryIndex;
                    TraverseNestedTreeAndList(files, baseDir, ref dirIndex, ref currentIndex);
                }
                else
                {
                    string fileName = FileNames.GetByIndex(currentEntry.NameIndex).Value;
                    if (currentEntry.FileExtensionIndex != 0)
                        fileName += Extensions.GetByIndex(currentEntry.FileExtensionIndex).Value;
                    files.Add(fileName, currentEntry);
                    currentIndex++;
                }
            }

            return files;
        }

        public void TraverseNestedTreeAndList(Dictionary<string, FileEntryKey> files, string baseDir, ref uint dirIndex, ref uint currentIndex)
        {
            string bDir = baseDir;
            uint baseDirIndex = dirIndex;
            for (int i = 0; i < Files[(int)dirIndex].Entries.Count; i++)
            {
                var currentEntry = Files[(int)dirIndex].Entries[i];
                if (currentEntry.Flags.HasFlag(EntryKeyFlags.Directory))
                {
                    baseDir += FileNames.GetByIndex(currentEntry.NameIndex).Value + '/';
                    dirIndex = currentEntry.EntryIndex;
                    TraverseNestedTreeAndList(files, baseDir, ref dirIndex, ref currentIndex);
                    baseDir = bDir;
                    dirIndex = baseDirIndex;
                }
                else
                {
                    string finalDir = baseDir + FileNames.GetByIndex(currentEntry.NameIndex).Value;
                    if (currentEntry.FileExtensionIndex != 0)
                        finalDir += Extensions.GetByIndex(currentEntry.FileExtensionIndex).Value;

                    files.Add(finalDir, currentEntry);
                    currentIndex++;
                }
            }
        }

        /// <summary>
        /// Registers a new extension for the table of contents and updates the file tree if needed.
        /// </summary>
        /// <param name="ext">Extension to update.</param>
        public uint RegisterExtension(string ext)
        {
            if (Extensions.TryAddNewString(ext, out uint keyIndex))
            {
                // Each new key after the extension needs increased index
                foreach (var tree in Files)
                {
                    foreach (var entry in tree.Entries)
                    {
                        if (entry.FileExtensionIndex >= keyIndex)
                            entry.FileExtensionIndex++;
                    }
                }
            }

            return keyIndex;
        }


        /// <summary>
        /// Registers a new file name for the table of contents and updates the file tree if needed.
        /// </summary>
        /// <param name="ext">Extension to update.</param>
        public uint RegisterFilename(string name)
        {
            if (FileNames.TryAddNewString(name, out uint keyIndex))
            {
                // We inserted a key possibly in the middle of the tree since its sorted - every keys after that one needs to be increased
                foreach (var tree in Files)
                {
                    foreach (var entry in tree.Entries)
                    {
                        if (entry.NameIndex >= keyIndex)
                            entry.NameIndex++;
                    }
                }

            }

            return keyIndex;
        }

        /// <summary>
        /// Gets the highest next entry index.
        /// </summary>
        /// <returns></returns>
        public uint NextEntryIndex()
        {
            uint lastIndex = FileInfos.Entries.Max(e => e.FileIndex);

            // TOC also counts as an entry, even if its not registered.
            return Math.Max(ParentHeader.ToCNodeIndex + 1, lastIndex + 1);
        }

        /// <summary>
        /// Gets the highest next sector index.
        /// </summary>
        /// <returns></returns>
        public uint NextSectorIndex()
        {
            FileInfoKey lastSectorKey = FileInfos.Entries.OrderByDescending(e => e.SectorOffset).FirstOrDefault();

            int minSize = 1;
            if (lastSectorKey.CompressedSize > minSize)
                minSize = (int)lastSectorKey.CompressedSize;
            return (uint)(lastSectorKey.SectorOffset + MathF.Ceiling(minSize / (float)SECTOR_SIZE)); // Last + Size
        }

        public ulong GetTotalPatchFileSystemSize(uint compressedTocSize)
        {
            compressedTocSize -= 8; // Remove the header

            uint lastSectorIndex = FileInfos.Entries.Max(e => e.SectorOffset);
            FileInfoKey lastFileInfoBySector = FileInfos.Entries.FirstOrDefault(e => e.SectorOffset == lastSectorIndex);
            ulong pdiFileSize = (ulong)(lastSectorIndex + MathF.Ceiling(lastFileInfoBySector.CompressedSize / (float)SECTOR_SIZE));
            double tocSecSize = Math.Ceiling(compressedTocSize / (float)SECTOR_SIZE);
            return (ulong)((tocSecSize + pdiFileSize + 1) * (float)SECTOR_SIZE);
        }

        /// <summary>
        /// Checks if a directory already exists within the table of contents.
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="path"></param>
        /// <param name="entryIndexIfExists"></param>
        /// <returns></returns>
        public bool DirectoryExists(FileEntryBTree tree, string path, out uint entryIndexIfExists)
        {
            entryIndexIfExists = 0;
            foreach (var entry in tree.Entries)
            {
                if (FileNames.GetByIndex(entry.NameIndex).Value.Equals(path))
                {
                    entryIndexIfExists = entry.EntryIndex;
                    return true;
                }
            }

            return false;
        }

        private bool IsCompressableFile(string path)
        {
            if (path.StartsWith("crs/") && path.EndsWith("stream")) // Stream files should NOT be compressed
                return false;

            if (path.StartsWith("car/") && path.EndsWith("bin")) // Car Paint files shouldn't either
                return false;

            if (path.StartsWith("replay/")) // Replays can be compressed already
                return false;

            if (path.StartsWith("carsound/")) // Dunno why
                return false;

            if (path.EndsWith("gpb") || path.EndsWith("mpackage")) // Not original but added it because the components inside are compressed already
                return false;

            return true;
        }
    }
}
