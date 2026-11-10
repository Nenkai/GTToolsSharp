//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

namespace GTToolsSharp.Encryption;

public struct Key
{
    public uint[] Data { get; set; }

    public Key(uint data1, uint data2, uint data3, uint data4)
    {
        Data = [data1, data2, data3, data4];
    }
}
