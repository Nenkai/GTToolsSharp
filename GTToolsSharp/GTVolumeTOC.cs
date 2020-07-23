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
                Program.Log($"Volume toc magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2")))})");
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
            FileNames.LoadEntries();

            Extensions = new StringBTree(Data, (int)FileExtensionTreeOffset);
            Extensions.LoadEntries();

            FileInfos = new FileInfoBTree(Data, (int)NodeTreeOffset);
            FileInfos.LoadEntries();

            Files = new List<FileEntryBTree>((int)entryTreeCount);

            for (int i = 0; i < entryTreeCount; i++)
            {
                Files.Add(new FileEntryBTree(Data, (int)RootAndFolderOffsets[i]));
                Files[i].LoadEntries();
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
            byte[] compressedToc = MiscUtils.ZlibCompress(tocSerialized);

            uncompressedSize = (uint)tocSerialized.Length;
            compressedTocSize = (uint)compressedToc.Length;

            ParentVolume.Keyset.CryptData(compressedToc, ParentHeader.LastIndex);

            string path = Path.Combine(outputDir, PDIPFSPathResolver.GetPathFromSeed(ParentHeader.LastIndex));
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
                if (tocFiles.ContainsKey(file))
                {
                    Program.Log($"[:] Pack: Removing file from table of contents: {file}");
                    RegisterFilePath(file);
                }
            }
        }

        /// <summary>
        /// Pack all provided files and edit the table of contents accordingly.
        /// </summary>
        /// <param name="FilesToPack">Files to pack.</param>
        /// <param name="outputDir">Main output dir to use to expose the packed files.</param>
        public void PackFilesForPatchFileSystem(Dictionary<string, InputPackEntry> FilesToPack, string[] filesToRemove, string outputDir)
        {
            if (filesToRemove.Length > 0)
                RemoveFilesFromTOC(filesToRemove);

            if (FilesToPack.Count > 0)
            {
                // Pick up files we're going to add if there's any
                PreRegisterNewFilesToPack(FilesToPack);

                Dictionary<string, FileEntryKey> tocFiles = GetAllRegisteredFileMap();
                foreach (var file in FilesToPack)
                {
                    if (tocFiles.TryGetValue(file.Key, out FileEntryKey entryKey))
                    {
                        Program.Log($"[:] Pack: Processing {file.Key}");
                        FileInfoKey key = FileInfos.GetByFileIndex(entryKey.EntryIndex);
                        string volPath = PDIPFSPathResolver.GetPathFromSeed(entryKey.EntryIndex);

                        uint newUncompressedSize = file.Value.FileSize;
                        uint newCompressedSize = file.Value.FileSize;

                        byte[] fileData = File.ReadAllBytes(file.Value.FullPath);
                        if (key.Flags.HasFlag(FileInfoKey.FileInfoFlags.Compressed))
                        {
                            Program.Log($"[:] Pack: Compressing {file.Key}");
                            fileData = MiscUtils.ZlibCompress(fileData);
                            newCompressedSize = (uint)fileData.Length;
                        }

                        Program.Log($"[:] Pack: Saving and encrypting {file.Key} to {volPath}");

                        // Will also update the ones we pre-registered
                        UpdateKeyAndRetroactiveAdjustSegments(key, newCompressedSize, newUncompressedSize);
                        ParentVolume.Keyset.CryptBytes(fileData, fileData, key.FileIndex);

                        string outputFile = Path.Combine(outputDir, volPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

                        File.WriteAllBytes(outputFile, fileData);

                    }
                }
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
                    Program.Log($"[:] Pack: Adding new file to table of contents: {file.Key}");
                    RegisterFilePath(file.Key);
                }
            }
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
            FileInfos.Entries.Add(newKey);
            string[] folders = path.Split(Path.AltDirectorySeparatorChar);

            uint dirEntryIndex = 0;
            for (uint i = 0; i < folders.Length - 1; i++) // Do not include file name
            {
                if (!DirectoryExists(Files[(int)dirEntryIndex], folders[i], out dirEntryIndex))
                    dirEntryIndex = RegisterDirectory(dirEntryIndex, folders[i]);
            }

            RegisterFile(dirEntryIndex, Path.GetFileNameWithoutExtension(path), ext);
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
        /// <param name="dirIndex"></param>
        /// <param name="dirName"></param>
        /// <returns></returns>
        public uint RegisterDirectory(uint dirIndex, string dirName)
        {
            uint dirNameIndex = RegisterFilename(dirName);
            uint entryIndex = 0;
            var dirEntries = Files[(int)dirIndex].Entries.Where(e => e.Flags.HasFlag(EntryKeyFlags.Directory));
            if (dirEntries.Count() > 1)
            {
                FileEntryKey k = dirEntries.FirstOrDefault(e => e.NameIndex > dirNameIndex);
                entryIndex = k?.EntryIndex ?? 0;
            }

            if (entryIndex == 0)
            {
                if (!dirEntries.Any())
                    entryIndex = dirIndex + 1; // New one
                else
                {
                    uint lastDirIndex = dirEntries.Max(e => e.EntryIndex);
                    Math.Max(lastDirIndex, Files[(int)lastDirIndex].Entries
                        .Where(e => e.Flags.HasFlag(EntryKeyFlags.Directory))
                        .Max(t => t.EntryIndex));
                    entryIndex = lastDirIndex + 1;
                }
            }

            // Update the trees if needed
            foreach (var tree in Files)
            {
                foreach (var child in tree.Entries)
                {
                    if (child.Flags.HasFlag(EntryKeyFlags.Directory) && child.EntryIndex >= entryIndex)
                    {
                        entryIndex = child.EntryIndex;
                        child.EntryIndex = entryIndex + 1;
                    }
                }
            }

            var newEntry = new FileEntryKey();
            newEntry.Flags = EntryKeyFlags.Directory;
            newEntry.NameIndex = dirNameIndex;
            newEntry.EntryIndex = entryIndex;
            Files[(int)dirIndex].Entries.Add(newEntry);

            Files[(int)dirIndex].ResortByNameIndexes();
            Files.Add(new FileEntryBTree());
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
                    files.Add(FileNames.GetByIndex(currentEntry.NameIndex).Value, currentEntry);
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
                // Each new key after the file name needs increased index
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
            return Math.Max(ParentHeader.LastIndex + 1, lastIndex + 1);
        }

        /// <summary>
        /// Gets the highest next segment index.
        /// </summary>
        /// <returns></returns>
        public uint NextSegmentIndex()
        {
            FileInfoKey lastSegmentKey = FileInfos.Entries.OrderByDescending(e => e.SegmentIndex).FirstOrDefault();
            return (uint)(lastSegmentKey.SegmentIndex + Math.Ceiling(lastSegmentKey.CompressedSize / (double)SEGMENT_SIZE)); // Last + Size
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
    }
}
