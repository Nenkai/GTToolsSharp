using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;


namespace GTToolsSharp.Utils
{
    public static class CryptoUtils
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

        public static void WriteUInt24BE(this BinaryStream bs, uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            bytes.Clear();

            BitConverter.TryWriteBytes(bytes, value);

            bytes.Reverse();
            bs.Write(bytes[1]);
            bs.Write(bytes[2]);
            bs.Write(bytes[3]);
        }

        public static ushort GetBitsAt(ref SpanReader sr, uint offset)
        {
            uint offsetAligned = (offset * 0x10 - offset * 4) / 8;
            ushort result = CryptoUtils.ReadUInt16AtOffset(ref sr, offsetAligned);
            if ((offset & 0x1) == 0)
                result >>= 4;

            return (ushort)(result & 0xFFF);
        }

        public static void WriteBitsAt(BinaryStream bs, uint data, uint offset)
        {
            uint offsetAligned = (offset * 12) / 8;
            bs.Position = (int)offsetAligned;

            if ((offset & 0x1) == 0)
            {
                data <<= 4;
                bs.Write((ushort)data);
            }
            else
            {
                int value = bs.ReadByte() << 8;
                bs.Position--;
                bs.Write((ushort)(data + value));
            }
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

        public static void EncodeAndAdvance(ref SpanWriter sw, uint value)
        {
            uint mask = 0x80;
            Span<byte> buffer = Array.Empty<byte>();

            if (value <= 0x7F)
            {
                sw.WriteByte((byte)value);
                return;
            }
            else if (value <= 0x3FFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(2, 2);
            }
            else if (value <= 0x1FFFFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(1, 3);
            }
            else if (value <= 0xFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
            }
            else if (value <= 0xFFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
                buffer = new byte[] { 0, buffer[0], buffer[1], buffer[2], buffer[3] };
            }
            else
                throw new Exception("????");

            for (int i = 1; i < buffer.Length; i++)
            {
                buffer[0] += (byte)mask;
                mask >>= 1;
            }

            sw.WriteBytes(buffer);
        }

        public static void EncodeAndAdvance(BinaryStream bs, uint value)
        {
            uint mask = 0x80;
            Span<byte> buffer = Array.Empty<byte>();

            if (value <= 0x7F)
            {
                bs.WriteByte((byte)value);
                return;
            }
            else if (value <= 0x3FFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(2, 2);
            }
            else if (value <= 0x1FFFFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(1, 3);
            }
            else if (value <= 0xFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
            }
            else if (value <= 0xFFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
                buffer = new byte[] { 0, buffer[0], buffer[1], buffer[2], buffer[3] };
            }
            else
                throw new Exception("????");

            for (int i = 1; i < buffer.Length; i++)
            {
                buffer[0] += (byte)mask;
                mask >>= 1;
            }

            bs.Write(buffer);
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

        public static uint ShuffleBits(uint x)
        {
            uint crc = 0;
            for (uint i = 0; i < 4; ++i)
            {
                crc = (crc << 8) ^ CRC32.checksum[(CryptoUtils.RotateLeft(x ^ crc, 10) & 0x3FC) >> 2];
                x <<= 8;
            }
            return ~crc;
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
