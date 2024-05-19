using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using System.IO;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

using PDTools.Hashing;
using PDTools.Crypto;

namespace GTToolsSharp.Encryption
{
	// Original implementation of the vol decryption, reverse engineered from scratch as flatz's gttool oversimplified it to a point where it was completely different.
	public class VolumeCrypto
	{
		/// <summary>
		/// For traditional decryption
		/// </summary>
		/// <param name="inStream"></param>
		/// <param name="outStream"></param>
		/// <param name="seed"></param>
		/// <param name="fileSize"></param>
		/// <param name="offset"></param>
		public static void Decrypt(Keyset keyset, Stream inStream, Stream outStream, uint seed, ulong fileSize, ulong offset)
		{
			uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
			uint[] keys = PrepareKey(crc ^ seed, keyset.Key.Data);
			byte[] table = GenerateBitsTable(keys);

			byte[] buffer = ArrayPool<byte>.Shared.Rent(0x20000);

			while (fileSize > 0)
			{
				ulong bufferSize = fileSize;
				if (fileSize > 0x20000)
					bufferSize = 0x20000;

				inStream.Position = (long)offset;
				inStream.Read(buffer);

				DecryptBuffer(buffer, buffer, (int)bufferSize, table, offset);
				outStream.Write(buffer.AsSpan(0, (int)bufferSize));

				fileSize -= bufferSize;
				offset += bufferSize;
			}

			ArrayPool<byte>.Shared.Return(buffer);
		}

		/// <summary>
		/// For decrypting GT5P JP Demo files
		/// </summary>
		/// <param name="inStream"></param>
		/// <param name="outStream"></param>
		/// <param name="seed"></param>
		/// <param name="fileSize"></param>
		/// <param name="offset"></param>
		public static void DecryptOld(Keyset keyset, Stream inStream, Stream outStream, uint seed, ulong fileSize, ulong offset, Salsa20 salsa = default, bool skipCompressMagicForDecrypt = true)
		{
			uint[] keys = PrepareKeyOld(keyset, seed);
			byte[] table = GenerateBitsTable(keys);

			byte[] buffer = ArrayPool<byte>.Shared.Rent(0x20000);

			bool first = true;
			while (fileSize > 0)
			{
				ulong bufferSize = fileSize;
				if (fileSize > 0x20000)
					bufferSize = 0x20000;

				inStream.Position = (long)offset;
				inStream.Read(buffer);

				DecryptBuffer(buffer, buffer, (int)bufferSize, table, offset);
				if (salsa.Initted)
				{
					if (skipCompressMagicForDecrypt)
						salsa.DecryptOffset(buffer, first ? (int)bufferSize - 8 : (int)bufferSize, first ? (long)offset + 8 : (long)offset, first ? 8 : 0);
					else
						salsa.DecryptOffset(buffer, (int)bufferSize, (int)offset, 0);
				}

				outStream.Write(buffer.AsSpan(0, (int)bufferSize));

				fileSize -= bufferSize;
				offset += bufferSize;

				first = false;
			}

			ArrayPool<byte>.Shared.Return(buffer);
		}

		/// <summary>
		/// Decryption bits table generation
		/// </summary>
		public static byte[] GenerateBitsTable(uint[] keys)
		{
			byte[] data = new byte[(0x11 * 8) + (0x13 * 8) + (0x17 * 8) + (0x1d * 8)];
			GenerateBits(data, keys[0], 0x11);
			GenerateBits(data.AsSpan(0x88), keys[1], 0x13);
			GenerateBits(data.AsSpan(0x120), keys[2], 0x17);
			GenerateBits(data.AsSpan(0x1d8), keys[3], 0x1d);

			return data;
		}

