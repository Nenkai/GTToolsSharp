using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class IndexWriter<TKey> where TKey : IBTreeKey<TKey>, new()
    {
        public int CurrentDataLength = 0;

        public int SegmentCount = 0;
        public List<BTreeIndex> CurrentIndexes { get; set; } = new();

        public IndexWriter()
        {

        }

        public void AddIndex(ref BitStream stream, int keyIndex, int segmentOffset, TKey prevKey, TKey nextKey)
        {
            //                                               v Include new
            int newIndexBlockSize = ((CurrentIndexes.Count + 1 * 12) + 12 + 12) / 8;
            newIndexBlockSize += CurrentDataLength;

            var diffKey = prevKey.CompareGetDiff(nextKey);
            var index = new BTreeIndex(keyIndex, segmentOffset, diffKey);
            uint indexSize = MeasureIndexEntrySize(index);

            if (newIndexBlockSize + indexSize >= 0x1000) // Can we fit index?
            {
                // Nope, we are about to start a new one
                WriteBlock(ref stream);
                CurrentIndexes.Clear();
            }
            else
            {
                CurrentIndexes.Add(index);
                if (CurrentIndexes.Count == 1)
                    SegmentCount++;

                CurrentDataLength += (int)indexSize;
            }
        }

        public void Finalize(ref BitStream stream, int segmentOffset, int lastIndex)
        {
            TKey t = new TKey();
            var keyIndex = t.GetLastIndex();

            var lastIndexEntry = new BTreeIndex(lastIndex, segmentOffset, keyIndex);
            CurrentIndexes.Add(lastIndexEntry);
            WriteBlock(ref stream);
        }

        private uint MeasureIndexEntrySize(BTreeIndex index)
        {
            uint size = (uint)BitStream.GetSizeOfVarInt(index.KeyIndex);
            size += index.Key.GetSerializedIndexSize();
            size += (uint)BitStream.GetSizeOfVarInt(index.SegmentOffset);
            return size;
        }

        private void WriteBlock(ref BitStream stream)
        {
            int baseIndexBlockPos = stream.Position;

            // Write entries first
            BitStream indexEntryWriter = new BitStream(BitStreamMode.Write);
            List<int> indexEntryOffsets = new List<int>();
            for (int i = 0; i < CurrentIndexes.Count; i++)
            {
                indexEntryOffsets.Add(indexEntryWriter.Position);

                indexEntryWriter.WriteVarInt(CurrentIndexes[i].KeyIndex);
                CurrentIndexes[i].Key.SerializeIndex(ref indexEntryWriter); // TODO: For strings: only store the first string difference instead of writing the whole thing
                indexEntryWriter.WriteVarInt(CurrentIndexes[i].SegmentOffset);
            }

            // Done writing entries, we can write the index header
            stream.WriteBits((ulong)CurrentIndexes.Count, 12);
            int tocSize = (int)Math.Round(((double)CurrentIndexes.Count * 12 + 12 + 12) / 8, MidpointRounding.AwayFromZero); // Entry count (12 bits) + offset array (12 * off count) + rem next segment (12)
            for (int i = 0; i < CurrentIndexes.Count; i++)
                stream.WriteBits((ulong)(tocSize + indexEntryOffsets[i]), 12);
            stream.WriteBits((ulong)(tocSize + indexEntryWriter.Length), 12);
 
            // Finally add the data
            stream.Position = baseIndexBlockPos + tocSize;
            stream.WriteByteData(indexEntryWriter.GetSpan());
        }

        public record BTreeIndex(int KeyIndex, int SegmentOffset, IBTreeKey<TKey> Key);
        
    }
}
