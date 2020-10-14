using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace GTToolsSharp.Encryption
{
    class VolumeCryptoTransform : ICryptoTransform
    {
        Key currentKey;

        public VolumeCryptoTransform(Keyset keyset, uint seed)
        {
            this.currentKey = keyset.ComputeKey(seed);
        }

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
        {
            for (int i = 0; i < inputCount; i++)
            {
                byte d = (byte)((((currentKey.Data[0] ^ currentKey.Data[1]) ^ inputBuffer[inputOffset + i]) ^ (currentKey.Data[2] ^ currentKey.Data[3])) & (byte)0xFF);

                currentKey.Data[0] = ((Keyset.RotateLeft(currentKey.Data[0], 9) & 0x1FE00u) | (currentKey.Data[0] >> 8));
                currentKey.Data[1] = ((Keyset.RotateLeft(currentKey.Data[1], 11) & 0x7F800u) | (currentKey.Data[1] >> 8));
                currentKey.Data[2] = ((Keyset.RotateLeft(currentKey.Data[2], 15) & 0x7F8000u) | (currentKey.Data[2] >> 8));
                currentKey.Data[3] = ((Keyset.RotateLeft(currentKey.Data[3], 21) & 0x1FE00000u) | (currentKey.Data[3] >> 8));

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