		/// <summary>
		/// Key computing modern path
		/// </summary>
		/// <param name="seed"></param>
		/// <param name="keys"></param>
		/// <returns></returns>
		public static uint[] PrepareKey(uint seed, uint[] keys)
		{
			uint v1 = Keyset.InvertedXorShift(seed, keys[0]);
			uint v2 = Keyset.InvertedXorShift(v1, keys[1]);
			uint v3 = Keyset.InvertedXorShift(v2, keys[2]);
			uint v4 = Keyset.InvertedXorShift(v3, keys[3]);

			uint[] newKey = new uint[4];
			newKey[0] = v1 & 0x1FFFF;
			newKey[1] = v2 & 0x7FFFF;
			newKey[2] = v3 & 0x7FFFFF;
			newKey[3] = v4 & 0x1FFFFFFF;

			return newKey;
		}

		/// <summary>
		/// Key computing GT5P JP Demo
		/// </summary>
		/// <param name="seed"></param>
		/// <returns></returns>
		public static uint[] PrepareKeyOld(Keyset keyset, uint seed)
		{
			var keysetSeedCrc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);

			uint one = CRC32.CRC32_0x04C11DB7_UIntInverted(seed ^ keysetSeedCrc);
			uint two = CRC32.CRC32_0x04C11DB7_UIntInverted(one);
			uint three = CRC32.CRC32_0x04C11DB7_UIntInverted(two);
			uint four = CRC32.CRC32_0x04C11DB7_UIntInverted(three);

			uint[] keys = new uint[4];
			keys[0] = one & 0x1FFFF;
			keys[1] = two & 0x7ffff;
			keys[2] = three & 0x7fffff;
			keys[3] = four & 0x1fffffff;

			return keys;
		}

		/// <summary>
		/// Decryption bits table generation 2
		/// </summary>
		/// <param name="table"></param>
		/// <param name="keyPiece"></param>
		/// <param name="rotateAmount"></param>
		public static void GenerateBits(Span<byte> table, uint keyPiece, int rotateAmount)
		{
			if (rotateAmount > 0)
			{
				for (int i = 0; i < rotateAmount; i++)
				{
					table[i] = (byte)keyPiece;
					keyPiece = (keyPiece & 0xFF) << rotateAmount - 8 | keyPiece >> 0x08; // rotr
				}
			}

			// Copy key bits row once, then twice, then four times
			MemCpy(table.Slice(rotateAmount), table, rotateAmount);
			MemCpy(table.Slice(rotateAmount * 2), table, rotateAmount * 2);
			MemCpy(table.Slice(rotateAmount * 4), table, rotateAmount * 4);
		}

