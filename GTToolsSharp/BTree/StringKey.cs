using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;

namespace GTToolsSharp.BTree
{
    public class StringKey : IBTreeKey
    {
        public StringKey() { }

        public StringKey(string val)
            => Value = val;

        public string Value { get; private set; }

        public void Deserialize(ref SpanReader sr)
            => Value = sr.ReadStringRaw(sr.ReadByte());

        public uint GetSerializedKeySize()
            => (uint)Value.Length + 1u;

        public override string ToString()
            => Value;
    }
}
