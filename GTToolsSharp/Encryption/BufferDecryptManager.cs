//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace GTToolsSharp.Encryption;

/// <summary>
/// Used for decryption of specific keys (used in GT5P JP Demo).
/// </summary>
public class BufferDecryptManager
{
    /// <summary>
    /// Keys for decryption. (Path, base64 Key)
    /// </summary>
    public Dictionary<string, string> Keys { get; set; }

}
