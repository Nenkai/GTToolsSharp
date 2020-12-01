using System;
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
        Key currentKey;

        public VolumeCryptoTransform(Keyset keyset, uint seed)
        {
            this.currentKey = keyset.ComputeKey(seed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i++)
            {
                byte d = (byte)((((currentKey.Data[0] ^ currentKey.Data[1]) ^ inputBuffer[inputOffset + i]) ^ (currentKey.Data[2] ^ currentKey.Data[3])) & (byte)0xFF);
                currentKey.Data[0] = ((BitOperations.RotateLeft(currentKey.Data[0], 9) & 0x1FE00u) | (currentKey.Data[0] >> 8));
                currentKey.Data[1] = ((BitOperations.RotateLeft(currentKey.Data[1], 11) & 0x7F800u) | (currentKey.Data[1] >> 8));
                currentKey.Data[2] = ((BitOperations.RotateLeft(currentKey.Data[2], 15) & 0x7F8000u) | (currentKey.Data[2] >> 8));
                currentKey.Data[3] = ((BitOperations.RotateLeft(currentKey.Data[3], 21) & 0x1FE00000u) | (currentKey.Data[3] >> 8));

                outputBuffer[outputOffset + i] = d;
            }

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

