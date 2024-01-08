using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;
using System.Diagnostics;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Utils;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class FileEntryBTree : BTree<FileEntryKey>
    {
        public FileEntryBTree() { }
        public FileEntryBTree(Memory<byte> buffer, PFSBTree parentToC)
            : base(buffer, parentToC)
        {

        }

        public override int EqualToKeyCompareOp(FileEntryKey key, Span<byte> data)
        {
            BitStream stream = new BitStream(BitStreamMode.Read, data);

            uint nameIndex = stream.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = stream.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;

            return 0;
        }

        public override int LessThanKeyCompareOp(FileEntryKey key, Span<byte> data)
        {
            BitStream stream = new BitStream(BitStreamMode.Read, data);

            uint nameIndex = stream.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = stream.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;
            else
                throw new Exception("?????");
        }

        public override FileEntryKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }


        public void ResortByNameIndexes()
        {
            /* Well, quicksort can go to hell. 
             * Wasted 2 days of mine because this crap would reorder entries that werent needed to be 
             * OrderBy doesnt touch them as it should be 
             */
            // Entries.Sort((x, y) => x.NameIndex.CompareTo(y.NameIndex)); 
            Entries = Entries.OrderBy(e => e.NameIndex).ToList();
        }

        public FileEntryKey GetFolderEntryByNameIndex(uint nameIndex)
        {
            foreach (var entry in Entries)
            {
                if (entry.NameIndex == nameIndex && entry.FileExtensionIndex == 0)
                    return entry;
            }

            return null;
        }

    }
}
