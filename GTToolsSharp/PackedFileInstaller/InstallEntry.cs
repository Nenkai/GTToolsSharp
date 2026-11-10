//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using Syroot.BinaryData.Memory;

namespace GTToolsSharp.PackedFileInstaller;

public class InstallEntry
{
    public string Path { get; set; }
    public long FileSize { get; set; }
    public int BlockIndex { get; set; }
    public int PathNameOffset { get; set; }

    public static InstallEntry Read(ref SpanReader sr)
    {
        InstallEntry entry = new InstallEntry();

        entry.FileSize = sr.ReadInt64();
        entry.BlockIndex = sr.ReadInt32();
        entry.PathNameOffset = sr.ReadInt32();

        sr.Position = entry.PathNameOffset;
        entry.Path = sr.ReadString0();

        return entry;
    }

    public override string ToString()
        => $"{Path} (FileSize: {FileSize}, Block Index: {BlockIndex})";
}
