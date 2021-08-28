using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class StringKey : IBTreeKey<StringKey>
    {
        public StringKey() { }

        public StringKey(string val)
            => Value = val;

        public string Value { get; private set; }

        public void Deserialize(ref BitStream stream, GTVolumeTOC parentToC)
        {
            Value = stream.ReadVarPrefixString();
        }

        public void Serialize(ref BitStream stream)
        {
            stream.WriteVarPrefixString(Value);
        }

        public void SerializeIndex(ref BitStream stream)
        {
            if (Value.Length > 0 && (byte)Value[0] == 255)
            {
                // Work around ignore encoding
                stream.WriteByte(1);
                stream.WriteByte(255);
            }
            else
                stream.WriteVarPrefixString(Value);
        }

        public StringKey GetLastIndex()
        {
            return new StringKey(((char)255).ToString()); // Max char comparison
        }

        public StringKey CompareGetDiff(StringKey nextKey)
        {
            int maxLen = Math.Max(this.Value.Length, nextKey.Value.Length);
            for (int i = 0; i < maxLen; i++)
            {
                if (i >= this.Value.Length || this.Value[i] != nextKey.Value[i])
                    return new StringKey(nextKey.Value.Substring(0, i + 1));
            }

            throw new ArgumentException("Both keys are equal.");
        }

        public uint GetSerializedKeySize()
            => (uint)BitStream.GetSizeOfVariablePrefixString(Value);

        public uint GetSerializedIndexSize()
            => (uint)BitStream.GetSizeOfVariablePrefixString(Value);

        public override string ToString()
            => Value;
    }
}
