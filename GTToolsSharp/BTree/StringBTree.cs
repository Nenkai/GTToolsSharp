using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;

namespace GTToolsSharp.BTree
{
    public class StringBTree : BTree<StringBTree, StringKey>
    {
        public StringBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {

        }

        public override int LessThanKeyCompareOp(StringKey key, ref SpanReader sr)
        {
            throw new NotImplementedException();
        }

        public override StringKey ReadKey(ref SpanReader sr)
        {
            uint len = (uint)DecodeBitsAndAdvance(ref sr);
            return new StringKey(sr.ReadStringRaw((int)len));
        }

        public override StringKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }
    }
}
