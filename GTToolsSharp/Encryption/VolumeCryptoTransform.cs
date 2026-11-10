//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GTToolsSharp
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

using PDTools.Hashing;

namespace GTToolsSharp.Encryption;

public class VolumeCryptoTransform : ICryptoTransform
{
    private readonly byte[] _bitsTable;
    private ulong _offset = 0;

    public VolumeCryptoTransform(Keyset keyset, uint seed, ulong offset = 0)
    {
        uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
        uint[] keys = VolumeCrypto.PrepareKey(crc ^ seed, keyset.Key.Data);
        _bitsTable = VolumeCrypto.GenerateBitsTable(keys);

        _offset = offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
    {
        VolumeCrypto.DecryptBuffer(inputBuffer, outputBuffer, inputCount, _bitsTable, _offset);

        _offset += (ulong)inputCount;
        return inputCount;
    }

    public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
    {
        var transformed = new byte[inputCount];
        TransformBlock(inputBuffer, inputOffset, inputCount, transformed, 0);
        return transformed;
    }

    public void SetOffset(ulong offset)
        => _offset = offset;

    public bool CanReuseTransform
    {
        get { return true; }
    }

    public bool CanTransformMultipleBlocks
    {
        get { return true; }
    }

    public int InputBlockSize
    {
        // 4 bytes in uint
        get { return 4; }
    }

    public int OutputBlockSize
    {
        get { return 4; }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

}

