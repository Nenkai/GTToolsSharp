using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    class NodeKey
    {
        public uint Flags { get; }
        public uint NodeIndex { get; }
        public uint Size1 { get; }
        public uint Size2 { get; }

        public NodeKey(uint nodeIndex)
        {
            NodeIndex = nodeIndex;
        }
    }
}
