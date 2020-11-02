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


		private static string GetSubPathName(uint seed, int charShiftCount)
		{
			string pathName = string.Empty;

			// Max 16 chars
			char[] chars = new char[charShiftCount + 1];

			if (charShiftCount != 0)
			{
				int index = 1;
				int charCnt = charShiftCount;
				do
				{
					char c = Charset[(int)(seed % 36)];
					seed /= 36;
					chars[index++] = c;
					charCnt--;
				} while (charCnt != 0);

				if ((charShiftCount & 1) == 0)
				{
					for (charCnt = charShiftCount; charCnt != 0; charCnt -= 2)
					{
						pathName += '/';
						pathName += chars[charCnt];
						pathName += chars[charCnt - 1];
					}
				}
				else
				{
					charCnt = charShiftCount - 1;
					for (pathName += chars[charCnt + 1], charShiftCount = charCnt; charCnt != 0;)
					{
						pathName += '/';
						charCnt = charShiftCount - 2;
						pathName += chars[charShiftCount];
						charShiftCount = charCnt;
						pathName += chars[charCnt + 1];
					}
				}
			}

			return pathName;
		}
	}
}
