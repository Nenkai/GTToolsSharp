//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System;

using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.Headers;

public class FileDeviceGTFSHeader : PFSVolumeHeaderBase
{
    public override int HeaderSize => 0x14;

    private static readonly byte[] HeaderMagic = [0x5B, 0x74, 0x51, 0x61];

    public bool HasCustomGameID { get; set; }

    public override void Read(Span<byte> buffer)
    {
        SpanReader sr = new SpanReader(buffer, Endian.Big);

        Magic = sr.ReadBytes(4);
        sr.Position += 4;
        ToCNodeIndex = sr.ReadUInt32();
        CompressedTOCSize = sr.ReadUInt32();
        ExpandedTOCSize = sr.ReadUInt32();
    }

    public override byte[] Serialize()
    {
        throw new NotImplementedException();
    }

    public override void PrintInfo()
    {
        Program.Log($"[>] PFS Version/Serial No: '{SerialNumber}' ({new DateTime(2001, 1, 1) + TimeSpan.FromSeconds(SerialNumber)})");
        Program.Log($"[>] Table of Contents Entry Index: {ToCNodeIndex}");
        Program.Log($"[>] TOC Size: 0x{CompressedTOCSize:X8} bytes (0x{ExpandedTOCSize:X8} expanded)");
    }
}
