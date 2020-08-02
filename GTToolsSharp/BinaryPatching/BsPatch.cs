using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace GTToolsSharp.BinaryPatching
{
    class BsPatch
    {
        public static void Patch(byte[] oldFile, byte[] newFile, string patchFile)
        {
			if (!File.Exists(patchFile))
				throw new FileNotFoundException("Patch file does not exist");

			long bzControlLength, bzDataLength, newSize;
			using (var patchFs = new FileStream(patchFile, FileMode.Open)) 
			{
				if (patchFs.Length < 32)
					throw new IOException($"Patch file stream length is under header size (32, size is {patchFs.Length}");

				using var patchBs = new BinaryStream(patchFs);
				if (patchBs.ReadString(8) != "BSDIFF40")
					throw new InvalidDataException("Patch file is not a Binary Patching file (BSDIFF40)");

				bzControlLength = patchBs.ReadInt64();
				bzDataLength = patchBs.ReadInt64();
				newSize = patchBs.ReadInt64();

				if (bzControlLength < 0 || bzDataLength < 0 || newSize < 0)
					throw new InvalidDataException("Patch file is corrupted (Invalid header sizes)");
			}

			var inputStream = new MemoryStream(oldFile);
			var newFileStream = new MemoryStream(newFile);

			int buffSize = 1048576;
			byte[] newData = new byte[buffSize];
			byte[] oldData = new byte[buffSize];


			using var compressedControlStream = new FileStream(patchFile, FileMode.Open);
			compressedControlStream.Position = 32;
			using var controlStream = new BZip2Stream(compressedControlStream, CompressionMode.Decompress, false);

			using var compressedDiffStream = new FileStream(patchFile, FileMode.Open);
			compressedDiffStream.Position = 32 + bzControlLength;
			using var diffStream = new BZip2Stream(compressedDiffStream, CompressionMode.Decompress, false);

			using var compressedExtraStream = new FileStream(patchFile, FileMode.Open);
			compressedExtraStream.Position = 32 + bzControlLength + bzDataLength;
			using var extraStream = new BZip2Stream(compressedExtraStream, CompressionMode.Decompress, false);

			long[] control = new long[3];
			byte[] buffer = new byte[8];

			int oldPosition = 0, newPosition = 0;
			while (newPosition < newSize)
            {
				for (int i = 0; i < 3; i++)
					control[i] = controlStream.ReadInt64();

				if (newPosition + control[0] > newSize)
					throw new InvalidOperationException("Patch is corrupted, position + control[0] > newSize");

				inputStream.Position = oldPosition;

				int bytesToCopy = (int)control[0];
				while (bytesToCopy > 0)
				{
					int actualBytesToCopy = Math.Min(bytesToCopy, 32);
					diffStream.Read(newData, 0, actualBytesToCopy);

					int availableInputBytes = Math.Min(actualBytesToCopy, (int)(inputStream.Length - inputStream.Position));
					inputStream.Read(oldData, 0, availableInputBytes);

					for (int index = 0; index < availableInputBytes; index++)
						newData[index] += oldData[index];

					newFileStream.Write(newData, 0, actualBytesToCopy);

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
					newFileStream.Write(newData, 0, actualBytesToCopy);

					newPosition += actualBytesToCopy;
					bytesToCopy -= actualBytesToCopy;
				}

				oldPosition = (int)(oldPosition + control[2]);
			}
		}
	}
}
