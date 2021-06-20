using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using System.IO;

using GTToolsSharp.Utils;
using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class StringBTree : BTree<StringKey>
    {
        public StringBTree(Memory<byte> buffer)
            : base(buffer)
        {

        }



        /// <summary>
        /// Adds a new string to the tree. Returns whether it was actually added.
        /// </summary>
        /// <param name="entry">Entry value.</param>
        /// <param name="entryIndex">Entry index, always returned.</param>
        /// <returns></returns>
        public bool TryAddNewString(string entry, out uint entryIndex)
        {
            StringKey existing = Entries.FirstOrDefault(e => e.Value.Equals(entry));
            if (existing != null)
            {
                entryIndex = (uint)Entries.IndexOf(existing);
                return false;
            }

            var newKey = new StringKey(entry);
            Entries.Add(newKey);
            Entries.Sort(StringLengthSorter);
            entryIndex = (uint)Entries.IndexOf(newKey);
            return true;
        }

        public int GetIndexOfString(string value)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Value == value)
                    return i;
            }

            return -1;
        }

        private static int StringLengthSorter(StringKey value1, StringKey value2)
        {
            string v1 = value1.Value;
            string v2 = value2.Value;

            int min = v1.Length > v2.Length ? v2.Length : v1.Length;
            for (int i = 0; i < min; i++)
            {
                if (v1[i] < v2[i])
                    return -1;
                else if (v1[i] > v2[i])
                    return 1;
            }
            if (v1.Length < v2.Length)
                return -1;
            else if (v1.Length > v2.Length)
                return 1;

            return 0;
        }

        public override void Serialize(ref BitStream stream, GTVolumeTOC parentTOC)
        {
            int baseTreePos = stream.Position;
            stream.Position += 6;

            short segmentCount = 0; // 1 per 0x1000
            int index = 0; // To keep track of which key we are currently at

            // Following lists is for writing index blocks later on
            BitStream indexStream = new BitStream(BitStreamMode.Write);
            IndexWriter<StringKey> indexWriter = new IndexWriter<StringKey>();

            int baseSegmentPos = stream.Position;
            while (index < Entries.Count)
            {
                int segmentKeyCount = 0;
                baseSegmentPos = stream.Position;

                BitStream entryWriter = new BitStream(BitStreamMode.Write, 1024);

                // To keep track of where the keys are located to write them in the toc
                List<int> segmentKeyOffsets = new List<int>();

                // Write keys in 0x1000 segments
                while (index < Entries.Count)
                {
                    StringKey key = Entries[index];
                    uint keySize = key.GetSerializedKeySize();

                    // Get the current segment size - apply size of key offsets (12 bits each) (extra (+ 12 + 12) due to segment header and next segment offset)
                    int currentSizeTaken = (int)Math.Round(((double)segmentKeyCount * 12 + 12 + 12) / 8, MidpointRounding.AwayFromZero);
                    currentSizeTaken += entryWriter.Position; // And size of key data themselves

                    // Segment size exceeded?
                    if (currentSizeTaken + (keySize + 2) >= BTREE_SEGMENT_SIZE) // Extra 2 to fit the 12 bits as short
                    {
                        // Segment's done, move on to next
                        break;
                    }

                    // To build up the segment's TOC when its filled or done
                    segmentKeyOffsets.Add(entryWriter.Position);

                    // Serialize the key
                    key.Serialize(ref entryWriter);

                    // Move on to next
                    segmentKeyCount++;
                    index++;
                }

                // Avoid writing the end yet
                if (index < Entries.Count)
                {
                    // For string btrees, the string index within the list is what we write for the game to search since these don't have actual indexes
                    indexWriter.AddIndex(ref indexStream, index, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]); 
                }

                // Finish up segment header
                stream.Position = baseSegmentPos;
                stream.WriteBoolBit(true);
                stream.WriteBits((ulong)segmentKeyCount, 11);

                int tocSize = (int)Math.Round(((double)segmentKeyCount * 12 + 12 + 12) / 8, MidpointRounding.AwayFromZero);
                for (int i = 0; i < segmentKeyCount; i++)
                {
                    // Translate each key offset to segment relative offsets
                    stream.WriteBits((ulong)(tocSize + segmentKeyOffsets[i]), 12);
                }

                Span<byte> entryBuffer = entryWriter.GetSpan();
                int nextSegmentOffset = tocSize + entryBuffer.Length;

                Debug.Assert(nextSegmentOffset < BTREE_SEGMENT_SIZE, "Next segment offset beyond segment size?");

                stream.WriteBits((ulong)nextSegmentOffset, 12);

                // Write key data
                stream.WriteByteData(entryWriter.GetSpan());

                segmentCount++;
            }

            // Write index blocks that links all segments together 
            int indexBlocksOffset = stream.Position - baseTreePos;

            if (segmentCount > 1)
            {
                StringKey key = new StringKey();
                var lastKey = key.GetLastIndex();

                indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, Entries.Count, lastKey); // Last index
                stream.WriteByteData(indexStream.GetSpan());
            }

            // Align the tree to nearest 0x04
            stream.Align(0x04);

            int endPos = stream.Position;
            stream.Position = baseTreePos;

            // Finish tree header
            stream.WriteByte(indexWriter.SegmentCount);
            stream.WriteBits(segmentCount > 1 ? (ulong)indexBlocksOffset : 6, 24); // If theres no index block, just point to the first and only segment
            stream.WriteInt16(segmentCount);

            // Done. Move to bottom.
            stream.Position = endPos;
        }

        public override int EqualToKeyCompareOp(StringKey key, Span<byte> data)
        {
            throw new NotImplementedException();
        }

        public override int LessThanKeyCompareOp(StringKey key, Span<byte> data)
        {
            throw new NotImplementedException();
        }

        public override StringKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }
    }
}
