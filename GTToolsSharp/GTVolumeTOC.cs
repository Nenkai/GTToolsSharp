using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.IO.Compression;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using static GTToolsSharp.Utils.CryptoUtils;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents multiple B-Trees designing file offsets within an entire volume.
    /// </summary>
    public class GTVolumeTOC
    {
        private readonly static byte[] SEGMENT_MAGIC = { 0x5B, 0x74, 0x51, 0x6E };
        public const int SEGMENT_SIZE = 2048;

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

        public GTVolumeHeader ParentHeader { get; }
        public GTVolume ParentVolume { get; }

        public GTVolumeTOC(GTVolumeHeader parentHeader, GTVolume parentVolume)
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
            if (!magic.AsSpan().SequenceEqual(SEGMENT_MAGIC.AsSpan()))
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

            
            FileNames = new StringBTree(Data, (int)NameTreeOffset);
            if (!ParentVolume.IsGT5PDemoStyle)
                FileNames.LoadEntries();
            else
                FileNames.LoadEntriesOld();
            
            Extensions = new StringBTree(Data, (int)FileExtensionTreeOffset);
            if (!ParentVolume.IsGT5PDemoStyle)
                Extensions.LoadEntries();
            else
                Extensions.LoadEntriesOld();

            FileInfos = new FileInfoBTree(Data, (int)NodeTreeOffset);
            if (!ParentVolume.IsGT5PDemoStyle)
                FileInfos.LoadEntries();
            else
                FileInfos.LoadEntriesOld();

            Files = new List<FileEntryBTree>((int)entryTreeCount);

            for (int i = 0; i < entryTreeCount; i++)
            {
                Files.Add(new FileEntryBTree(Data, (int)RootAndFolderOffsets[i]));
                if (!ParentVolume.IsGT5PDemoStyle)
                    Files[i].LoadEntries();
                else
                    Files[i].LoadEntriesOld();
            }

            var test = this.GetAllRegisteredFileMap();
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
            byte[] compressedToc = MiscUtils.ZlibCompress(tocSerialized);

            uncompressedSize = (uint)tocSerialized.Length;
            compressedTocSize = (uint)compressedToc.Length;

            ParentVolume.Keyset.CryptData(compressedToc, ParentHeader.TOCEntryIndex);

            string path = Path.Combine(outputDir, PDIPFSPathResolver.GetPathFromSeed(ParentHeader.TOCEntryIndex));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, compressedToc);
        }

        /// <summary>
        /// Serializes the table of contents and its B-Trees.
        /// </summary>
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var bs = new BinaryStream(ms, ByteConverter.Big);

            bs.Write(SEGMENT_MAGIC);
            bs.Position = 16;
            bs.WriteUInt32((uint)Files.Count);
            bs.Position += sizeof(uint) * Files.Count;

            uint fileNamesOffset = (uint)bs.Position;
            FileNames.Serialize(bs);

            uint extOffset = (uint)bs.Position;
            Extensions.Serialize(bs);

            uint fileInfoOffset = (uint)bs.Position;
            FileInfos.Serialize(bs);

            // The list of file entry btrees mostly consist of the relation between files, folder, extensions and data
            // Thus it is writen at the end
            // Each tree is a seperate general subdir
            const int baseListPos = 20;
            for (int i = 0; i < Files.Count; i++)
            {
                FileEntryBTree f = Files[i];
                uint treeOffset = (uint)bs.Position;
                bs.Position = baseListPos + (i * sizeof(uint));
                bs.WriteUInt32(treeOffset);
                bs.Position = treeOffset;
                f.Serialize(bs, (uint)FileNames.Entries.Count, (uint)Extensions.Entries.Count);
            }

            // Go back to write the meta data
            bs.Position = 4;
            bs.WriteUInt32(fileNamesOffset);
            bs.WriteUInt32(extOffset);
            bs.WriteUInt32(fileInfoOffset);
            return ms.ToArray();
        }

        public void RemoveFilesFromTOC(string[] filesToRemove)
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
        public PackCache PackFilesForPatchFileSystem(Dictionary<string, InputPackEntry> FilesToPack, PackCache packCache, string[] filesToRemove, string outputDir, bool packAllAsNewEntries)
        {
            // If we are packing as new, ensure the TOC is before all the files (that will come after it)
            if (packAllAsNewEntries)
                ParentHeader.TOCEntryIndex = NextEntryIndex();

            if (filesToRemove.Length > 0)
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
            Program.Log($"[:] Pack: Processing {file.VolumeDirPath}");
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
                    UpdateKeyAndRetroactiveAdjustSegments(key, (uint)validCacheEntry.CompressedFileSize, (uint)validCacheEntry.FileSize);
                    return;
                }
                else
                {
                    Program.Log($"[:] Pack: {file.VolumeDirPath} found in cache file but actual file is missing ({pfsFilePath}) - recreating it");
                }
            }

            byte[] fileData;
            while (true)
            {
                try
                {
                    fileData = File.ReadAllBytes(file.FullPath);
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
            else if (key.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                Program.Log($"[:] Pack: Compressing {file.VolumeDirPath}");
                fileData = MiscUtils.ZlibCompress(fileData);
                newCompressedSize = (uint)fileData.Length;
            }

            Program.Log($"[:] Pack: Saving and encrypting {file.VolumeDirPath} -> {pfsFilePath}");

            // Will also update the ones we pre-registered
            UpdateKeyAndRetroactiveAdjustSegments(key, newCompressedSize, newUncompressedSize);
            ParentVolume.Keyset.CryptBytes(fileData, fileData, key.FileIndex);

            string outputFile = Path.Combine($"{outputDir}_temp", pfsFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            File.WriteAllBytes(outputFile, fileData);

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
                        fileInfo.SegmentIndex = NextSegmentIndex(); // Pushed to the end, so technically the segment is new, will be readjusted at the end anyway
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

        public bool TryCheckAndFixInvalidSegmentIndexes()
        {
            bool valid = true;
            List<FileInfoKey> segmentSortedFiles = FileInfos.Entries.OrderBy(e => e.SegmentIndex).ToList();
            for (int i = 0; i < segmentSortedFiles.Count - 1; i++)
            {
                FileInfoKey current = segmentSortedFiles[i];
                FileInfoKey next = segmentSortedFiles[i + 1];
                double segmentsTakenByCurrent = MathF.Ceiling(current.CompressedSize / (float)SEGMENT_SIZE);
                if (next.SegmentIndex != current.SegmentIndex + segmentsTakenByCurrent)
                {
                    valid = false;
                    next.SegmentIndex = (uint)(current.SegmentIndex + segmentsTakenByCurrent);
                }
            }
            return valid;
        }

        /// <summary>
        /// Updates a file entry, and adjusts all the key segments if needed.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="newCompressedSize"></param>
        /// <param name="newUncompressedSize"></param>
        private void UpdateKeyAndRetroactiveAdjustSegments(FileInfoKey fileInfo, uint newCompressedSize, uint newUncompressedSize)
        {
            float oldTotalSegments = MathF.Ceiling(fileInfo.CompressedSize / (float)SEGMENT_SIZE);
            float newTotalSegments = MathF.Ceiling(newCompressedSize / (float)SEGMENT_SIZE);
            fileInfo.CompressedSize = newCompressedSize;
            fileInfo.UncompressedSize = newUncompressedSize;
            if (oldTotalSegments != newTotalSegments)
            {
                List<FileInfoKey> orderedKeySegments = FileInfos.Entries.OrderBy(e => e.SegmentIndex).ToList();
                for (int i = orderedKeySegments.IndexOf(fileInfo); i < orderedKeySegments.Count - 1; i++)
                {
                    FileInfoKey currentFileInfo = orderedKeySegments[i];
                    FileInfoKey nextFileInfo = orderedKeySegments[i + 1];
                    float segmentCount = MathF.Ceiling(currentFileInfo.CompressedSize / (float)SEGMENT_SIZE);

                    // New file pushes older files beyond segment size? Update them by the amount of segments that increases
                    if (nextFileInfo.SegmentIndex != currentFileInfo.SegmentIndex + segmentCount)
                        nextFileInfo.SegmentIndex = (uint)(currentFileInfo.SegmentIndex + segmentCount);
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
            newKey.SegmentIndex = this.NextSegmentIndex();

            newKey.CompressedSize = 1; // Important for segments to count as at least one
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
                List<FileInfoKey> orderedKeySegments = FileInfos.Entries.OrderBy(e => e.SegmentIndex).ToList();
                int fileSegmentIndex = orderedKeySegments.IndexOf(file);

                // Sort it all
                FileInfoKey cur = orderedKeySegments[fileSegmentIndex];
                FileInfoKey next = orderedKeySegments[fileSegmentIndex + 1];
                cur.SegmentIndex = next.SegmentIndex;
                for (int i = orderedKeySegments.IndexOf(file); i < orderedKeySegments.Count - 1; i++)
                {
                    cur = orderedKeySegments[i];
                    next = orderedKeySegments[i + 1];
                    float segmentCountFromFile = MathF.Ceiling(cur.CompressedSize / (float)SEGMENT_SIZE);

                    // Re-update segments
                    if (next.SegmentIndex != cur.SegmentIndex + segmentCountFromFile)
                        next.SegmentIndex = (uint)(cur.SegmentIndex + segmentCountFromFile);
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
            return Math.Max(ParentHeader.TOCEntryIndex + 1, lastIndex + 1);
        }

        /// <summary>
        /// Gets the highest next segment index.
        /// </summary>
        /// <returns></returns>
        public uint NextSegmentIndex()
        {
            FileInfoKey lastSegmentKey = FileInfos.Entries.OrderByDescending(e => e.SegmentIndex).FirstOrDefault();

            int minSize = 1;
            if (lastSegmentKey.CompressedSize > minSize)
                minSize = (int)lastSegmentKey.CompressedSize;
            return (uint)(lastSegmentKey.SegmentIndex + MathF.Ceiling(minSize / (float)SEGMENT_SIZE)); // Last + Size
        }

        public ulong GetTotalPatchFileSystemSize(uint compressedTocSize)
        {
            compressedTocSize -= 8; // Remove the header

            uint lastSegmentIndex = FileInfos.Entries.Max(e => e.SegmentIndex);
            FileInfoKey lastFileInfoBySegment = FileInfos.Entries.FirstOrDefault(e => e.SegmentIndex == lastSegmentIndex);
            ulong pdiFileSize = (ulong)(lastSegmentIndex + MathF.Ceiling(lastFileInfoBySegment.CompressedSize / (float)SEGMENT_SIZE));
            double tocSegSize = Math.Ceiling(compressedTocSize / (float)SEGMENT_SIZE);
            return (ulong)((tocSegSize + pdiFileSize + 1) * (float)SEGMENT_SIZE);
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
