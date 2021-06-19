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
