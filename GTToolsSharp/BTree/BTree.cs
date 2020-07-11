using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using GTToolsSharp.BTree;

namespace GTToolsSharp.BTree
{
    public abstract class BTree<TBTree, TKey>
    {
        public Memory<byte> _buffer;

        public BTree(byte[] buffer, int offset)
        {
            _buffer = buffer.AsMemory(offset);
        }

        public bool TryFindIndex(uint index, out TKey key)
        {
			SpanReader sr = new SpanReader(_buffer.Span, Syroot.BinaryData.Core.Endian.Big);
			uint offsetAndCount = sr.ReadUInt32();
			uint nodeCount = sr.ReadUInt16();

			for (uint i = 0u; i < nodeCount; ++i)
			{
                uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, high + 1);

                if (index < high)
					break;

				index -= high;

                sr.Position += (int)nextOffset;// = advancePointer(p, nextOffset);
			}

            uint offset = GetBitsAt(ref sr, index + 1);
            sr.Position += (int)offset; // p = advancePointer(p, offset);

            key = ReadKey(ref sr);
            return key != null;
		}

        public abstract TKey ReadKey(ref SpanReader sr);

        public abstract TKey SearchByKey(ref SpanReader sr);

        public abstract int LessThanKeyCompareOp(TKey key, ref SpanReader sr);

        public int SearchWithComparison(ref SpanReader sr, uint count, TKey key, out SearchResult res)
        {
            res = new SearchResult();
            uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
            uint low = 0;
            uint index = 0;

            res.upperBound = high;

            SpanReader subData = sr;
            while (low < high)
            {
                uint mid = low + (high - low) / 2;
                index = mid + 1;
                uint offset = GetBitsAt(ref sr, index);

                sr.Position += (int)offset;
                subData = sr;

                int ret = LessThanKeyCompareOp(key, ref subData);
                if (ret == 0)
                {
                    res.lowerBound = mid;
                    res.index = mid;
                    return subData.Position;
                }
                else if (ret > 0)
                    low = index;
                else if (ret < 0)
                {
                    high = mid;
                    index = mid;
                }
            }

            subData.Position = -1;

            res.lowerBound = index;
            res.index = ~0u;

            if (count != 0 && index != res.upperBound)
            {
                uint offset = GetBitsAt(ref sr, index + 1);
                subData = sr.Slice((int)offset);
            }

            return subData.Position;
        }

        public static ushort GetBitsAt(ref SpanReader sr, uint offset)
        {
            uint offsetAligned = (offset * 12) / 8;
            ushort result = ReadUInt16AtOffset(ref sr, offsetAligned);
            if ((offset & 0x1) == 0)
                result >>= 4;
            return (ushort)(result & 0xFFF);
        }

        public static ulong DecodeBitsAndAdvance(ref SpanReader sr)
        {
            ulong value = sr.ReadByte();
            ulong mask = 0x80;

            while ((value & mask) != 0)
            {
                value = ((value - mask) << 8) | (sr.ReadByte());
                mask <<= 7;
            }
            return value;
        }

        public static ushort ReadUInt16AtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            ushort val = sr.ReadUInt16();
            sr.Position = curPos;
            return val;
        }

        public static ushort ReadByteAtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            ushort val = sr.ReadByte();
            sr.Position = curPos;
            return val;
        }

        public static uint ReadUInt24AtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            uint val = (uint)sr.ReadUInt24As32();
            sr.Position = curPos;
            return val;
        }
    }
}
