using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Numerics;
using System.Buffers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Encryption;

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

        // Just used to seek and skip the compress header
        private static byte[] _tmpBuff = new byte[8];

        /// <summary>
        /// Decrypts and decompress a file in a stream and saves it to the provided path.
        /// </summary>
        /// <param name="keyset"></param>
        /// <param name="fs"></param>
        /// <param name="seed"></param>
        /// <param name="outPath"></param>
        public static void DecryptAndInflateToFile(Keyset keyset, FileStream fs, uint seed, string outPath, bool closeStream = true)
        {
            // Our new file
            using (var newFileStream = new FileStream(outPath, FileMode.Create))
            {
                var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);
                decryptStream.Read(_tmpBuff, 0, 8); // Compress Ignore header
                var ds = new DeflateStream(decryptStream, CompressionMode.Decompress);
                ds.CopyTo(newFileStream);
            }

            if (closeStream)
                fs.Dispose();
        }

        /// <summary>
        /// Decrypts and decompress a file in a segmented stream and saves it to the provided path.
        /// </summary>
        /// <param name="keyset"></param>
        /// <param name="fs"></param>
        /// <param name="seed"></param>
        /// <param name="outPath"></param>
        public static void DecryptAndInflateToFile(Keyset keyset, FileStream fs, uint seed, uint uncompressedSize, string outPath, bool closeStream = true)
        {
            // Our new file
            using (var newFileStream = new FileStream(outPath, FileMode.Create))
            {
                var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);
                decryptStream.Read(_tmpBuff, 0, 8); // Compress Ignore header

                var ds = new DeflateStream(decryptStream, CompressionMode.Decompress);

                int bytesLeft = (int)uncompressedSize;
                int read;
                const int bufSize = 81_920;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufSize);

                int currentPos = 0;
                while (bytesLeft > 0 && (read = ds.Read(buffer, 0, Math.Min(buffer.Length, bytesLeft))) > 0)
                {
                    newFileStream.Write(buffer, 0, read);

                    bytesLeft -= read;
                }

                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (closeStream)
                fs.Dispose();
        }

        /// <summary>
        /// Crypt a buffer.
        /// </summary>
        /// <param name="keyset">Keys for decryption.</param>
        /// <param name="fs">Input stream.</param>
        /// <param name="seed">Seed for the entry.</param>
        /// <param name="outPath">File output name.</param>
        public static void CryptToFile(Keyset keyset, Stream fs, uint seed, string outPath, bool closeStream = true)
        {
            // Our new file
            using (var newFileStream = new FileStream(outPath, FileMode.Create))
            {
                var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);
                decryptStream.CopyTo(newFileStream);
            }

            if (closeStream)
                fs.Dispose();
        }

        public static void CryptToFile(Keyset keyset, Stream fs, uint seed, uint outSize, string outPath, bool closeStream = true)
        {
            // Our new file
            using (var newFileStream = new FileStream(outPath, FileMode.Create))
            {
                var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);

                int bytes = (int)outSize;
                int read;
                const int bufSize = 0x20000;
                byte[] buffer = ArrayPool<byte>.Shared.Rent(bufSize);
                while (outSize > 0 && (read = decryptStream.Read(buffer, 0, Math.Min(buffer.Length, (int)bytes))) > 0)
                {
                    newFileStream.Write(buffer, 0, read);
                    bytes -= read;
                }

                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (closeStream)
                fs.Dispose();
        }

        /// <summary>
        /// Crypts a buffer.
        /// </summary>
        /// <param name="keyset">Keys for crypto.</param>
        /// <param name="inBuf">Incoming encrypted or decrypted buffer.</param>
        /// <param name="outBuf">Output.</param>
        /// <param name="seed">Entry seed for decryption.</param>
        public unsafe static void CryptBuffer(Keyset keyset, Span<byte> inBuf, Span<byte> outBuf, uint seed)
        {
            uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
            uint[] keys = VolumeCrypto.PrepareKey(crc ^ seed, keyset.Key.Data);
            var bitsTable = VolumeCrypto.GenerateBitsTable(keys);

            VolumeCrypto.DecryptBuffer(inBuf, outBuf, inBuf.Length, bitsTable, 0);
        }
    }
}
