using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.IO.Compression;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.Utils
{
    public class MiscUtils
    {
        public static byte[] ZlibCompress(byte[] input)
        {
            using var ms = new MemoryStream(input.Length);
            using var bs = new BinaryStream(ms);
            bs.WriteUInt32(Constants.ZLIB_MAGIC);
            bs.WriteUInt32(0u - (uint)input.Length);

            using var ds = new DeflateStream(ms, CompressionMode.Compress);
            ds.Write(input, 0, input.Length);
            ds.Flush();
            return ms.ToArray();
        }

        public unsafe static bool TryInflate(Span<byte> data, ulong outSize, out byte[] deflatedData)
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
            fixed (byte* pBuffer = &sr.Span.Slice(headerSize)[0])
            {
                using var ums = new UnmanagedMemoryStream(pBuffer, sr.Span.Length - headerSize);
                using var ds = new DeflateStream(ums, CompressionMode.Decompress);
                ds.Read(deflatedData, 0, (int)outSize);
            }

            return true;
        }
    }
}
