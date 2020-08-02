using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GTToolsSharp.Utils
{
    public static class MiscExtensions
    {
		public static int MemoryCompare(this Span<byte> src, ReadOnlySpan<byte> input, int size)
		{
			if (size < 0)
				throw new ArgumentException("Size must not be below 0.", nameof(size));

			if (size >= src.Length)
				throw new ArgumentException("Size must not above source length.", nameof(size));

			if (size > src.Length)
				throw new ArgumentException("Size must not above input length.", nameof(size));

			for (int i = 0; i < size; i++)
			{
				if (src[i] != input[i])
                {
					if (src[i] < input[i])
						return -1;
					else
						return 1;

				}
			}

			return 0;
		}
	}
}
