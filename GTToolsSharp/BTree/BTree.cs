using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Utils;
using static GTToolsSharp.Utils.CryptoUtils;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    [DebuggerDisplay("Count = {Entries.Count}")]
    public abstract class BTree<TKey> where TKey : IBTreeKey<TKey>, new()
    {
        public const int BTREE_SEGMENT_SIZE = 0x1000;

        protected Memory<byte> _buffer;
        private PFSBTree _parentToC;

        public List<TKey> Entries = new List<TKey>();

        public BTree()
        {

        }

        public BTree(Memory<byte> buffer, PFSBTree parentToC)
        {
            _buffer = buffer;
            _parentToC = parentToC;
        }

        public TKey GetByIndex(uint index)
            => Entries[(int)index];

        public void LoadEntries()
        {
            BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

            byte indexBlockCount = treeStream.ReadByte();
            uint indexBlockOffset = (uint)treeStream.ReadBits(24);
            short segmentCount = treeStream.ReadInt16();

            // Iterate through all segments and all their keys
            for (int i = 0; i < segmentCount; i++)
            {
                int segPos = treeStream.Position;

                bool moreThanOneKey = treeStream.ReadBoolBit();
                uint keyCount = (uint)treeStream.ReadBits(11);

                // 25/07/2021 - Not necessarily true - caused issues with 2.11 US Toc (GT5) - moreThanOneKey was true, keyCount was 0
                // Debug.Assert(moreThanOneKey && keyCount > 0, "More than one key flag set but key count is 0?");

                List<uint> keyOffsets = new List<uint>((int)keyCount);
                for (uint j = 0; j < keyCount; j++)
                {
                    uint offset = (uint)treeStream.ReadBits(12);
                    keyOffsets.Add(offset);
                }

                // Offset to the next segment for the btree
                uint nextSegmentOffset = (uint)treeStream.ReadBits(12);

                for (int j = 0; j < keyCount; j++)
                {
                    treeStream.Position = (int)(segPos + keyOffsets[j]);
                    Debug.Assert(keyOffsets[j] < nextSegmentOffset, "Key offset was beyond next segment?");

                    // Parse key info
                    TKey key = new TKey();
                    key.Deserialize(ref treeStream, _parentToC);
                    Entries.Add(key);
                }

                // Done with this segment, go to next
                if (i != segmentCount - 1)
                    treeStream.Position = (int)(segPos + nextSegmentOffset);
            }
        }

        public void LoadEntriesOld()
        {
            BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

            byte indexBlockCount = treeStream.ReadByte();
            uint segmentCount = (uint)treeStream.ReadBits(12);
            uint unkOffset = (uint)treeStream.ReadBits(12);

            // Iterate through all segments and all their keys
            for (int i = 0; i < segmentCount + 1; i++)
            {
                int segPos = treeStream.Position;

                bool moreThanOneKey = treeStream.ReadBoolBit();
                uint keyCount = (uint)treeStream.ReadBits(11);

                Debug.Assert(moreThanOneKey && keyCount > 0);

                List<uint> keyOffsets = new List<uint>((int)keyCount);
                for (uint j = 0; j < keyCount; j++)
                {
                    uint offset = (uint)treeStream.ReadBits(12);
                    keyOffsets.Add(offset);
                }

                for (int j = 0; j < keyCount; j++)
                {
                    treeStream.Position = (int)(segPos + keyOffsets[j]);

                    // Parse key info
                    TKey key = new TKey();
                    key.Deserialize(ref treeStream, _parentToC);
                    Entries.Add(key);
                }
            }
        }

        public bool TryFindIndex(uint index, out TKey key)
        {
            BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

            byte hasEntries = treeStream.ReadByte();
            uint indexBlockOffset = (uint)treeStream.ReadBits(24);
            short segmentCount = treeStream.ReadInt16();

            for (uint i = 0u; i < segmentCount; ++i)
            {
                int segPos = treeStream.Position;

                bool moreThanOneKey = treeStream.ReadBoolBit();
                uint keyCount = (uint)treeStream.ReadBits(11);

                if (index < keyCount)
                {
                    treeStream.SeekToBit((int)((treeStream.Position * 8) + (index + 1) * 12));
                    int keyOffset = (int)treeStream.ReadBits(12);

                    treeStream.SeekToByte(segPos + keyOffset);

                    key = new TKey();
                    key.Deserialize(ref treeStream, _parentToC);
                    return key != null;
                }

                index -= keyCount;

                uint nextSegmentOffset = (uint)treeStream.ReadBits(12);
                treeStream.SeekToByte((int)(segPos + nextSegmentOffset));
            }

            key = default;
            return false;
        }

        public abstract TKey SearchByKey(Span<byte> data);

        public abstract int LessThanKeyCompareOp(TKey key, Span<byte> data);

        public abstract int EqualToKeyCompareOp(TKey key, Span<byte> data);

        public Span<byte> SearchWithComparison(ref BitStream stream, uint count, TKey key, SearchResult res, SearchCompareMethod method)
        {
            int segPos = stream.Position;

            bool moreThanOneKey = stream.ReadBoolBit();
            uint high = (uint)stream.ReadBits(11); // Segment key count

            uint low = 0;
            uint index = 0;

            res.upperBound = high;

            Span<byte> keyData = default;

            // BSearch segment to compare with our target key
            while (low < high)
            {
                uint mid = low + (high - low) / 2;
                index = mid + 1;

                stream.SeekToBit((int)((segPos * 8) + index * 12));
                int keyOffset = (int)stream.ReadBits(12);
                stream.SeekToByte(segPos + keyOffset);

                keyData = stream.GetSpanFromCurrentPosition();

                int ret;
                if (method == SearchCompareMethod.LessThan)
                    ret = LessThanKeyCompareOp(key, keyData);
                else if (method == SearchCompareMethod.EqualTo)
                    ret = EqualToKeyCompareOp(key, keyData);
                else
                    throw new ArgumentException($"Invalid search method provided '{method}'");

                if (ret == 0)
                {
                    res.lowerBound = mid;
                    res.index = mid;
                    return keyData;
                }
                else if (ret > 0)
                    low = index;
                else if (ret < 0)
                {
                    high = mid;
                    index = mid;
                }
            }

            res.lowerBound = index;
            res.index = ~0u;

            if (count != 0 && index != res.upperBound)
            {
                stream.SeekToBit((int)((segPos * 8) + (index + 1) * 12));
                int keyOffset = (int)stream.ReadBits(12);

                stream.SeekToByte(segPos + keyOffset);
                keyData = stream.GetSpanFromCurrentPosition();
            }

            return keyData;
        }

        public void Serialize(ref BitStream stream, PFSBTree parentTOC)
        {
            int baseTreePos = stream.Position;
            stream.Position += 6;

            short segmentCount = 0; // 1 per 0x1000
            int index = 0; // To keep track of which key we are currently at

            // Following lists is for writing index blocks later on
            BitStream indexStream = new BitStream(BitStreamMode.Write);
            IndexWriter<TKey> indexWriter = new IndexWriter<TKey>();

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
                    TKey key = Entries[index];
                    uint keySize = key.GetSerializedKeySize();

                    // Get the current segment size - apply size of key offsets (12 bits each) (extra (+ 12 + 12) due to segment header and next segment offset)
                    int currentSizeTaken = MiscUtils.MeasureBytesTakenByBits(((double)segmentKeyCount * 12) + 12 + 12);
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
                    // For info btrees, the *file* index within the list is what we write for the game to search
                    TKey current = Entries[index];

                    if (current is FileInfoKey fileInfoKey)
                        indexWriter.AddIndex(ref indexStream, (int)fileInfoKey.FileIndex, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]);
                    else if (current is FileEntryKey fileEntryKey)
                        indexWriter.AddIndex(ref indexStream, (int)fileEntryKey.NameIndex, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]);
                    else if (current is StringKey)
                        indexWriter.AddIndex(ref indexStream, index, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]);
                }


                // Finish up segment header
                stream.Position = baseSegmentPos;
                stream.WriteBoolBit(true);
                stream.WriteBits((ulong)segmentKeyCount, 11);

                int tocSize = MiscUtils.MeasureBytesTakenByBits((segmentKeyCount * 12) + 12 + 12);
                for (int i = 0; i < segmentKeyCount; i++)
                {
                    // Translate each key offset to segment relative offsets
                    stream.WriteBits((ulong)(tocSize + segmentKeyOffsets[i]), 12);
                }

                Span<byte> entryBuffer = entryWriter.GetSpanToCurrentPosition();
                int nextSegmentOffset = tocSize + entryBuffer.Length;

                Debug.Assert(nextSegmentOffset < BTREE_SEGMENT_SIZE, "Next segment offset beyond segment size?");

                stream.WriteBits((ulong)nextSegmentOffset, 12);

                // Write key data
                stream.WriteByteData(entryBuffer);

                segmentCount++;
            }

            // Write index blocks that links all segments together 
            int indexBlocksOffset = stream.Position - baseTreePos;

            if (segmentCount > 1)
            {
                // Hack to check current type
                var k = new TKey();
                TKey lastKey = k.GetLastEntryAsIndex(); // This should be static but w/e

                if (k is FileEntryKey lastEntryIndex)
                {
                    lastEntryIndex.FileExtensionIndex = (uint)(parentTOC.Extensions.Entries.Count);
                    indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, parentTOC.FileNames.Entries.Count, lastKey); // Last index of file name & extension tree
                }
                else if (k is FileInfoKey)
                    indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, (int)(Entries[^1] as FileInfoKey).FileIndex + 1, lastKey); // Last index
                else if (k is StringKey)
                    indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, Entries.Count, lastKey); // Last index

                stream.WriteByteData(indexStream.GetSpanToCurrentPosition());
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

        public enum SearchCompareMethod
        {
            LessThan,
            EqualTo,
        }
    }

    public enum BTreeEndian
    {
        Little,
        Big
    }
}
