using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;

using System.IO;
using System.IO.Compression;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

using ICSharpCode.SharpZipLib.Zip.Compression;

using GTToolsSharp.Encryption;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace GTToolsSharp.Utils
{
    public class MiscUtils
    {
        // https://stackoverflow.com/a/4975942
        private static string[] sizeSuf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
        public static string BytesToString(long byteCount)
        {
            if (byteCount == 0)
                return "0" + sizeSuf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + sizeSuf[place];
        }

        // https://stackoverflow.com/a/9995303
        public static byte[] StringToByteArray(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new Exception("The binary key cannot have an odd number of digits");

            byte[] arr = new byte[hex.Length >> 1];

            for (int i = 0; i < hex.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
            }

            return arr;
        }

        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            //return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        public static byte[] ZlibCompress(byte[] input)
        {
            using var ms = new MemoryStream(input.Length);
            using var bs = new BinaryStream(ms);
            bs.WriteUInt32(Constants.ZLIB_MAGIC);
            bs.WriteInt32(-input.Length);

            /* For some reason System.IO.Compression has issues making the games load these files?
            using var ds = new DeflateStream(ms, CompressionLevel.Optimal);
            ds.Write(input, 0, input.Length);
            ds.Flush();
            */

            // Fall back to ICSharpCode
            var d = new Deflater(Deflater.DEFAULT_COMPRESSION, true);
            d.SetInput(input);
            d.Finish();

            int count = d.Deflate(input);
            bs.Write(input, 0, count);

            return ms.ToArray();
        }

        /// <summary>
        /// Decompresses a file (in memory, unsuited for large files).
        /// </summary>
        /// <param name="data"></param>
        /// <param name="outSize"></param>
        /// <param name="deflatedData"></param>
        /// <returns></returns>
        public unsafe static bool TryInflateInMemory(Span<byte> data, ulong outSize, out byte[] deflatedData)
        {
            deflatedData = Array.Empty<byte>();
            if (outSize > uint.MaxValue)
                return false;

            // Inflated is always little
            var sr = new SpanReader(data, Endian.Little);
            uint zlibMagic = sr.ReadUInt32();
            uint sizeComplement = sr.ReadUInt32();

            if ((long)zlibMagic != Constants.ZLIB_MAGIC)
                return false;

            if ((uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;
            if (sr.Length <= headerSize) // Header size, if it's under, data is missing
                return false;

            deflatedData = new byte[(int)outSize];
            fixed (byte* pBuffer = &sr.Span.Slice(headerSize)[0]) // Vol Header Size
            {
                using var ums = new UnmanagedMemoryStream(pBuffer, sr.Span.Length - headerSize);
                using var ds = new DeflateStream(ums, CompressionMode.Decompress);
                ds.Read(deflatedData, 0, (int)outSize);
            }

            return true;
        }

        /// <summary>
        /// Checks if compression is valid for the buffer.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="outSize"></param>
        /// <returns></returns>
        public unsafe static bool CheckCompression(Span<byte> data, ulong outSize)
        {
            if (outSize > uint.MaxValue)
                return false;

            // Inflated is always little
            var sr = new SpanReader(data, Endian.Little);
            uint zlibMagic = sr.ReadUInt32();
            uint sizeComplement = sr.ReadUInt32();

            if ((long)zlibMagic != Constants.ZLIB_MAGIC)
                return false;

            if ((uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;       
            if (sr.Length <= headerSize) // Header size, if it's under, data is missing
                return false;

            return true;
        }

        /// <summary>
        /// Checks if compression is valid for the stream.
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="keyset"></param>
        /// <param name="seed"></param>
        /// <param name="outSize"></param>
        /// <returns></returns>
        public unsafe static bool DecryptCheckCompression(FileStream fs, Keyset keyset, uint seed, ulong outSize)
        {
            if (outSize > uint.MaxValue)
                return false;

            Span<byte> _tmpBuff = new byte[8];
            fs.Read(_tmpBuff);
            CryptoUtils.CryptBuffer(keyset, _tmpBuff, _tmpBuff, seed);

            // Inflated is always little
            uint zlibMagic = BinaryPrimitives.ReadUInt32LittleEndian(_tmpBuff);
            uint sizeComplement = BinaryPrimitives.ReadUInt32LittleEndian(_tmpBuff[4..]);

            if ((long)zlibMagic != Constants.ZLIB_MAGIC)
                return false;

            if ((uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;
            if (fs.Length <= headerSize) // Header size, if it's under, data is missing
                return false;

            return true;
        }

        public static uint Align(uint x, uint alignment)
        {
            uint mask = ~(alignment - 1);
            return (x + (alignment - 1)) & mask;
        }

        public static int MeasureBytesTakenByBits(double bitCount)
            => (int)Math.Round(bitCount / 8, MidpointRounding.AwayFromZero);
    }
}
