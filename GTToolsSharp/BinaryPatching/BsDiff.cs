using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

using Syroot.BinaryData;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

using static GTToolsSharp.Utils.MiscExtensions;

namespace GTToolsSharp.BinaryPatching
{
	class BsDiff
	{
		static void Split(Span<int> I, Span<int> V, int start, int len, int h)
		{
			int i, j, k, x, tmp, jj, kk;

			if (len < 16)
			{
				for (k = start; k < start + len; k += j)
				{
					j = 1; x = V[I[k] + h];
					for (i = 1; k + i < start + len; i++)
					{
						if (V[I[k + i] + h] < x)
						{
							x = V[I[k + i] + h];
							j = 0;
						};
						if (V[I[k + i] + h] == x)
						{
							tmp = I[k + j]; I[k + j] = I[k + i]; I[k + i] = tmp;
							j++;
						};
					};
					for (i = 0; i < j; i++) V[I[k + i]] = k + j - 1;
					if (j == 1) I[k] = -1;
				};
				return;
			};

			x = V[I[start + len / 2] + h];
			jj = 0; kk = 0;
			for (i = start; i < start + len; i++)
			{
				if (V[I[i] + h] < x) jj++;
				if (V[I[i] + h] == x) kk++;
			};
			jj += start; kk += jj;

			i = start; j = 0; k = 0;
			while (i < jj)
			{
				if (V[I[i] + h] < x)
				{
					i++;
				}
				else if (V[I[i] + h] == x)
				{
					tmp = I[i]; I[i] = I[jj + j]; I[jj + j] = tmp;
					j++;
				}
				else
				{
					tmp = I[i]; I[i] = I[kk + k]; I[kk + k] = tmp;
					k++;
				};
			};

			while (jj + j < kk)
			{
				if (V[I[jj + j] + h] == x)
				{
					j++;
				}
				else
				{
					tmp = I[jj + j]; I[jj + j] = I[kk + k]; I[kk + k] = tmp;
					k++;
				};
			};

			if (jj > start) Split(I, V, start, jj - start, h);

			for (i = 0; i < kk - jj; i++) V[I[jj + i]] = kk - 1;
			if (jj == kk - 1) I[jj] = -1;

			if (start + len > kk) Split(I, V, kk, start + len - kk, h);
		}

		static void QSuffixSort(Span<int> I, Span<int> V, Span<byte> old, int oldsize)
		{
			int[] buckets = new int[256];
			int i, h, len;

			for (i = 0; i < 256; i++) buckets[i] = 0;
			for (i = 0; i < oldsize; i++) buckets[old[i]]++;
			for (i = 1; i < 256; i++) buckets[i] += buckets[i - 1];
			for (i = 255; i > 0; i--) buckets[i] = buckets[i - 1];
			buckets[0] = 0;

			for (i = 0; i < oldsize; i++) I[++buckets[old[i]]] = i;
			I[0] = oldsize;
			for (i = 0; i < oldsize; i++) V[i] = buckets[old[i]];
			V[oldsize] = 0;
			for (i = 1; i < 256; i++) if (buckets[i] == buckets[i - 1] + 1) I[buckets[i]] = -1;
			I[0] = -1;

			for (h = 1; I[0] != -(oldsize + 1); h += h)
			{
				len = 0;
				for (i = 0; i < oldsize + 1;)
				{
					if (I[i] < 0)
					{
						len -= I[i];
						i -= I[i];
					}
					else
					{
						if (len != 0) I[i - len] = -len;
						len = V[I[i]] + 1 - i;
						Split(I, V, i, len, h);
						i += len;
						len = 0;
					};
				};
				if (len != 0) I[i - len] = -len;
			};

			for (i = 0; i < oldsize + 1; i++) I[V[i]] = i;
		}

		static int MatchLength(Span<byte> old, int oldsize, Span<byte> newB, int newsize)
		{
			int i;

			for (i = 0; (i < oldsize) && (i < newsize); i++)
				if (old[i] != newB[i]) break;

			return i;
		}

		static int Search(Span<int> I, Span<byte> old, int oldsize,
					Span<byte> newB, int newsize, int st, int en, out int pos)
		{
			int x, y;

			if (en - st < 2) {
				x = MatchLength(old.Slice(I[st]), oldsize - I[st], newB, newsize);
				y = MatchLength(old.Slice(I[en]), oldsize - I[en], newB, newsize);

				if (x > y)
				{
					pos = I[st];
					return x;
				}
				else
				{
					pos = I[en];
					return y;
				}
			};

			x = st + (en - st) / 2;
			if (old.Slice(I[x]).MemoryCompare(newB, Math.Min(oldsize - I[x], newsize) ) < 0)
				return Search(I, old, oldsize, newB, newsize, x, en, out pos);
			else
				return Search(I, old, oldsize, newB, newsize, st, x, out pos);
		}

