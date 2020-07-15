using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

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

        public static ulong DecodeBitsAndAdvance(ref SpanReader sr)
        {
            ulong value = sr.ReadByte();
            ulong mask = 0x80;

            while ((value & mask) != 0)
            {
                value = ((value - mask) << 8) | (sr.ReadByte());
                mask <<= 7;
            }
            return value;
        }

        /// <summary>
        /// Reads bytes within the file stream.
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="data">Buffer of which the data will placed to.</param>
        /// <param name="offset">Offset within the stream.</param>
        /// <param name="size">Size of the buffer to read.</param>
        public static void ReadBytesAt(this FileStream fs, byte[] data, ulong offset, int size)
        {
            fs.Position = (long)offset;
            fs.Read(data, 0, size);
        }

        public static SpanReader GetReaderAtOffset(this SpanReader sr, int offset)
            => new SpanReader(sr.Span.Slice(sr.Position + offset), sr.Endian);

        public static ushort ReadUInt16AtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            ushort val = sr.ReadUInt16();
            sr.Position = curPos;
            return val;
        }

        public static ushort ReadByteAtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            ushort val = sr.ReadByte();
            sr.Position = curPos;
            return val;
        }

        public static uint ReadUInt24AtOffset(ref SpanReader sr, uint offset)
        {
            int curPos = sr.Position;
            sr.Position += (int)offset;
            uint val = (uint)sr.ReadUInt24As32();
            sr.Position = curPos;
            return val;
        }
    }
}
