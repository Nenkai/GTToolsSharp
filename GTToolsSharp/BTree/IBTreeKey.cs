//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

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
