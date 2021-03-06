﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace GTToolsSharp.Encryption
{
    class VolumeCryptoTransform : ICryptoTransform
    {
        private byte[] bitsTable;
        private ulong pos = 0;

        public VolumeCryptoTransform(Keyset keyset, uint seed)
        {
            uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
            uint[] keys = VolumeCrypto.PrepareKey(crc ^ seed, keyset.Key.Data);
            bitsTable = VolumeCrypto.GenerateBitsTable(keys);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            VolumeCrypto.DecryptBuffer(inputBuffer, outputBuffer, inputCount, bitsTable, pos);

            pos += (ulong)inputCount;
            return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
            var transformed = new byte[inputCount];
            TransformBlock(inputBuffer, inputOffset, inputCount, transformed, 0);
            return transformed;
        }

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
        }

    }
}