		/// <summary>
		/// Actual Data Decrypter Reverse Engineered.
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		/// <param name="size"></param>
		/// <param name="decryptTable"></param>
		/// <param name="offset"></param>
		public static void DecryptBuffer(Span<byte> input, Span<byte> output, int size, Span<byte> decryptTable, ulong offset)
		{
			// Bit offsets
			int bO1 = (int)(offset % 0x11);
			int bO2 = (int)(offset % 0x13);
			int bO3 = (int)(offset % 0x17);
			int bO4 = (int)(offset % 0x1d);

			// For the sake of C#, these will stay 0 - these should be the addresses of each buffer pointers
			// Depending on where they are certain paths will be taken
			int inPos = 0; int outPos = 0;

			// Are input buffer and output buffer on the same alignment?
			if (inPos % 8 == 0 && outPos % 8 == 0)
			{
				if ((inPos ^ inPos + size) / 8 == 0)
				{
					if (size == 0)
						return;
				}
				else
				{
					// Is our input buffer aligned?
					if (inPos % 8 != 0)
					{
						// Decrypt byte by byte until the input buffer is aligned to do 8 byte chunk decrypting
						int negCountToAligned = -(inPos % 8);
						int countToAligned = negCountToAligned + 8;
						if (countToAligned > 0)
						{
							var bits0_ = decryptTable.Slice(bO1);
							var bits1_ = decryptTable.Slice(bO2 + 0x88);
							var bits2_ = decryptTable.Slice(bO3 + 0x120);
							var bits3_ = decryptTable.Slice(bO4 + 0x1D8);

							for (int i = 0; i < countToAligned; i++)
							{
								output[i] = (byte)(bits1_[0] ^ bits2_[0] ^ input[i] ^ bits0_[0] ^ bits3_[0]);
								bits0_ = bits0_[1..]; bits1_ = bits1_[1..]; bits2_ = bits2_[1..]; bits3_ = bits3_[1..]; // Advance all by 1
							}
						}

						bO1 += countToAligned;
						if (bO1 >= 0x11)
							bO1 -= 0x11;

						bO2 += countToAligned;
						if (bO2 >= 0x13)
							bO2 -= 0x13;

						bO3 += countToAligned;
						if (bO3 >= 0x17)
							bO3 -= 0x17;

						bO4 += countToAligned;
						if (bO4 >= 0x1D)
							bO4 -= 0x1D;
					}

					// Decrypt in 8 bytes chunks
					int longCount = ((inPos + size) - inPos) / 8;

					// Bit offsets
					bO1 = unkBitTable[bO1];
					bO2 = unkBitTable[bO2 + 0x22];
					bO3 = unkBitTable[bO3 + 0x48];
					bO4 = unkBitTable[bO4 + 0x76];

					Span<ulong> outputLong = MemoryMarshal.Cast<byte, ulong>(output);
					Span<ulong> inputLong = MemoryMarshal.Cast<byte, ulong>(input);

					Span<ulong> decryptTableLong = MemoryMarshal.Cast<byte, ulong>(decryptTable);
					int bytesRead;
					if (longCount < 1)
						bytesRead = longCount * 8;
					else
					{
						// Decrypt by chunks of 0x08
						int iLong = 0;
						for (int i = 0; i < longCount; i++)
						{
							outputLong[iLong] = decryptTableLong[bO1] ^
												decryptTableLong[bO2 + (0x88 / 8)] ^
												decryptTableLong[bO3 + (0x120 / 8)] ^
												decryptTableLong[bO4 + (0x1D8 / 8)] ^
												inputLong.Slice(iLong)[0];

							bO1 = (bO1 + 1) & (((((bO1 + 1) ^ 0x11) >> 31) - ((((bO1 + 1) ^ 0x11) >> 31) ^ (bO1 + 1) ^ 0x11)) >> 31);
							bO2 = (bO2 + 1) & (((((bO2 + 1) ^ 0x13) >> 31) - ((((bO2 + 1) ^ 0x13) >> 31) ^ (bO2 + 1) ^ 0x13)) >> 31);
							bO3 = (bO3 + 1) & (((((bO3 + 1) ^ 0x17) >> 31) - ((((bO3 + 1) ^ 0x17) >> 31) ^ (bO3 + 1) ^ 0x17)) >> 31);
							bO4 = (bO4 + 1) & (((((bO4 + 1) ^ 0x1D) >> 31) - ((((bO4 + 1) ^ 0x1D) >> 31) ^ (bO4 + 1) ^ 0x1D)) >> 31);
							iLong++;
						}

						bytesRead = longCount * 8;
						input = input.Slice(bytesRead);
						output = output.Slice(bytesRead);
					}

					size -= bytesRead;
					bO1 = unkBitTable[bO1 + 0x11];
					bO2 = unkBitTable[bO2 + 0x35];
					bO3 = unkBitTable[bO3 + 0x5F];
					bO4 = unkBitTable[bO4 + 0x93];
				}

				if (size > 0)
				{
					// Decrypt remainder byte by byte
					var bits0 = decryptTable.Slice(bO1);
					var bits1 = decryptTable.Slice(bO2 + 0x88);
					var bits2 = decryptTable.Slice(bO3 + 0x120);
					var bits3 = decryptTable.Slice(bO4 + 0x1d8);

					for (int i = 0; i < size; i++)
					{
						output[i] = (byte)(bits1[0] ^ bits2[0] ^ input[i] ^ bits0[0] ^ bits3[0]);
						bits0 = bits0[1..]; bits1 = bits1[1..]; bits2 = bits2[1..]; bits3 = bits3[1..]; // Advance all by 1
					}
				}
			}
			else
			{
				// Slow path, both of them are not on the same alignment
				if (size == 0)
					return;

				var bits0 = decryptTable.Slice(bO1);
				var bits1 = decryptTable.Slice(bO2);
				var bits2 = decryptTable.Slice(bO3);
				var bits3 = decryptTable.Slice(bO4);

				for (int i = 0; i < size; i++)
				{
					while (true)
					{
						output[i] = (byte)(bits1[0] ^ bits2[0] ^ input[i] ^ bits0[0] ^ bits3[0]);

						int bUnk0 = bO1;
						bO1++;

						// Wrap around if needed
						if (bUnk0 == 0x10)
						{
							bO1 = 0;
							bits0 = decryptTable;
						}
						else
							bits0 = bits0[1..];

						int bUnk1 = bO1;
						bO1++;
						if (bUnk1 == 0x12)
						{
							bO1 = 0;
							bits1 = decryptTable.Slice(0x88);
						}
						else
							bits1 = bits1[1..];

						int bUnk2 = bO2;
						bO2++;
						if (bUnk2 == 0x16)
						{
							bO2 = 0;
							bits2 = decryptTable.Slice(0x120);
						}
						else
							bits2 = bits2[1..];

						int bUnk3 = bO3;
						bO3++;
						if (bUnk3 == 0x1c)
							break;

						i++;
						bits3 = bits3[1..];
						if (i > size)
							return;
					}

					bO3 = 0;
					bits3 = decryptTable.Slice(0x1D8);
				}
			}
		}

