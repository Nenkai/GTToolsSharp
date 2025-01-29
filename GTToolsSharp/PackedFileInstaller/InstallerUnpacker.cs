using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;

using GTToolsSharp.Encryption;
using GTToolsSharp.Utils;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.PackedFileInstaller;

/// <summary>
/// For unpacking TV.DAT.
/// </summary>
public class InstallerUnpacker
{
    public const string Magic = "EZPF"; // EZ Packed File??
    public const int DefaultSeed = 10;
    public const int BlockSize = 0x800;

    public List<InstallEntry> Entries = [];

    public Keyset _keys;
    public string _file;

    public InstallerUnpacker(Keyset keys, string file)
    {
        _keys = keys;
        _file = file;
    }

    public static InstallerUnpacker Load(Keyset keyset, string file, bool saveHeaderToc = false)
    {
        if (keyset.Key.Data is null || keyset.Key.Data.Length < 4)
            return null;

        Span<byte> header = stackalloc byte[0x10];
        using (var fs = new FileStream(file, FileMode.Open))
            fs.ReadExactly(header);

        CryptoUtils.CryptBuffer(keyset, header, header, DefaultSeed);

        SpanReader sr = new SpanReader(header, Endian.Big);
        if (sr.ReadStringRaw(4) != Magic)
            return null;

        Program.Log($"[:] Successfully decrypted installer header with keyset: {keyset.GameCode} - {keyset.Magic}");

        uint tocSize = sr.ReadUInt32();
        ulong fileCount = sr.ReadUInt64();

        Program.Log($"[:] TOC Size: {tocSize}");
        Program.Log($"[:] File Count: {fileCount}..");

        InstallerUnpacker unpacker = new InstallerUnpacker(keyset, file);
        unpacker.LoadToC(tocSize, (uint)fileCount);

        return unpacker;
    }

    private void LoadToC(uint tocSize, uint fileCount, bool saveHeaderToc = false)
    {
        using var fs = new FileStream(_file, FileMode.Open);
        byte[] tocBuffer = new byte[tocSize];
        fs.ReadExactly(tocBuffer);

        CryptoUtils.CryptBuffer(_keys, tocBuffer, tocBuffer, DefaultSeed);
        if (saveHeaderToc)
            File.WriteAllBytes("InstallerHeaderTOC.bin", tocBuffer);

        SpanReader sr = new SpanReader(tocBuffer, Endian.Big);
        sr.Position = 0x10;
        for (int i = 0; i < fileCount; i++)
        {
            sr.Position = 0x10 + (i * 0x10);
            var entry = InstallEntry.Read(ref sr);
            Entries.Add(entry);
        }
    }

    public void Unpack(string outPath)
    {
        using var fs = new FileStream(_file, FileMode.Open);

        for (int i = 0; i < Entries.Count; i++)
        {
            InstallEntry entry = Entries[i];
            string outFileName = Path.Combine(outPath, entry.Path);

            Directory.CreateDirectory(Path.GetDirectoryName(outFileName));

            Program.Log($"[:] Unpacking: {entry.Path}.. ({i + 1}/{Entries.Count})");

            // Decrypt it from offset
            using (FileStream tempOutDecrypt = new FileStream(outFileName + ".in", FileMode.Create))
                VolumeCrypto.Decrypt(_keys, fs, tempOutDecrypt, DefaultSeed, (ulong)entry.FileSize, (ulong)entry.BlockIndex * BlockSize);

            // Decompress if needed
            bool isCompressed = true;
            using (FileStream inStream = new FileStream(outFileName + ".in", FileMode.Open))
            {
                if (inStream.ReadUInt32() == 0xFFF7EEC5)
                {
                    int size = -inStream.ReadInt32();
                    inStream.Position = 8;
                    using var outDecompressStream = new FileStream(outFileName, FileMode.Create);

                    using var ds = new DeflateStream(inStream, CompressionMode.Decompress);
                    ds.CopyTo(outDecompressStream);

                }
                else
                {
                    isCompressed = false;
                }
            }

            if (isCompressed)
                File.Delete(outFileName + ".in");
            else
                File.Move(outFileName + ".in", outFileName);
        }

        Program.Log($"[:] Install unpack finished.");
    }
}

