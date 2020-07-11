using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

namespace GTToolsSharp
{
    public static class Utils
    {
        public static uint ReverseEndian(this uint x)
        {
            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }

        public static uint RotateLeft(uint val, int places)
            => (val << places) | (val >> (32 - places)); // 32 = bit count, size * byte bit size;

        public static uint AlignUp(uint x, uint alignment)
        {
            uint mask = ~(alignment - 1);
            return (x + (alignment - 1)) & mask;
        }

        public static uint ReadUInt24As32(this ref SpanReader sr)
        { 
            if (sr.Endian == Endian.Big)
                return (uint)sr.ReadByte() | (uint)sr.ReadByte() << 8 | (uint)sr.ReadByte() << 16;
            else
                return (uint)sr.ReadByte() << 16 | (uint)sr.ReadByte() << 8 | (uint)sr.ReadByte();
        }


    }
}
