using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

using static GTToolsSharp.Utils;

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
			SpanReader sr = new SpanReader(_buffer.Span, Endian.Big);
			uint offsetAndCount = sr.ReadUInt32();

			uint nodeCount = sr.ReadUInt16();

			for (uint i = 0u; i < nodeCount; ++i)
			{
                uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, high + 1);

                if (index < high)
					break;

				index -= high;

                sr.Position += (int)nextOffset;
			}

            uint offset = GetBitsAt(ref sr, index + 1);
            sr.Position += (int)offset;

            key = ReadKeyFromStream(default, ref sr);
            return key != null;
		}

        public abstract TKey ReadKeyFromStream(TKey key, ref SpanReader sr);

        public abstract TKey SearchByKey(ref SpanReader sr);

        public abstract int LessThanKeyCompareOp(TKey key, ref SpanReader sr);

        public abstract int EqualToKeyCompareOp(TKey key, ref SpanReader sr);

        public SpanReader SearchWithComparison(ref SpanReader sr, uint count, TKey key, SearchResult res, SearchCompareMethod method)
        {
            uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
            uint low = 0;
            uint index = 0;

            res.upperBound = high;

            SpanReader subData = default;
            while (low < high)
            {
                uint mid = low + (high - low) / 2;
                index = mid + 1;
                uint offset = GetBitsAt(ref sr, index);

                subData = sr.GetReaderAtOffset((int)offset);

                int ret;
                if (method == SearchCompareMethod.LessThan)
                    ret = LessThanKeyCompareOp(key, ref subData);
                else if (method == SearchCompareMethod.EqualTo)
                    ret = EqualToKeyCompareOp(key, ref subData);
                else
                    throw new ArgumentException($"Invalid search method provided '{method}'");

                if (ret == 0)
                {
                    res.lowerBound = mid;
                    res.index = mid;
                    return subData;
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
                subData = sr.GetReaderAtOffset((int)offset);
            }

            return subData;
        }

        public static ushort GetBitsAt(ref SpanReader sr, uint offset)
        {
            uint offsetAligned = (offset * 12) / 8;
            ushort result = Utils.ReadUInt16AtOffset(ref sr, offsetAligned);
            if ((offset & 0x1) == 0)
                result >>= 4;
            return (ushort)(result & 0xFFFu);
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

        public enum SearchCompareMethod
        {
            LessThan,
            EqualTo,
        }
    }
}
