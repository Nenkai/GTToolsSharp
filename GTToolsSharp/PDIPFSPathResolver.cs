using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp
{
	class PDIPFSPathResolver
	{
		private const string Charset = "K59W4S6H7DOVJPERUQMT8BAIC2YLG30Z1FNX";

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
			else if (unchecked(seed + 0xfdf7c00 > -1))
			{
				t += '4';
				uint s = XorShift(0x8889111, 32, seed + 0xFDEFC00);
				t += GetSubPathName(s, 6);
			}


			return t;
		}

		private static uint XorShift(uint x, int rounds, uint startingValue)
		{
			for (int i = rounds; i != 0; i--)
			{
				startingValue <<= 1;
				bool hasUpperBit = (1 << (int)rounds & startingValue) != 0;
				if (hasUpperBit)
					startingValue ^= x;
			}

			return startingValue;
		}


		private static string GetSubPathName(uint seed, int charShiftCount)
		{
			string pathName = "/";

			// Max 16 chars
			char[] chars = new char[charShiftCount];

			if (charShiftCount != 0)
			{
				for (int i = charShiftCount - 1; i > -1; i--)
				{
					char c = Charset[(int)(seed % 36)];
					seed /= 36;
					chars[i] = c;
				}
			}

			if ((charShiftCount & 1) != 0) // On Even, new folder
			{
				pathName += '/';
				pathName += chars[charShiftCount];
			}
			else
			{
				int charCountUntilFolder = 2;
				for (int i = 0; i < charShiftCount; i++)
				{
					if (charCountUntilFolder == 0)
					{
						charCountUntilFolder = 2;
						pathName += '/';
					}

					pathName += chars[i];
					charCountUntilFolder--;
				}
			}

			return pathName;
		}
	}
}
