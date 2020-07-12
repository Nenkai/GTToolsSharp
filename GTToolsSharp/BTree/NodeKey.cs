using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    public class NodeKey
    {
        public uint Flags { get; set; }
        public uint NodeIndex { get; set; } = InvalidIndex;
        public uint CompressedSize { get; set; }
        public uint UncompressedSize { get; set; }
        public uint VolumeIndex { get; set; } = InvalidIndex;
        public uint SectorIndex { get; set; }

        public const uint InvalidIndex = uint.MaxValue;

        public NodeKey(uint nodeIndex)
        {
            NodeIndex = nodeIndex;
        }
    }
}