		public static void CreatePatch(string oldFile, string newFile, string outputPatchFilePath)
		{
			byte[] oldBuffer = File.ReadAllBytes(oldFile);
			int oldSize = oldBuffer.Length;

			int[] I = new int[oldSize / 4];
			int[] V = new int[oldSize / 4];

			QSuffixSort(I, V, oldBuffer, oldBuffer.Length);

			byte[] newBuffer = File.ReadAllBytes(newFile);
			int newSize = newBuffer.Length;
			byte[] db = new byte[newSize];
			byte[] eb = new byte[newSize];

			using var patchFileStream = new FileStream(outputPatchFilePath, FileMode.Open);
			using var bs = new BinaryStream(patchFileStream);
			bs.WriteString("BSDIFF40", StringCoding.Raw);

			// Skip 16 bytes, write at the end

			bs.Position = 24;
			bs.WriteUInt64((ulong)newBuffer.Length);

			using var bzs = new BZip2Stream(patchFileStream, CompressionMode.Compress, false);
			int dblen = 0, eblen = 0;
			int scan = 0, pos = 0, len = 0;
			int lastscan = 0, lastpos = 0, lastoffset = 0;

			while (scan < newBuffer.Length)
			{
				int oldscore = 0;

				int scsc;
				for (scsc = scan += len; scan < newSize; scan++)
				{
					len = Search(I, oldBuffer, oldSize, newBuffer.AsSpan(scan), newSize - scan, 0, oldSize, out pos);

					for (; scsc < scan + len; scsc++)
						if ((scsc + lastoffset < oldSize) &&
							(oldBuffer[scsc + lastoffset] == newBuffer[scsc]))
							oldscore++;

					if (((len == oldscore) && (len != 0)) || (len > oldscore + 8)) 
						break;

					if ((scan + lastoffset < oldSize) && (oldBuffer[scan + lastoffset] == newBuffer[scan]))
						oldscore--;
				}

				if ((len != oldscore) || (scan == newSize))
				{
					int s = 0, Sf = 0, lenf = 0, i = 0;
					for (i = 0; (lastscan + i < scan) && (lastpos + i < oldSize);)
					{
						if (oldBuffer[lastpos + i] == newBuffer[lastscan + i]) 
							s++;

						i++;

						if (s * 2 - i > Sf * 2 - lenf) 
						{ 
							Sf = s; 
							lenf = i;
						}
					}

					int lenb = 0;
					if (scan < newSize)
					{
						s = 0; int Sb = 0;
						for (i = 1; (scan >= lastscan + i) && (pos >= i); i++)
						{
							if (oldBuffer[pos - i] == newBuffer[scan - i]) 
								s++;

							if (s * 2 - i > Sb * 2 - lenb) 
							{ 
								Sb = s; 
								lenb = i; 
							}
						}
					}

					if (lastscan + lenf > scan - lenb)
					{
						int overlap = (lastscan + lenf) - (scan - lenb);
						s = 0; int Ss = 0; int lens = 0;
						for (i = 0; i < overlap; i++)
						{
							if (newBuffer[lastscan + lenf - overlap + i] == oldBuffer[lastpos + lenf - overlap + i]) 
								s++;
							if (newBuffer[scan - lenb + i] == oldBuffer[pos - lenb + i]) 
								s--;

							if (s > Ss) 
							{ 
								Ss = s; 
								lens = i + 1; 
							}
						}

						lenf += lens - overlap;
						lenb -= lens;
					}

					for (i = 0; i < lenf; i++)
						db[dblen + i] = (byte)(newBuffer[lastscan + i] - oldBuffer[lastpos + i]);
					for (i = 0; i < (scan - lenb) - (lastscan + lenf); i++)
						eb[eblen + i] = newBuffer[lastscan + lenf + i];

					dblen += lenf;
					eblen += (scan - lenb) - (lastscan + lenf);

					bzs.Write(BitConverter.GetBytes(lenf), 0, 8);
					bzs.Write(BitConverter.GetBytes((long)(scan - lenb - (lastscan + lenf))), 0, 8);
					bzs.Write(BitConverter.GetBytes((long)(pos - lenb - (lastpos + lenf))), 0, 8);

					lastscan = scan - lenb;
					lastpos = pos - lenb;
					lastoffset = pos - scan;
				}
			}

			bzs.Flush();

			long endPos = patchFileStream.Position;
			bzs.Write(db, 0, dblen);
			bzs.Flush();

			long pos2 = bs.Position;
			patchFileStream.WriteInt64(patchFileStream.Position - endPos);
			bzs.Write(eb, 0, eblen);
			bzs.Flush();

			bs.Position = 8;
			bs.WriteInt64(endPos - 32); /* Header Size */
			bs.WriteInt64(pos2 - endPos);
		}
	}
}
