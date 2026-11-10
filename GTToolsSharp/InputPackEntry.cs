//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System;

namespace GTToolsSharp;

public class InputPackEntry
{
    /// <summary>
    /// Full path on the file system.
    /// </summary>
    public string FullPath { get; set; }

    /// <summary>
    /// Path relative to the volume.
    /// </summary>
    public string VolumeDirPath { get; set; }

    /// <summary>
    /// Uncompressed file size of this entry.
    /// </summary>
    public uint FileSize { get; set; }

    /// <summary>
    /// When the file was last modified to use against packing cache.
    /// </summary>
    public DateTime LastModified { get; set; }

    public bool IsAddedFile { get; set; }

    /// <summary>
    /// MD5 checksum of the file, mostly used for patching.
    /// </summary>
    public string MD5Checksum { get; set; }
}
