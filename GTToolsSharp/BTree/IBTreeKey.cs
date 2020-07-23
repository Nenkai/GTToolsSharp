using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;

namespace GTToolsSharp.BTree
{
    public interface IBTreeKey
    {
        public void Deserialize(ref SpanReader sr);

        public uint GetSerializedKeySize();
    }
}
