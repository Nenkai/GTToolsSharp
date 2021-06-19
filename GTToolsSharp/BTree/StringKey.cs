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

        public void Deserialize(ref BitStream stream)
        {
            Value = stream.ReadVarPrefixString();
        }

        public void Serialize(ref BitStream stream)
        {
            stream.WriteVarPrefixString(Value);
        }

        public void SerializeIndex(ref BitStream stream)
        {
            stream.WriteVarPrefixString(Value);
        }

        public StringKey GetLastIndex()
        {
            return new StringKey(Encoding.ASCII.GetString(new byte[] { 255 })); // Max char comparison
        }

        public StringKey CompareGetDiff(StringKey nextKey)
        {
            int maxLen = Math.Max(this.Value.Length, nextKey.Value.Length);
            for (int i = 0; i < maxLen; i++)
            {
                if (i >= nextKey.Value.Length || this.Value[i] != nextKey.Value[i])
                    return new StringKey(nextKey.Value.Substring(i + 1));
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
