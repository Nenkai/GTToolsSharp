using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Numerics;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

namespace GTToolsSharp.Utils
{
    public static class CryptoUtils
    {
        public static uint ShuffleBits(uint x)
        {
            uint crc = 0;
            for (uint i = 0; i < 4; ++i)
            {
                crc = (crc << 8) ^ CRC32.checksum[(BitOperations.RotateLeft(x ^ crc, 10) & 0x3FC) >> 2];
                x <<= 8;
            }
            return ~crc;
        }
    }
}
