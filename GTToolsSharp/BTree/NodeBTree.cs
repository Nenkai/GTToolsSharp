using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

namespace GTToolsSharp.BTree
{
    class NodeBTree : BTree<NodeBTree, NodeKey>
    {
        public NodeBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {

        }

        public override NodeKey ReadKey(ref SpanReader sr)
        {
            return null;
        }

        
        public uint SearchByKey(NodeKey key)
        {
            SpanReader sr = new SpanReader(this._buffer.Span, Syroot.BinaryData.Core.Endian.Big);
            uint count = (uint)ReadByteAtOffset(ref sr, 0);

            sr.Endian = Endian.Little;
            uint offset = ReadUInt24AtOffset(ref sr, 1); // 4th byte is 0
            sr.Endian = Endian.Big;

            sr.Position += (int)offset;
            for (int i = 0; i < count; i++)
            {
                SearchWithComparison(ref sr, count, key, out SearchResult res);
            }

            return uint.MaxValue;
        }


        public override int LessThanKeyCompareOp(NodeKey key, ref SpanReader sr) 
	    {
            uint nodeIndex = (uint)DecodeBitsAndAdvance(ref sr);
            if (key.NodeIndex < nodeIndex)
                return -1;
            else if (key.NodeIndex > nodeIndex)
                return 1;
            else
                throw new Exception("This should not happen");
        }

        public override NodeKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }
    }
}
