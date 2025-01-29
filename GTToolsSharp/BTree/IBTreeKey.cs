using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PDTools.Utils;

namespace GTToolsSharp.BTree;

public interface IBTreeKey<T>
{
    public T GetLastEntryAsIndex();

    public T CompareGetDiff(T key);

    public void Deserialize(ref BitStream sr, PFSBTree parentToC);

    public void Serialize(ref BitStream sr);

    public void SerializeIndex(ref BitStream sr);

    public uint GetSerializedKeySize();

    public uint GetSerializedIndexSize();

}
