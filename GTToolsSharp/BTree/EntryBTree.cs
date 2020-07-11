using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

namespace GTToolsSharp.BTree
{
    class EntryBTree : BTree<EntryBTree, EntryKey>
    {
        public EntryBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {
            
        }

        public void Traverse(EntryUnpacker unpacker)
        {
            SpanReader sr = new SpanReader(_buffer.Span, Endian.Big);

            uint offsetAndCount = sr.ReadUInt32();
            uint nodeCount = sr.ReadUInt16();

            for (int i = 0; i < nodeCount; i++)
            {
                uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, high + 1);

                for (uint j = 0; j < high; ++j) // high is pretty much entry count
                {
                    uint offset = GetBitsAt(ref sr, j + 1) & 0x7FFu;
                    sr.Position += (int)offset;

                    EntryKey key = ReadKey(ref sr);
                    unpacker.UnpackFromKey(key);
                }

                sr.Position += (int)nextOffset;
            }


        }

        public override EntryKey ReadKey(ref SpanReader sr)
        {
            EntryKey k = new EntryKey();
            k.Flags = (EntryKeyFlags)sr.ReadByte();
            k.NameIndex = (uint)DecodeBitsAndAdvance(ref sr);
            k.ExtIndex = k.Flags.HasFlag(EntryKeyFlags.File) ? (uint)DecodeBitsAndAdvance(ref sr) : 0;
            k.LinkIndex = (uint)DecodeBitsAndAdvance(ref sr);

            return k;
        }

        public override EntryKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }

        public override int LessThanKeyCompareOp(EntryKey key, ref SpanReader sr)
        {
            uint nameIndex = sr.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = sr.ReadUInt32();

            if (key.ExtIndex < extIndex)
                return -1;
            else if (key.ExtIndex > extIndex)
                return 1;
            else
                throw new Exception("?????");
        }
    }
}
