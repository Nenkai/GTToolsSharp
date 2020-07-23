using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Utils;
using static GTToolsSharp.Utils.CryptoUtils;

namespace GTToolsSharp.BTree
{
    public class FileInfoBTree : BTree<FileInfoKey>
    {
        public FileInfoBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {

        }

        public void Serialize(BinaryStream bTreeWriter)
        {
            uint totalKeyIndex = 0;
            ushort segmentCount = 0;

            // Children data
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
                    uint countToNextSegmentAligned = ((keysThisSegment + 4u) * 12) / 8u;
                    // Needs to be odd
                    if ((keysThisSegment + 4 & 1) == 0)
                        countToNextSegmentAligned--;

                    var keyBuffer = new MemoryStream();
                    uint keyLength = 0;
                    if (keySegmentIndex < Entries.Count)
                        keyLength = Entries[keySegmentIndex].GetSerializedKeySize();

                    if (keyTreeBufferWriter.Position + countToNextSegmentAligned + keyLength >= (GTVolumeTOC.SEGMENT_SIZE * 2) || keySegmentIndex + 1 == Entries.Count)
                    {
                        if (keySegmentIndex + 1 == Entries.Count)
                        {
                            if (countToNextSegmentAligned + keyTreeBufferWriter.Position + keyBuffer.Length >= (GTVolumeTOC.SEGMENT_SIZE * 2))
                            {
                                // This never happens due to the length very likely to be never above the size
                                // If it ever happens, need to actually implement this
                                throw new NotImplementedException("Last fileinfo entry exceeded segment size, unhandled behavior");
                            }

                            currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                            Entries[keySegmentIndex].Serialize(keyTreeBufferWriter);
                            keysThisSegment++;
                        }

                        // Write the key count
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, keysThisSegment + GTVolumeTOC.SEGMENT_SIZE, 0);

                        // Write offsets
                        for (int o = 0; o < keysThisSegment; o++)
                            CryptoUtils.WriteBitsAt(offsetsBufferWriter, currentSegmentOffsets[o] + ((((keysThisSegment + 3) * 12) / 8) - 1), (uint)o + 1u);

                        // Write the space remaining to the next offset
                        countToNextSegmentAligned = (((keysThisSegment + 3) * 12) / 8) - 1;
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, countToNextSegmentAligned + (uint)keyTreeBuffer.Length, keysThisSegment + 1);
                        bTreeWriter.Write(offsetsBuffer.ToArray());
                        bTreeWriter.Write(keyTreeBuffer.ToArray());
                        nextKeyIndexes.Add(Entries[keySegmentIndex].FileIndex);
                        nodeOffsets.Add((uint)((bTreeWriter.Position - treeStartOffset) - offsetsBufferWriter.Length - keyTreeBufferWriter.Length));

                        segmentCount++;
                        break;
                    }

                    currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                    Entries[keySegmentIndex].Serialize(keyTreeBufferWriter);
                    keysThisSegment++;
                    keySegmentIndex++;

