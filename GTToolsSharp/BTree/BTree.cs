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
    [DebuggerDisplay("Count = {Entries.Count}, Offset: {_offsetStart}")]
    public abstract class BTree<TKey> where TKey : IBTreeKey<TKey>, new()
    {
        public const int BTREE_SEGMENT_SIZE = 0x1000;

        protected Memory<byte> _buffer;

        public List<TKey> Entries = new List<TKey>();

        public BTree()
        {

        }

        public BTree(Memory<byte> buffer)
        {
            _buffer = buffer;
        }

        public TKey GetByIndex(uint index)
            => Entries[(int)index];

        public void LoadEntries()
        {
            BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

            byte indexBlockCount = treeStream.ReadByte();
            uint indexBlockOffset = (uint)treeStream.ReadBits(24);
            short segmentCount = treeStream.ReadInt16();

            if (this is FileEntryBTree && indexBlockCount != 0)
            {
                List<(int, int, int)> test = new List<(int, int, int)>();

                {
                    treeStream.Position = (int)indexBlockOffset;
                    int indexesCount = (int)treeStream.ReadBits(12);
                    List<int> indexOffsets = new List<int>();
                    for (int i = 0; i < indexesCount; i++)
                        indexOffsets.Add((int)treeStream.ReadBits(12));
                    int nextSeg = (int)treeStream.ReadBits(12);

                    for (int i = 0; i < indexesCount; i++)
                    {
                        int nameIndex = (int)treeStream.ReadVarInt();
                        int extIndex = (int)treeStream.ReadVarInt();
                        int childOffset = (int)treeStream.ReadVarInt();

                        test.Add((nameIndex, extIndex, childOffset));
                    }

                }
                treeStream.Position = 6;
            }

            
            

            // Iterate through all segments and all their keys
            for (int i = 0; i < segmentCount; i++)
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

                // Offset to the next segment for the btree
                uint nextSegmentOffset = (uint)treeStream.ReadBits(12);

                for (int j = 0; j < keyCount; j++)
                {
                    treeStream.Position = (int)(segPos + keyOffsets[j]);

                    // Parse key info
                    TKey key = new TKey();
                    key.Deserialize(ref treeStream);
                    Entries.Add(key);
                }

                // Done with this segment, go to next
                if (i != segmentCount - 1)
                    treeStream.Position = (int)(segPos + nextSegmentOffset);
            }
        }

        public void LoadEntriesOld()
        {
            /*
            SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position += _offsetStart;

            sr.Position += 1;
            uint nodeCount = CryptoUtils.GetBitsAt(ref sr, (uint)sr.Position, 0);
            sr.Position += 1;

            uint lastDataPos = (uint)sr.Position;
            for (int i = 0; i < nodeCount + 1; i++)
            {
                uint keyCount = GetBitsAt(ref sr, lastDataPos, 0) & 0b111_1111_1111u;
                GetBitsAt(ref sr, lastDataPos, keyCount + 1); 

                for (uint j = 0; j < keyCount; j++)
                {
                    uint offset = GetBitsAt(ref sr, lastDataPos, j + 1);
                    sr.Position = (int)lastDataPos + (int)offset;

                    TKey key = new TKey();
                    key.Deserialize(ref sr);
                    Entries.Add(key);
                }

                lastDataPos = (uint)sr.Position;
            }
            */
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
                    key.Deserialize(ref treeStream);
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

        public abstract void Serialize(ref BitStream stream, GTVolumeTOC parentTOC);

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

                keyData = stream.GetSpanOfCurrentPosition();

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
                keyData = stream.GetSpanOfCurrentPosition();
            }

            return keyData;
        }



        public enum SearchCompareMethod
        {
            LessThan,
            EqualTo,
        }
    }
}
