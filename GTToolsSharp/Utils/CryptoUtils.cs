using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Numerics;
using System.Buffers;
using System.Buffers.Binary;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using ICSharpCode.SharpZipLib.Zip.Compression;

using PDTools.Hashing;
using PDTools.Compression;

using GTToolsSharp.Encryption;

namespace GTToolsSharp.Utils;

public static class CryptoUtils
{
    public static uint ShuffleBits(uint x)
    {
        uint crc = 0;
        for (uint i = 0; i < 4; ++i)
        {
            crc = (crc << 8) ^ CRC32.checksum_0x04C11DB7[(BitOperations.RotateLeft(x ^ crc, 10) & 0x3FC) >> 2];
            x <<= 8;
        }
        return ~crc;
    }

    /// <summary>
    /// Decrypts and decompress a file in a segmented stream and saves it to the provided path.
    /// </summary>
    /// <param name="keyset"></param>
    /// <param name="inputStream"></param>
    /// <param name="nodeSeed"></param>
    /// <param name="outPath"></param>
    public static bool DecryptAndInflateToFile(Keyset keyset, FileStream inputStream, uint nodeSeed, uint uncompressedSize, string outPath, bool closeStream = true)
    {
        bool result = false;
        // Our new file
        using (var newFileStream = new FileStream(outPath, FileMode.Create))
        {
            var decryptStream = new CryptoStream(inputStream, new VolumeCryptoTransform(keyset, nodeSeed), CryptoStreamMode.Read);
            uint magic = decryptStream.ReadUInt32();
            if (magic == PS2ZIP.PS2ZIP_MAGIC)
                result = PS2ZIP.TryInflate(decryptStream, newFileStream, skipMagic: true);
            else if (magic == PDIZIP.PDIZIP_MAGIC)
                result = PDIZIP.Inflate(decryptStream, newFileStream, skipMagic: true);
        }

        if (closeStream)
            inputStream.Dispose();

        return result;
    }

