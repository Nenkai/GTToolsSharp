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

        public EntryUnpacker(GTVolume baseVolume, string outDir, string parentDir)
        {
            _volume = baseVolume;
            OutDir = outDir;
            ParentDirectory = parentDir;
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
                if (!_volume.IsPatchVolume || _volume.NoUnpack)
                    Program.Log($"DIR: {entryPath}");

                if (_volume.NoUnpack)
                    Directory.CreateDirectory(fullEntryPath);

                var childEntryBTree = new FileEntryBTree(_volume.TableOfContents.Data, (int)_volume.TableOfContents.RootAndFolderOffsets[(int)entryKey.EntryIndex]);
                var childUnpacker = new EntryUnpacker(_volume, OutDir, entryPath);
                childEntryBTree.TraverseAndUnpack(childUnpacker);
            }
            else if (entryKey.Flags.HasFlag(EntryKeyFlags.File))
            {
                if (!_volume.IsPatchVolume || _volume.NoUnpack)
                    Program.Log($"FILE: {entryPath}");

                var nodeBTree = new FileInfoBTree(_volume.TableOfContents.Data, (int)_volume.TableOfContents.NodeTreeOffset);
                var nodeKey = new FileInfoKey(entryKey.EntryIndex);

                uint nodeIndex = nodeBTree.SearchIndexByKey(nodeKey);

                if (nodeIndex != FileInfoKey.InvalidIndex)
                {
                     _volume.UnpackNode(nodeKey, fullEntryPath);
                }
            }
        }

    }
}
