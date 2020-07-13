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

            string fullEntryPath = Path.Combine(OutDir, entryPath);
            if (entryKey.Flags.HasFlag(EntryKeyFlags.Directory))
            {
                if (!_volume.IsPatchVolume)
                {
                    Program.Log($"DIR: {entryPath}");
                    Directory.CreateDirectory(fullEntryPath);
                }

                var childEntryBTree = new FileEntryBTree(_volume.TOCData, (int)_volume.EntryOffsets[(int)entryKey.LinkIndex]);
                var childUnpacker = new EntryUnpacker(_volume, OutDir, entryPath);
                childEntryBTree.Traverse(childUnpacker);
            }
            else if (entryKey.Flags.HasFlag(EntryKeyFlags.File))
            {
                if (!_volume.IsPatchVolume)
                    Program.Log($"FILE: {entryPath}");

                var nodeBTree = new NodeBTree(_volume.TOCData, (int)_volume.NodeTreeOffset);
                var nodeKey = new NodeKey(entryKey.LinkIndex);

                uint nodeIndex = nodeBTree.SearchIndexByKey(nodeKey);

                if (nodeIndex != NodeKey.InvalidIndex)
                {
                   _volume.UnpackNode(nodeKey, fullEntryPath);
                }
            }


        }

    }
}