    /// <summary>
    /// Encrypts and compress a file stream to a provided output path.
    /// </summary>
    /// <param name="keyset"></param>
    /// <param name="inputStream"></param>
    /// <param name="nodeSeed"></param>
    /// <param name="outPath"></param>
    public static uint EncryptAndDeflateToFile(Keyset keyset, FileStream inputStream, uint nodeSeed, string outPath, bool closeStream = true)
    {
        const int bufferSize = 0x20000;
        // Prepare encryption
        uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
        uint[] keys = VolumeCrypto.PrepareKey(crc ^ nodeSeed, keyset.Key.Data);
        byte[] bitsTable = VolumeCrypto.GenerateBitsTable(keys);

        // Prepare compression and buffers
        var deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, true);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        byte[] deflateBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        using var outputStream = File.Open(outPath, FileMode.Create);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, PS2ZIP.PS2ZIP_MAGIC);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], -(int)inputStream.Length);
        VolumeCrypto.DecryptBuffer(header, header, header.Length, bitsTable, 0);
        outputStream.Write(header);

        long bytesLeft = inputStream.Length;
        while (bytesLeft > 0)
        {
            int read = inputStream.Read(buffer);
            deflater.SetInput(buffer, 0, read);
            if (bytesLeft <= bufferSize)
                deflater.Finish();
            else
                deflater.Flush(); // Important

            int nBytesDeflated = deflater.Deflate(deflateBuffer, 0, read);
            VolumeCrypto.DecryptBuffer(deflateBuffer, deflateBuffer, nBytesDeflated, bitsTable, (ulong)outputStream.Position);
            outputStream.Write(deflateBuffer, 0, nBytesDeflated);

            bytesLeft -= read;
        }

        if (closeStream)
            inputStream.Dispose();

        return (uint)outputStream.Length;
    }


    /// <summary>
    /// Encrypts a file stream to a provided output path.
    /// </summary>
    /// <param name="keyset"></param>
    /// <param name="inputStream"></param>
    /// <param name="nodeSeed"></param>
    /// <param name="outPath"></param>
    public static void EncryptToFile(Keyset keyset, FileStream inputStream, uint nodeSeed, string outPath, bool closeStream = true)
    {
        uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
        uint[] keys = VolumeCrypto.PrepareKey(crc ^ nodeSeed, keyset.Key.Data);
        byte[] bitsTable = VolumeCrypto.GenerateBitsTable(keys);

        // Our new file
        using var newFileStream = new FileStream(outPath, FileMode.Create);
        int bytesLeft = (int)inputStream.Length;
        const int bufSize = 0x20000;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufSize);

        ulong inputOffset = 0;
        while (bytesLeft > 0)
        {
            int read = inputStream.Read(buffer);

            VolumeCrypto.DecryptBuffer(buffer, buffer, read, bitsTable, inputOffset);
            newFileStream.Write(buffer, 0, read);

            bytesLeft -= read;

            inputOffset += (uint)read;
        }

        ArrayPool<byte>.Shared.Return(buffer);

        if (closeStream)
            inputStream.Dispose();
        newFileStream.Flush();
    }

    /// <summary>
    /// Checks if compression is valid for the stream.
    /// </summary>
    /// <param name="fs"></param>
    /// <param name="keyset"></param>
    /// <param name="seed"></param>
    /// <param name="outSize"></param>
    /// <returns></returns>
    public unsafe static bool DecryptCheckCompression(FileStream fs, Keyset keyset, uint seed, ulong outSize)
    {
        if (outSize > uint.MaxValue)
            return false;

        Span<byte> _tmpBuff = new byte[8];
        fs.ReadExactly(_tmpBuff);
        CryptoUtils.CryptBuffer(keyset, _tmpBuff, _tmpBuff, seed);

        // Inflated is always little
        uint zlibMagic = BinaryPrimitives.ReadUInt32LittleEndian(_tmpBuff);
        uint sizeComplement = BinaryPrimitives.ReadUInt32LittleEndian(_tmpBuff[4..]);

        if ((long)zlibMagic == PS2ZIP.PS2ZIP_MAGIC)
        {
            if ((uint)outSize + sizeComplement != 0)
                return false;

            const int headerSize = 8;
            if (fs.Length <= headerSize)
                return false;
        }
        else if (zlibMagic == PDIZIP.PDIZIP_MAGIC)
        {
            if ((uint)outSize != sizeComplement)
                return false;

            if (fs.Length <= 0x20)
                return false;
        }
          
        return true;
    }


    /// <summary>
    /// Crypt a buffer.
    /// </summary>
    /// <param name="keyset">Keys for decryption.</param>
    /// <param name="fs">Input stream.</param>
    /// <param name="seed">Seed for the entry.</param>
    /// <param name="outPath">File output name.</param>
    public static void CryptToFile(Keyset keyset, Stream fs, uint seed, string outPath, bool closeStream = true)
    {
        // Our new file
        using (var newFileStream = new FileStream(outPath, FileMode.Create))
        {
            var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);
            decryptStream.CopyTo(newFileStream);
        }

        if (closeStream)
            fs.Dispose();
    }

    public static void CryptToFile(Keyset keyset, Stream fs, uint seed, uint outSize, string outPath, bool closeStream = true)
    {
        // Our new file
        using (var newFileStream = new FileStream(outPath, FileMode.Create))
        {
            var decryptStream = new CryptoStream(fs, new VolumeCryptoTransform(keyset, seed), CryptoStreamMode.Read);

            int bytes = (int)outSize;
            int read;
            const int bufSize = 0x20000;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufSize);
            while (outSize > 0 && (read = decryptStream.Read(buffer, 0, Math.Min(buffer.Length, (int)bytes))) > 0)
            {
                newFileStream.Write(buffer, 0, read);
                bytes -= read;
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (closeStream)
            fs.Dispose();
    }

    /// <summary>
    /// Crypts a buffer.
    /// </summary>
    /// <param name="keyset">Keys for crypto.</param>
    /// <param name="inBuf">Incoming encrypted or decrypted buffer.</param>
    /// <param name="outBuf">Output.</param>
    /// <param name="seed">Entry seed for decryption.</param>
    public unsafe static void CryptBuffer(Keyset keyset, Span<byte> inBuf, Span<byte> outBuf, uint seed)
    {
        byte[] bitsTable;
        if (keyset != null)
        {
            uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
            uint[] keys = VolumeCrypto.PrepareKey(crc ^ seed, keyset.Key.Data);
            bitsTable = VolumeCrypto.GenerateBitsTable(keys);
        }
        else 
            bitsTable = new byte[0x2C0];

        VolumeCrypto.DecryptBuffer(inBuf, outBuf, inBuf.Length, bitsTable, 0);
    }

    /// <summary>
    /// Crypts a buffer (alternative version for certain files).
    /// </summary>
    /// <param name="keyset">Keys for crypto.</param>
    /// <param name="inBuf">Incoming encrypted or decrypted buffer.</param>
    /// <param name="outBuf">Output.</param>
    /// <param name="seed">Entry seed for decryption.</param>
    public unsafe static void CryptBufferAlternative(Keyset keyset, Span<byte> inBuf, Span<byte> outBuf)
    {
        byte[] bitsTable;
        if (keyset != null)
        {
            uint crc = ~CRC32.CRC32_0x04C11DB7(keyset.Magic, 0);
            uint[] keys = VolumeCrypto.PrepareKey(crc, keyset.Key.Data);
            bitsTable = VolumeCrypto.GenerateBitsTable(keys);
        }
        else
            bitsTable = new byte[0x2C0];

        VolumeCrypto.DecryptBuffer(inBuf, outBuf, inBuf.Length, bitsTable, 0);
    }
}
