using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp
{
	/// <summary>
	/// Polyphony Digital Patch File System
	/// </summary>
	class PDIPFSPathResolver
	{
		private const string Charset = "K59W4S6H7DOVJPERUQMT8BAIC2YLG30Z1FNX";

		private static string _default;
		public static string Default
		{
			get
			{
				_default ??= GetPathFromSeed(1);
				return _default;
			}
		}


		public static string GetPathFromSeed(uint seed)
		{
			string t = string.Empty;
			if (seed < 0x400)
			{
				t += 'K';
				uint s = XorShift(0x499, 10, seed);
				t += GetSubPathName(s, 2);
			}
			else if (seed - 0x400 < 0x8000)
			{
				t += '5';
				uint s = XorShift(0x8891, 15, seed - 0x400);
				t += GetSubPathName(s, 3);
			}
			else if (seed - 0x8400 < 0x100000)
			{
				t += '9';
				uint s = XorShift(0x111889, 20, seed - 0x8400);
				t += GetSubPathName(s, 4);
			}
			else if (seed - 0x108400 < 0x2000000)
			{
				t += 'W';
				uint s = XorShift(0x2242211, 25, seed - 0x108400);
				t += GetSubPathName(s, 5);
			}
			else if (seed + 0xfdf7c00 >= 0)
			{
				t += '4';
				uint s = XorShift(0x8889111, 32, seed + 0xFDEFC00);
				t += GetSubPathName(s, 6);
			}


			return t;
		}

		private static uint XorShift(uint x, int rounds, uint startingValue)
		{
			for (int i = 0; i < rounds; i++)
			{
				startingValue <<= 1;
				bool hasUpperBit = (1 << (int)rounds & startingValue) != 0;
				if (hasUpperBit)
					startingValue ^= x;
			}

			return startingValue;
		}


		private static string GetSubPathName(uint seed, int subpathLength)
		{
			string pathName = string.Empty;

			// Max 16 chars
			char[] chars = new char[subpathLength];

			if (subpathLength != 0)
			{
				for (int i = 0; i < subpathLength; i++)
                {
					char c = Charset[(int)(seed % 36)];
					seed /= 36;
					chars[i] = c;
				}

				int pos = subpathLength - 1;
				if (subpathLength % 2 == 0)
				{
					pathName += '/';
					pathName += chars[pos];
					pos--;
				}

				while (true)
                {
					pathName += chars[pos];
					if (pos == 0)
						break;
					pathName += '/';
					pathName += chars[pos - 1];
					pos -= 2;
				}

			}

			return pathName;
		}
	}
}
