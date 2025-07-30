using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

using PDTools.Hashing;

namespace GTToolsSharp.Encryption;

public class GT5POldCrypto
{
	public static void DecryptPass(uint seed, Span<byte> input, Span<byte> output, int size)
	{
		uint one = CRC32.CRC32_0x04C11DB7_UIntInverted(seed);
		uint two = CRC32.CRC32_0x04C11DB7_UIntInverted(one);
		uint three = CRC32.CRC32_0x04C11DB7_UIntInverted(two);
		uint four = CRC32.CRC32_0x04C11DB7_UIntInverted(three);

		if (size == 0)
			return;

		// CryptDataChunk
		one &= 0x1ffff;
		two &= 0x7ffff;
		three &= 0x7fffff;
		four &= 0x1fffffff;

		for (int i = 0; i < size; i++)
		{
			output[i] = (byte)((byte)three ^ (byte)four ^ (byte)one ^ (byte)two ^ input[i]);
			one = (one & 0xff) << 0x9 | one >> 0x08;
			two = (two & 0xff) << 0xb | two >> 0x08;
			three = (three & 0xff) << 0xf | three >> 0x08;
			four = (four & 0xff) << 0x15 | four >> 0x08;
		}
	}

	public static void DecryptHeaderSpecific(Span<byte> output, Span<byte> input)
	{
		Span<uint> asUInts = MemoryMarshal.Cast<byte, uint>(output);
		for (int i = 0; i < 5; i++)
		{
			int cur = i * sizeof(uint);
			asUInts[i] = (uint)input[cur + 3] | (uint)input[cur + 2] << 0x8 | (uint)input[cur + 1] << 0x10 | (uint)input[cur] << 0x18;
		}

		DoHeaderXorPass(asUInts);
		for (int i = 0; i < 5; i++)
			asUInts[i] = BinaryPrimitives.ReverseEndianness(asUInts[i]);
	}

	public static void DoHeaderXorPass(Span<uint> input)
	{
		uint ret = CRCXor(input[1..], input[0]);
		ret = CRCXor(input[2..], ret);
		ret = CRCXor(input[3..], ret);
		ret = CRCXor(input[4..], ret);
	}

	public static uint CRCXor(Span<uint> input, uint input2)
	{
		uint crc = CRC32.CRC32_0x04C11DB7_UIntInverted(input2);

		uint old = input[0];
		input[0] ^= crc;
		return old;
	}
}
