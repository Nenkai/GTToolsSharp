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
    class FileInfoBTree : BTree<FileInfoBTree, FileInfoKey>
    {
        public FileInfoBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {

        }

        public override FileInfoKey ReadKeyFromStream(FileInfoKey key, ref SpanReader sr)
        {
            key.Flags = sr.ReadByte();

            key.FileIndex = (uint)DecodeBitsAndAdvance(ref sr);
            key.CompressedSize = (uint)DecodeBitsAndAdvance(ref sr);
            key.UncompressedSize = (key.Flags & 0xF) != 0 ? (uint)DecodeBitsAndAdvance(ref sr) : key.CompressedSize;

            //m_hasMultipleVolumes

            key.SectorIndex = (uint)DecodeBitsAndAdvance(ref sr);
            return key;
        }

        public void WriteKeyToStream(FileInfoKey key, ref SpanWriter sr)
        {
             sr.WriteByte((byte)key.Flags);
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
                ReadKeyFromStream(key, ref data);
                return index;
            }
            else
                return FileInfoKey.InvalidIndex;
        }


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
