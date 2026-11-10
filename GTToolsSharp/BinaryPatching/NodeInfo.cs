//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

namespace GTToolsSharp.BinaryPatching;

public class NodeInfo
{
    /// <summary>
    /// The new entry for the file info.
    /// </summary>
    public uint NewEntryIndex { get; set; }

    /// <summary>
    /// The new expected file size for the file info.
    /// </summary>
    public uint NewFileSize { get; set; }

    /// <summary>
    /// The old file state after applying.
    /// </summary>
    public TPPSFileState OldFileInfoFlags { get; set; }

    /// <summary>
    /// MD5 Check of the new file.
    /// </summary>
    public string MD5Checksum { get; set; }

    /// <summary>
    /// The older entry index for the file info.
    /// </summary>
    public uint CurrentEntryIndex { get; set; }

    /// <summary>
    /// The new file state after applying.
    /// </summary>
    public TPPSFileState NewFileInfoFlags { get; set; }

    /// <summary>
    /// The older compressed file size.
    /// </summary>
    public uint NewCompressedFileSize { get; set; }
}