                    keyBuffer.Dispose();
                }

                totalKeyIndex += keysThisSegment;
            }

            nextKeyIndexes[^1]++;

            if (segmentCount > 1)
            {
                childTreeOffset = (uint)(bTreeWriter.Position - treeStartOffset);
                SerializeChildren(bTreeWriter, nextKeyIndexes, nodeOffsets);
            }

            long tempPos = bTreeWriter.Position;
            bTreeWriter.Position = treeStartOffset;
            
            // Write meta data
            bTreeWriter.Write(segmentCount > 1 ? (byte)1 : (byte)0); // If there's more than one segment, this is toggled
            bTreeWriter.WriteUInt24BE(childTreeOffset); // Offset for the child
            bTreeWriter.WriteUInt16(segmentCount); // Count of segments (4096)

            bTreeWriter.Position = tempPos;
        }

        private static void SerializeChildren(BinaryStream srcStream, List<uint> childIndexes, List<uint> childOffsets)
        {
            using var childrenBuffer = new MemoryStream();
            using var childrenBufferWriter = new BinaryStream(childrenBuffer, ByteConverter.Big);

            List<uint> segmentOffsets = new List<uint>();
            byte[] entryBuffer = Array.Empty<byte>();
            using var entryBufferStream = new MemoryStream();
            using var entryBufferWriter = new BinaryStream(entryBufferStream, ByteConverter.Big);

            uint entryCount = 0;
            for (int i = 0; i < childIndexes.Count; i++)
            {
                segmentOffsets.Add((uint)entryBufferWriter.BaseStream.Position);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, childIndexes[i]); // Next segments
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, childOffsets[i]); // Next offsets
                entryCount++;
            }

            entryBuffer = entryBufferStream.ToArray();

            CryptoUtils.WriteBitsAt(childrenBufferWriter, entryCount, 0);

            for (uint i = 0U; i < entryCount; i++)
                CryptoUtils.WriteBitsAt(childrenBufferWriter, segmentOffsets[(int)i] + (((entryCount + 1) * 12) / 8) + 2, i + 1);

            uint remainingTillNextSegment = ((entryCount + 3U) * 12) / 8U - 1U;
            CryptoUtils.WriteBitsAt(childrenBufferWriter, remainingTillNextSegment + (uint)entryBuffer.Length, entryCount + 1U);
            childrenBufferWriter.Write(entryBuffer);
            childrenBufferWriter.WriteUInt16(0);
            srcStream.Write(childrenBuffer.ToArray());

        }

        public uint SearchIndexByKey(FileInfoKey key)
        {
            SpanReader sr = new SpanReader(this._buffer, Endian.Big);
            sr.Position += _offsetStart;

            uint count = (uint)ReadByteAtOffset(ref sr, 0);

            sr.Endian = Endian.Little;
            uint offset = ReadUInt24AtOffset(ref sr, 1); // 4th byte is 0
            sr.Endian = Endian.Big;

            // Can't do data = sr.Slice(), because for some reason the endian setting does not carry over?
            SpanReader data = sr.GetReaderAtOffset((int)offset);

            SearchResult res = new SearchResult();
            for (uint i = count; i != 0; i--)
            {
                data = SearchWithComparison(ref data, count, key, res, SearchCompareMethod.LessThan);
                if (data.Position == -1)
                    goto DONE;

                res.maxIndex = (uint)DecodeBitsAndAdvance(ref data);

                uint nextOffset = (uint)DecodeBitsAndAdvance(ref data);

                data = sr.GetReaderAtOffset((int)nextOffset);
            }

            data = SearchWithComparison(ref data, 0, key, res, SearchCompareMethod.EqualTo);

            DONE:
            if (count == 0)
                res.upperBound = 0;

            if (data.Position != -1)
            {
                uint index = (res.maxIndex - res.upperBound + res.lowerBound);
                data.Position = 0;

                key.KeyOffset = (uint)(_buffer.Length - data.Length);
                key.Deserialize(ref data);
                return index;
            }
            else
                return FileInfoKey.InvalidIndex;
        }

        public FileInfoKey GetByFileIndex(uint fileIndex)
            => Entries.FirstOrDefault(e => e.FileIndex == fileIndex);

        public override int LessThanKeyCompareOp(FileInfoKey key, ref SpanReader sr) 
	    {
            uint nodeIndex = (uint)DecodeBitsAndAdvance(ref sr);
            if (key.FileIndex < nodeIndex)
                return -1;
            else 
                return 1;
        }

        public override int EqualToKeyCompareOp(FileInfoKey key, ref SpanReader sr)
        {
            sr.ReadByte(); // Skip flag
            uint nodeIndex = (uint)DecodeBitsAndAdvance(ref sr);
            if (key.FileIndex < nodeIndex)
                return -1;
            else if (key.FileIndex > nodeIndex)
                return 1;
            else
                return 0;
        }

        public override FileInfoKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }
    }
}
