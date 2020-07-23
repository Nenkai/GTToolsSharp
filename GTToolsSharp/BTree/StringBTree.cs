using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using System.IO;

using GTToolsSharp.Utils;
namespace GTToolsSharp.BTree
{
    public class StringBTree : BTree<StringKey>
    {
        public StringBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {

        }

        public void Serialize(BinaryStream bTreeWriter)
        {
            uint totalKeyIndex = 0;
            ushort segmentCount = 0;

            bool writeIndex = true;

            // Children data
            List<StringKey> firstSegmentKeys = new List<StringKey>();
            List<uint> nextKeyIndexes = new List<uint>();
            List<uint> nodeOffsets = new List<uint>();

            uint childTreeOffset = 6;
            uint treeStartOffset = (uint)bTreeWriter.Position;

            bTreeWriter.Position += 6; // Skip tree metadata to write later

            // Go through a segment, everytime
            // Each segment contains indexes, and entries
            while (totalKeyIndex < Entries.Count)
            {
                int keySegmentIndex = (int)totalKeyIndex;
                uint keysThisSegment = 0;

                List<uint> currentSegmentOffsets = new List<uint>();

                // Streams for the current segment, needed as we're jumping around frequently

                // Writes the offsets
                using var offsetsBuffer = new MemoryStream();
                using var offsetsBufferWriter = new BinaryStream(offsetsBuffer, ByteConverter.Big);

                // Writes the keys
                using var keyTreeBuffer = new MemoryStream();
                using var keyTreeBufferWriter = new BinaryStream(keyTreeBuffer, ByteConverter.Big);

                while (keySegmentIndex < Entries.Count)
                {
                   uint offsetAligned = ((keysThisSegment + 4u) * 12) / 8u;
                    // Is Odd
                    if ((keysThisSegment + 4 & 1) == 0)
                        offsetAligned--;

                    uint keyLength = (uint)Entries[(int)keySegmentIndex].Value.Length + 1; // Prefixed String
                    if (keyTreeBufferWriter.Position + offsetAligned + keyLength >= (GTVolumeTOC.SEGMENT_SIZE * 2) || keySegmentIndex + 1 == Entries.Count)
                    {
                        if (keySegmentIndex + 1 == Entries.Count)
                        {
                            if (offsetAligned + keyTreeBufferWriter.Position + keyLength >= (GTVolumeTOC.SEGMENT_SIZE * 2))
                            {
                                // This never happens due to the length of strings very likely to be never above the size
                                throw new NotImplementedException("Last string entry exceeded segment size, unhandled behavior"");
                            }

                            currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                            keyTreeBufferWriter.WriteString(Entries[keySegmentIndex].Value, StringCoding.ByteCharCount, Encoding.ASCII);
                            keysThisSegment++;
                        }

                        // Write the key count
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, keysThisSegment + GTVolumeTOC.SEGMENT_SIZE, 0);

                        // Write offsets
                        for (int o = 0; o < keysThisSegment; o++)
                            CryptoUtils.WriteBitsAt(offsetsBufferWriter, currentSegmentOffsets[o] + (((keysThisSegment + 1) * 12) / 8) + 2, (uint)o + 1u);

                        // Write the space remaining to the next offset
                        offsetAligned = (((keysThisSegment + 3) * 12 ) / 8) - 1;
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, offsetAligned + (uint)keyTreeBufferWriter.Length, keysThisSegment + 1);
                        bTreeWriter.Write(offsetsBuffer.ToArray());
                        bTreeWriter.Write(keyTreeBuffer.ToArray());
                        nextKeyIndexes.Add(totalKeyIndex + keysThisSegment);
                        nodeOffsets.Add((uint)((bTreeWriter.Position - treeStartOffset) - offsetsBufferWriter.Length - keyTreeBufferWriter.Length));

                        writeIndex = false;
                        segmentCount++;
                        break;
                    }

                    if (!writeIndex)
                    {
                        firstSegmentKeys.Add(Entries[keySegmentIndex]);
                        writeIndex = true;
                    }

                    currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                    keyTreeBufferWriter.Write(Entries[keySegmentIndex].Value, StringCoding.ByteCharCount, Encoding.ASCII);
                    keysThisSegment++;
                    keySegmentIndex++;
                }

                totalKeyIndex += keysThisSegment;
            }

            if (segmentCount > 1)
            {
                childTreeOffset = (uint)(bTreeWriter.Position - treeStartOffset);
                SerializeChildren(bTreeWriter, nextKeyIndexes, firstSegmentKeys, nodeOffsets);
            }

            long tempPos = bTreeWriter.Position;
            bTreeWriter.Position = treeStartOffset;

            // Write meta data
            bTreeWriter.Write(segmentCount > 1 ? (byte)1 : (byte)0); // If there's more than one segment, this is toggled
            bTreeWriter.WriteUInt24BE(childTreeOffset); // Offset for the child
            bTreeWriter.WriteUInt16(segmentCount); // Count of segments (4096)

            bTreeWriter.Position = tempPos;
        }

        private static void SerializeChildren(BinaryStream srcStream, List<uint> childIndexes, List<StringKey> childNames, List<uint> childOffsets)
        {
            using var childrenBuffer = new MemoryStream();
            using var childrenBufferWriter = new BinaryStream(childrenBuffer, ByteConverter.Big);

            List<uint> segmentOffsets = new List<uint>();
            byte[] entryBuffer = Array.Empty<byte>();
            uint entryCount = 0;

            using var entryBufferStream = new MemoryStream();
            using var entryBufferWriter = new BinaryStream(entryBufferStream, ByteConverter.Big);

            for (int i = 0; i < childNames.Count; i++)
            {
                segmentOffsets.Add((uint)entryBufferWriter.BaseStream.Position);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, childIndexes[i]);
                entryBufferWriter.WriteString(childNames[i].Value, StringCoding.ByteCharCount, converter: ByteConverter.Big);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, childOffsets[i]);
                entryCount++;
            }

            segmentOffsets.Add((uint)entryBufferWriter.BaseStream.Position);
            CryptoUtils.EncodeAndAdvance(entryBufferWriter, childIndexes[^1]);
            entryBufferWriter.WriteByte(1);
            entryBufferWriter.WriteByte(255);
            CryptoUtils.EncodeAndAdvance(entryBufferWriter, childOffsets[^1]);
            entryCount++;
            entryBuffer = entryBufferStream.ToArray();

            CryptoUtils.WriteBitsAt(childrenBufferWriter, entryCount, 0);

            for (uint i = 0; i < entryCount; i++)
                CryptoUtils.WriteBitsAt(childrenBufferWriter, segmentOffsets[(int)i] + ((entryCount + 1) * 12) / 8 + 2, i + 1);

            uint remainingTillNextSegment = ((entryCount + 3) * 12) / 8 - 1;
            CryptoUtils.WriteBitsAt(childrenBufferWriter, remainingTillNextSegment + (uint)entryBuffer.Length, entryCount + 1);
            childrenBufferWriter.Write(entryBuffer);
            srcStream.Write(childrenBuffer.ToArray());
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

        public override int EqualToKeyCompareOp(StringKey key, ref SpanReader sr)
        {
            throw new NotImplementedException();
        }

        public override int LessThanKeyCompareOp(StringKey key, ref SpanReader sr)
        {
            throw new NotImplementedException();
        }

        public override StringKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }
    }
}
