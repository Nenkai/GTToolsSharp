using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Syroot.BinaryData.Memory;

using GTToolsSharp.BTree;

namespace GTToolsSharp
{
    public class EntryUnpacker
    {
        private GTVolume _volume;
        private string ParentDirectory;
        private string OutDir;
        private List<int> _fileIndexesToExtract;

        public EntryUnpacker(GTVolume baseVolume, string outDir, string parentDir, List<int> fileIndexesToExtract)
        {
            _volume = baseVolume;
            OutDir = outDir;
            ParentDirectory = parentDir;
            _fileIndexesToExtract = fileIndexesToExtract;
        }

        public void UnpackFromKey(FileEntryKey entryKey)
        {
            string entryPath = _volume.GetEntryPath(entryKey, ParentDirectory);
            if (string.IsNullOrEmpty(entryPath))
            {
                Program.Log($"Could not determine entry path for Entry key at name Index {entryKey.NameIndex}");
                return;
            }

            string fullEntryPath = Path.Combine(OutDir ?? string.Empty, entryPath);
            if (entryKey.Flags.HasFlag(EntryKeyFlags.Directory))
            {
                if (_fileIndexesToExtract.Count == 0) // Make sure not to spam when not needed
                {
                    if (!_volume.IsPatchVolume || _volume.NoUnpack)
                        Program.Log($"[:] Entering Directory: {entryPath}");
                }

                var childEntryBTree = new FileEntryBTree(_volume.TableOfContents.Data, (int)_volume.TableOfContents.RootAndFolderOffsets[(int)entryKey.EntryIndex]);
                var childUnpacker = new EntryUnpacker(_volume, OutDir, entryPath, _fileIndexesToExtract);
                childEntryBTree.TraverseAndUnpack(childUnpacker);
            }
            else 
            {
                if (_fileIndexesToExtract.Count != 0 && !_fileIndexesToExtract.Contains((int)entryKey.EntryIndex))
                    return;

                if (!_volume.IsPatchVolume || _volume.NoUnpack)
                    Program.Log($"[:] Extracting: {entryPath}");

                var nodeBTree = new FileInfoBTree(_volume.TableOfContents.Data, (int)_volume.TableOfContents.NodeTreeOffset);
                var nodeKey = new FileInfoKey(entryKey.EntryIndex);

                uint nodeIndex = nodeBTree.SearchIndexByKey(nodeKey);

                if (nodeIndex != FileInfoKey.InvalidIndex)
                     _volume.UnpackNode(nodeKey, fullEntryPath);
            }
        }

    }
}
