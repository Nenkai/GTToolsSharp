using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;

using GTToolsSharp.Utils;
using PDTools.Crypto;

namespace GTToolsSharp.Encryption
{
    /// <summary>
    /// Represents a set of keys used to decrypt files.
    /// </summary>
    public class Keyset
    {
        public string GameCode { get; set; }
        public string Magic { get; set; }
        public Key Key { get; set; }
        public BufferDecryptManager DecryptManager { get; set; }

        public Keyset() { }

        public Keyset(string gameCode, string magic, Key key, BufferDecryptManager decryptManager)
        {
            DecryptManager = decryptManager;
            GameCode = gameCode;
            Magic = magic;
            Key = key;
        }

        public Keyset(string gameCode, string magic, Key key)
        {
            GameCode = gameCode;
            Magic = magic;
            Key = key;
        }

        public Keyset(string magic, Key key)
        {
            Magic = magic;
            Key = key;
        }

        /* \!/ The crypto algorithms following are kept for documentation purposes only
               They are not the original ones, they have been simplified by flatz
               For the real ones, look at VolumeCrypto.cs */

        /// <summary>
        /// Creates a key based on a seed.
        /// </summary>
        /// <param name="seed"></param>
        /// <returns></returns>
        public Key ComputeKey(uint seed)
        {
            uint c0 = (~CRC32.CRC32_0x04C11DB7(Magic, 0)) ^ seed;

            uint c1 = InvertedXorShift(c0, Key.Data[0]);
            uint c2 = InvertedXorShift(c1, Key.Data[1]);
            uint c3 = InvertedXorShift(c2, Key.Data[2]);
            uint c4 = InvertedXorShift(c3, Key.Data[3]);

            return new Key(c1 & ((1 << 17) - 1),
                c2 & ((1 << 19) - 1),
                c3 & ((1 << 23) - 1),
                c4 & ((1 << 29) - 1));
        }

        /// <summary>
        /// Crypts a buffer using the provided seed which turned into a key during decryption.
        /// Can be used for both encrypting and decrypting.
        /// </summary>
        /// <param name="data">Input buffer.</param>
        /// <param name="dest">Output buffer.</param>
        /// <param name="seed">Seed to use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CryptBytes(Span<byte> data, Span<byte> dest, uint seed)
        {
            Key key = ComputeKey(seed);

            for (int i = 0; i < data.Length; i++)
            {
                byte d = (byte)((((key.Data[0] ^ key.Data[1]) ^ data[i]) ^ (key.Data[2] ^ key.Data[3])) & (byte)0xFF);

                key.Data[0] = ((BitOperations.RotateLeft(key.Data[0], 9) & 0x1FE00u) | (key.Data[0] >> 8));
                key.Data[1] = ((BitOperations.RotateLeft(key.Data[1], 11) & 0x7F800u) | (key.Data[1] >> 8));
                key.Data[2] = ((BitOperations.RotateLeft(key.Data[2], 15) & 0x7F8000u) | (key.Data[2] >> 8));
                key.Data[3] = ((BitOperations.RotateLeft(key.Data[3], 21) & 0x1FE00000u) | (key.Data[3] >> 8));

                dest[i] = d;
            }
        }

        public void DecryptBlocks(Span<uint> data, Span<uint> dest)
        {
            uint prevBlock = BinaryPrimitives.ReverseEndianness(data[0]);
            dest[0] = BinaryPrimitives.ReverseEndianness(prevBlock);

            if (data.IsEmpty)
                return;

            for (int i = 1; i < data.Length; i++)
            {
                uint curBlock = BinaryPrimitives.ReverseEndianness(data[i]);
                uint outBlock = CryptBlock(curBlock, prevBlock);

                outBlock = BinaryPrimitives.ReverseEndianness(outBlock);

                prevBlock = curBlock;

                dest[i] = outBlock;
            }
        }

        public void EncryptBlocks(Span<uint> data, Span<uint> dest)
        {
            uint prevBlock = BinaryPrimitives.ReverseEndianness(data[0]);
            dest[0] = BinaryPrimitives.ReverseEndianness(prevBlock);

            if (data.IsEmpty)
                return;

            for (int i = 1; i < data.Length; i++)
            {
                uint curBlock = BinaryPrimitives.ReverseEndianness(data[i]);
                uint outBlock = CryptBlock(curBlock, prevBlock);
                dest[i] = BinaryPrimitives.ReverseEndianness(outBlock);

                prevBlock = outBlock;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint CryptBlock(uint x, uint y)
            => x ^ CryptoUtils.ShuffleBits(y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint InvertedXorShift(uint x, uint y)
            => ~XorShift(x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint XorShift(uint x, uint y)
        {
            uint result = x;
            uint count = 32; // sizeof(x) * CHAR_BIT /* 8 */;
            for (uint i = 0u; i < count; ++i)
            {
                bool hasUpperBit = (result & 0x80000000) != 0;
                result <<= 1;
                if (hasUpperBit)
                    result ^= y;
            }
            return result;
        }
    }
}
