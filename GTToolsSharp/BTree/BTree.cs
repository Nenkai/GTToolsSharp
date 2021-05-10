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

namespace GTToolsSharp.BTree
{
    [DebuggerDisplay("Count = {Entries.Count}, Offset: {_offsetStart}")]
    public abstract class BTree<TKey> where TKey : IBTreeKey, new()
    {
        protected byte[] _buffer;
        protected int _offsetStart;

        public List<TKey> Entries = new List<TKey>();

        public BTree()
        {

        }

        public BTree(byte[] buffer, int offsetStart)
        {
            _buffer = buffer;
            _offsetStart = offsetStart;
        }

        public TKey GetByIndex(uint index)
            => Entries[(int)index];

        public void LoadEntries()
        {
            SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position = (int)_offsetStart;

            uint offsetAndCount = sr.ReadUInt32();
            uint nodeCount = sr.ReadUInt16();

            for (int i = 0; i < nodeCount; i++)
            {
                uint keyCount = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, keyCount + 1);

                for (uint j = 0; j < keyCount; j++)
                {
                    uint offset = GetBitsAt(ref sr, j + 1);
                    var data = sr.GetReaderAtOffset((int)offset);

                    TKey key = new TKey();
                    key.Deserialize(ref data);
                    Entries.Add(key);
                }

                sr.Position += (int)nextOffset;
            }
        }

        public void LoadEntriesOld()
        {
            SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position += _offsetStart;

            sr.Position += 1;
            uint nodeCount = CryptoUtils.GetBitsAt(ref sr, (uint)sr.Position, 0);
            sr.Position += 1;

            uint lastDataPos = (uint)sr.Position;
            for (int i = 0; i < nodeCount + 1; i++)
            {
                uint keyCount = GetBitsAt(ref sr, lastDataPos, 0) & 0x7FFu;
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
        }

        public bool TryFindIndex(uint index, out TKey key)
        {
			SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position = _offsetStart;
			uint offsetAndCount = sr.ReadUInt32();

			uint segmentCount = sr.ReadUInt16();

			for (uint i = 0u; i < segmentCount; ++i)
			{
                uint keyCount = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, keyCount + 1);

                if (index < keyCount)
					break;

				index -= keyCount;

                sr.Position += (int)nextOffset;
			}

            uint offset = GetBitsAt(ref sr, index + 1);
            sr.Position += (int)offset;

            key = new TKey();
            key.Deserialize(ref sr);
            return key != null;
		}

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

        public enum SearchCompareMethod
        {
            LessThan,
            EqualTo,
        }
    }
}
