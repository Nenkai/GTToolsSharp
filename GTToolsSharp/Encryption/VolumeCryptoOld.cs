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

using GTToolsSharp.Utils;

namespace GTToolsSharp.Encryption
{
	// Original implementation of the vol decryption, reverse engineered from scratch as flatz's gttool oversimplified it to a point where it was completely different.
    public class VolumeCryptoOld
    {
		private Keyset _keys;

		public VolumeCryptoOld(Keyset keyset)
        {
			_keys = keyset;
        }

		/// <summary>
		/// For traditional decryption
		/// </summary>
		/// <param name="inStream"></param>
		/// <param name="outStream"></param>
		/// <param name="seed"></param>
		/// <param name="fileSize"></param>
		/// <param name="offset"></param>
		public void Decrypt(Stream inStream, Stream outStream, uint seed, ulong fileSize, ulong offset)
        {
			uint crc = ~CRC32.CRC32_0x04C11DB7(_keys.Magic, 0);
			uint[] keys = PrepareKey(crc ^ seed, _keys.Key.Data);
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
		public void DecryptOld(Stream inStream, Stream outStream, uint seed, ulong fileSize, ulong offset)
		{
			uint[] keys = PrepareKeyOld(seed);
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
		/// Decryption bits table generation
		/// </summary>
		private byte[] GenerateBitsTable(uint[] keys)
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
		private uint[] PrepareKey(uint seed, uint[] keys)
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
		private uint[] PrepareKeyOld(uint seed)
		{
			uint one = CRC32.CRC32UInt(seed ^ 0xADD1F79B);
			uint two = CRC32.CRC32UInt(one);
			uint three = CRC32.CRC32UInt(two);
			uint four = CRC32.CRC32UInt(three);

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
		private void GenerateBits(Span<byte> table, uint keyPiece, int rotateAmount)
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
		/// Internal decrypter
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		/// <param name="size"></param>
		/// <param name="decryptTable"></param>
		/// <param name="offset"></param>
		private void DecryptBuffer(Span<byte> input, Span<byte> output, int size, Span<byte> decryptTable, ulong offset)
		{
			ulong u1 = (ulong)(BigInteger.Multiply(new BigInteger(offset), new BigInteger(0x642c8590b21642c9)) >> 0x40);
			ulong u4 = (ulong)(BigInteger.Multiply(new BigInteger(offset), new BigInteger(0x1A7B9611A7B9611B)) >> 0x40);

			int bitOffset0 = (int)(offset + (BigInteger.Multiply(new BigInteger(offset), new BigInteger(0xF0F0F0F0F0F0F0F1)) >> 0x44) * -0x11);
			int bitOffset1 = (int)(offset + (BigInteger.Multiply(new BigInteger(offset), new BigInteger(0xD79435E50D79435F)) >> 0x44) * -0x13);
			int bitOffset2 = (int)((uint)offset + (uint)(u1 + (offset - u1 >> 0x1) >> 0x4) * -0x17);
			int bitOffset3 = (int)((uint)offset + (uint)(u4 + (offset - u4 >> 0x1) >> 0x4) * -0x1d);

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
							var bits0_ = decryptTable.Slice(bitOffset0);
							var bits1_ = decryptTable.Slice(bitOffset1 + 0x88);
							var bits2_ = decryptTable.Slice(bitOffset2 + 0x120);
							var bits3_ = decryptTable.Slice(bitOffset3 + 0x1D8);

							for (int i = 0; i < countToAligned; i++)
							{
								output[i] = (byte)(bits1_[0] ^ bits2_[0] ^ input[i] ^ bits0_[0] ^ bits3_[0]);
								bits0_ = bits0_[1..]; bits1_ = bits1_[1..]; bits2_ = bits2_[1..]; bits3_ = bits3_[1..]; // Advance all by 1
							}
						}

						bitOffset0 += countToAligned;
						if (bitOffset0 >= 0x11)
							bitOffset0 -= 0x11;

						bitOffset1 += countToAligned;
						if (bitOffset1 >= 0x13)
							bitOffset1 -= 0x13;

						bitOffset2 += countToAligned;
						if (bitOffset2 >= 0x17)
							bitOffset2 -= 0x17;

						bitOffset3 += countToAligned;
						if (bitOffset3 >= 0x1D)
							bitOffset3 -= 0x1D;
					}

					// Decrypt in 8 bytes chunks
					int longCount = ((inPos + size) - inPos) / 8;

					uint unkA = unkBitTable[bitOffset0];
					uint unkB = unkBitTable[bitOffset1 + 0x22];
					uint unkC = unkBitTable[bitOffset2 + 0x48];
					uint unkD = unkBitTable[bitOffset3 + 0x76];

					Span<ulong> outputLong = MemoryMarshal.Cast<byte, ulong>(output);
					Span<ulong> inputLong = MemoryMarshal.Cast<byte, ulong>(input);

					int bytesRead;
					if (longCount < 1)
						bytesRead = longCount * 8;
					else
					{
						// Decrypt by chunks of 0x08
						int iLong = 0;
						for (int i = 0; i < longCount; i++)
						{
							uint unkA2 = unkA * 8;
							uint unkB2 = unkB * 8;
							uint unkC2 = unkC * 8;
							uint unkD2 = unkD * 8;

							uint unkA3 = unkA + 1 ^ 0x11;
							uint unkA4 = unkA3 >> 0x1F;

							uint unkB3 = unkB + 1 ^ 0x13;
							uint unkB4 = unkB3 >> 0x1F;

							uint unkC3 = unkC + 1 ^ 0x17;
							uint unkC4 = unkC3 >> 0x1F;

							uint unkD3 = unkD + 1 ^ 0x1D;
							uint unkD4 = unkD3 >> 0x1F;

							unkA = (uint)(unkA + 1 & (int)(unkA4 - (unkA4 ^ unkA3)) >> 0x1F);
							unkB = (uint)(unkB + 1 & (int)(unkB4 - (unkB4 ^ unkB3)) >> 0x1F);
							unkC = (uint)(unkC + 1 & (int)(unkC4 - (unkC4 ^ unkC3)) >> 0x1F);
							unkD = (uint)(unkD + 1 & (int)(unkD4 - (unkD4 ^ unkD3)) >> 0x1F);

							outputLong[iLong] = BinaryPrimitives.ReadUInt64LittleEndian(decryptTable.Slice((int)unkB2 + 0x88)) ^ 
												BinaryPrimitives.ReadUInt64LittleEndian(decryptTable.Slice((int)unkC2 + 0x120)) ^ 
												BinaryPrimitives.ReadUInt64LittleEndian(decryptTable.Slice((int)unkA2)) ^ 
												inputLong.Slice(iLong)[0] ^ 
												BinaryPrimitives.ReadUInt64LittleEndian(decryptTable.Slice((int)unkD2 + 0x1D8));
							iLong++;
						}

						bytesRead = longCount * 8;
						input = input.Slice(bytesRead);
						output = output.Slice(bytesRead);
					}

					size -= bytesRead;
					bitOffset0 = unkBitTable[unkA + 0x11];
					bitOffset1 = unkBitTable[unkB + 0x35];
					bitOffset2 = unkBitTable[unkC + 0x5F];
					bitOffset3 = unkBitTable[unkD + 0x93];

					// Done reading, no more bytes
					if (size == 0)
						return;
				}

				// Decrypt remainder byte by byte
				var bits0 = decryptTable.Slice(bitOffset0);
				var bits1 = decryptTable.Slice(bitOffset1 + 0x88);
				var bits2 = decryptTable.Slice(bitOffset2 + 0x120);
				var bits3 = decryptTable.Slice(bitOffset3 + 0x1d8);

				for (int i = 0; i < size; i++)
				{
					output[i] = (byte)(bits1[0] ^ bits2[0] ^ input[i] ^ bits0[0] ^ bits3[0]);
					bits0 = bits0[1..]; bits1 = bits1[1..]; bits2 = bits2[1..]; bits3 = bits3[1..]; // Advance all by 1
				}
			}
			else
			{
				// Slow path, both of them are not on the same alignment
				if (size == 0)
					return;

				var bits0 = decryptTable.Slice(bitOffset0);
				var bits1 = decryptTable.Slice(bitOffset1);
				var bits2 = decryptTable.Slice(bitOffset2);
				var bits3 = decryptTable.Slice(bitOffset3);

				for (int i = 0; i < size; i++)
				{
					while (true)
					{
						output[i] = (byte)(bits1[0] ^ bits2[0] ^ input[i] ^ bits0[0] ^ bits3[0]);

						int bUnk0 = bitOffset0;
						bitOffset0++;

						// Wrap around if needed
						if (bUnk0 == 0x10)
						{
							bitOffset0 = 0;
							bits0 = decryptTable;
						}
						else
							bits0 = bits0[1..];

						int bUnk1 = bitOffset1;
						bitOffset1++;
						if (bUnk1 == 0x12)
						{
							bitOffset1 = 0;
							bits1 = decryptTable.Slice(0x88);
						}
						else
							bits1 = bits1[1..];

						int bUnk2 = bitOffset2;
						bitOffset2++;
						if (bUnk2 == 0x16)
						{
							bitOffset2 = 0;
							bits2 = decryptTable.Slice(0x120);
						}
						else
							bits2 = bits2[1..];

						int bUnk3 = bitOffset3;
						bitOffset3++;
						if (bUnk3 == 0x1c)
							break;

						i++;
						bits3 = bits3[1..];
						if (i > size)
							return;
					}

					bitOffset3 = 0;
					bits3 = decryptTable.Slice(0x1D8);
				}
			}
		}

		byte[] unkBitTable = new byte[]
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

		private void MemCpy(Span<byte> output, Span<byte> input, int length)
		{
			input.Slice(0, length).CopyTo(output);
		}
	}
}
