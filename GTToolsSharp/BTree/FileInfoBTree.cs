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

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class FileInfoBTree : BTree<FileInfoKey>
    {
        public FileInfoBTree(Memory<byte> buffer)
            : base(buffer)
        {

        }


        public uint SearchIndexByKey(FileInfoKey key)
        {
            /*
            BitStream sr = new BitStream(_buffer.Span);

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
            */
            return 0;
        }

        public FileInfoKey GetByFileIndex(uint fileIndex)
            => Entries.FirstOrDefault(e => e.FileIndex == fileIndex);

        public override int LessThanKeyCompareOp(FileInfoKey key, Span<byte> data) 
	    {
            BitStream bitStream = new BitStream(BitStreamMode.Read, data);

            uint nodeIndex = (uint)bitStream.ReadVarInt();
            if (key.FileIndex < nodeIndex)
                return -1;
            else 
                return 1;
        }

        public override int EqualToKeyCompareOp(FileInfoKey key, Span<byte> data)
        {
            BitStream bitStream = new BitStream(BitStreamMode.Read, data);

            bitStream.ReadByte(); // Skip flag
            uint nodeIndex = (uint)bitStream.ReadVarInt();
            if (key.FileIndex < nodeIndex)
                return -1;
            else if (key.FileIndex > nodeIndex)
                return 1;
            else
                return 0;
        }

        public override FileInfoKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }
    }
}