		static byte[] unkBitTable = new byte[]
		{
			0x00, 0x0F, 0x0D, 0x0B, 0x09, 0x07, 0x05, 0x03, 0x01, 0x10, 0x0E, 0x0C, 0x0A, 0x08, 0x06, 0x04,
			0x02, 0x00, 0x08, 0x10, 0x07, 0x0F, 0x06, 0x0E, 0x05, 0x0D, 0x04, 0x0C, 0x03, 0x0B, 0x02, 0x0A,
			0x01, 0x09, 0x00, 0x0C, 0x05, 0x11, 0x0A, 0x03, 0x0F, 0x08, 0x01, 0x0D, 0x06, 0x12, 0x0B, 0x04,
			0x10, 0x09, 0x02, 0x0E, 0x07, 0x00, 0x08, 0x10, 0x05, 0x0D, 0x02, 0x0A, 0x12, 0x07, 0x0F, 0x04,
			0x0C, 0x01, 0x09, 0x11, 0x06, 0x0E, 0x03, 0x0B, 0x00, 0x03, 0x06, 0x09, 0x0C, 0x0F, 0x12, 0x15,
			0x01, 0x04, 0x07, 0x0A, 0x0D, 0x10, 0x13, 0x16, 0x02, 0x05, 0x08, 0x0B, 0x0E, 0x11, 0x14, 0x00,
			0x08, 0x10, 0x01, 0x09, 0x11, 0x02, 0x0A, 0x12, 0x03, 0x0B, 0x13, 0x04, 0x0C, 0x14, 0x05, 0x0D,
			0x15, 0x06, 0x0E, 0x16, 0x07, 0x0F, 0x00, 0x0B, 0x16, 0x04, 0x0F, 0x1A, 0x08, 0x13, 0x01, 0x0C,
			0x17, 0x05, 0x10, 0x1B, 0x09, 0x14, 0x02, 0x0D, 0x18, 0x06, 0x11, 0x1C, 0x0A, 0x15, 0x03, 0x0E,
			0x19, 0x07, 0x12, 0x00, 0x08, 0x10, 0x18, 0x03, 0x0B, 0x13, 0x1B, 0x06, 0x0E, 0x16, 0x01, 0x09,
			0x11, 0x19, 0x04, 0x0C, 0x14, 0x1C, 0x07, 0x0F, 0x17, 0x02, 0x0A, 0x12, 0x1A, 0x05, 0x0D, 0x15
		};

		private static void MemCpy(Span<byte> output, Span<byte> input, int length)
		{
			input.Slice(0, length).CopyTo(output);
		}
	}
}
