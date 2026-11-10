//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System;
using System.Linq;

using PDTools.Utils;

namespace GTToolsSharp.BTree;

public class FileEntryBTree : BTree<FileEntryKey>
{
    public FileEntryBTree() { }
    public FileEntryBTree(Memory<byte> buffer, PFSBTree parentToC)
        : base(buffer, parentToC)
    {

    }

    public override int EqualToKeyCompareOp(FileEntryKey key, Span<byte> data)
    {
        BitStream stream = new BitStream(BitStreamMode.Read, data);

        uint nameIndex = stream.ReadUInt32();
        if (key.NameIndex < nameIndex)
            return -1;
        else if (key.NameIndex > nameIndex)
            return 1;

        uint extIndex = stream.ReadUInt32();

        if (key.FileExtensionIndex < extIndex)
            return -1;
        else if (key.FileExtensionIndex > extIndex)
            return 1;

        return 0;
    }

    public override int LessThanKeyCompareOp(FileEntryKey key, Span<byte> data)
    {
        BitStream stream = new BitStream(BitStreamMode.Read, data);

        uint nameIndex = stream.ReadUInt32();
        if (key.NameIndex < nameIndex)
            return -1;
        else if (key.NameIndex > nameIndex)
            return 1;

        uint extIndex = stream.ReadUInt32();

        if (key.FileExtensionIndex < extIndex)
            return -1;
        else if (key.FileExtensionIndex > extIndex)
            return 1;
        else
            throw new Exception("?????");
    }

    public override FileEntryKey SearchByKey(Span<byte> data)
    {
        throw new NotImplementedException();
    }


    public void ResortByIndices()
    {
        // When the game needs to search for rows, it will bsearch each byte of the key's data (essentially: name index + extension index + file info index)
        // So ensure that we order these.
        Entries = Entries.OrderBy(e => e.NameIndex)
            .ThenBy(e => e.FileExtensionIndex)
            .ToList();
    }

    public FileEntryKey GetFolderEntryByNameIndex(uint nameIndex)
    {
        foreach (var entry in Entries)
        {
            if (entry.NameIndex == nameIndex && entry.FileExtensionIndex == 0)
                return entry;
        }

        return null;
    }

}
