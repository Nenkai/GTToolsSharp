//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace GTToolsSharp;

public class PackCache
{
    public Dictionary<string, PackedCacheEntry> Entries { get; set; } = [];
    public bool Invalidated { get; set; }

    public bool HasValidCachedEntry(InputPackEntry inputFile, uint fileIndex, out PackedCacheEntry cacheEntry)
    {
        if (Entries.TryGetValue(inputFile.VolumeDirPath, out PackedCacheEntry cachedEntry))
        {
            cacheEntry = cachedEntry;
            if (cachedEntry.FileIndex == fileIndex && cachedEntry.FileSize == inputFile.FileSize && cachedEntry.LastModified.Equals(inputFile.LastModified))
                return true;
        }

        cacheEntry = null;
        return false;
    }

    public void Save(string path)
    {
        using var fs = File.Open(path, FileMode.Create);
        using var ts = new StreamWriter(fs);

        foreach (var entry in Entries)
        {
            ts.Write(entry.Key); ts.Write("\t");
            ts.Write(entry.Value.FileIndex); ts.Write("\t");
            ts.Write(entry.Value.LastModified); ts.Write("\t");
            ts.Write(entry.Value.FileSize); ts.Write("\t");
            ts.WriteLine(entry.Value.CompressedFileSize);
        }
    }
}

public class PackedCacheEntry
{
    public string VolumePath { get; set; }
    public uint FileIndex { get; set; }
    public long FileSize { get; set; }
    public long CompressedFileSize { get; set; }

    public string PDIPFSPath { get; set; }
    public DateTime LastModified { get; set; }
}
