using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace GTToolsSharp.BinaryPatching
{
    class BsPatch
    {
		public static void Patch(Stream inputFileStream, Stream outputFileStream, string patchFile)
		{
			if (!File.Exists(patchFile))
				throw new FileNotFoundException("Patch file does not exist");

			long bzControlLength, bzDataLength, newSize;
			using (var patchFs = new FileStream(patchFile, FileMode.Open))
			{
				if (patchFs.Length < 0x20)
					throw new IOException($"Patch file stream length is under header size (0x20, size is {patchFs.Length}");

				using var patchBs = new BinaryStream(patchFs);
				if (patchBs.ReadString(8) != "BSDIFF40")
					throw new InvalidDataException("Patch file is not a Binary Patching file (BSDIFF40)");

				bzControlLength = patchBs.ReadInt64();
				bzDataLength = patchBs.ReadInt64();
				newSize = patchBs.ReadInt64();

				if (bzControlLength < 0 || bzDataLength < 0 || newSize < 0)
					throw new InvalidDataException("Patch file is corrupted (Invalid header sizes)");
			}

			const int buffSize = 0x100000;
			byte[] newData = ArrayPool<byte>.Shared.Rent(buffSize);
			byte[] oldData = ArrayPool<byte>.Shared.Rent(buffSize);

			byte[] patch = File.ReadAllBytes(patchFile);
			using var compressedControlStream = new MemoryStream(patch);
			compressedControlStream.Position = 0x20;
			using var controlStream = new BZip2Stream(compressedControlStream, CompressionMode.Decompress, false);

			using var compressedDiffStream = new MemoryStream(patch);
			compressedDiffStream.Position = 0x20 + bzControlLength;
			using var diffStream = new BZip2Stream(compressedDiffStream, CompressionMode.Decompress, false);

			using var compressedExtraStream = new MemoryStream(patch);
			compressedExtraStream.Position = 0x20 + bzControlLength + bzDataLength;
			using var extraStream = new BZip2Stream(compressedExtraStream, CompressionMode.Decompress, false);

			Span<long> control = stackalloc long[3];

			int oldPosition = 0, newPosition = 0;
			while (newPosition < newSize)
            {
				for (int i = 0; i < 3; i++)
					control[i] = controlStream.ReadInt64();

				if (newPosition + control[0] > newSize)
					throw new InvalidOperationException("Patch is corrupted, position + control[0] > newSize");

				inputFileStream.Position = oldPosition;

				int bytesToCopy = (int)control[0];
				while (bytesToCopy > 0)
				{
					int actualBytesToCopy = Math.Min(bytesToCopy, 32);
					diffStream.Read(newData, 0, actualBytesToCopy);

					int availableInputBytes = Math.Min(actualBytesToCopy, (int)(inputFileStream.Length - inputFileStream.Position));
					inputFileStream.Read(oldData, 0, availableInputBytes);

					for (int index = 0; index < availableInputBytes; index++)
						newData[index] += oldData[index];

					outputFileStream.Write(newData, 0, actualBytesToCopy);

					newPosition += actualBytesToCopy;
					oldPosition += actualBytesToCopy;
					bytesToCopy -= actualBytesToCopy;

					if (newPosition + control[1] > newSize)
						throw new InvalidOperationException("Patch is corrupted, newPosition + control[1] > newSize");
				}

				bytesToCopy = (int)control[1];
				while (bytesToCopy > 0)
				{
					int actualBytesToCopy = Math.Min(bytesToCopy, 32);

					extraStream.Read(newData, 0, actualBytesToCopy);
					outputFileStream.Write(newData, 0, actualBytesToCopy);

					newPosition += actualBytesToCopy;
					bytesToCopy -= actualBytesToCopy;
				}

				oldPosition = (int)(oldPosition + control[2]);
			}
		}
	}
}
