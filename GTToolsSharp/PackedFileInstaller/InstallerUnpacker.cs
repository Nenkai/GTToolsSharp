using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using GTToolsSharp.Encryption;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.PackedFileInstaller
{
    /// <summary>
    /// For unpacking TV.DAT.
    /// </summary>
    public class InstallerUnpacker
    {
        public const string Magic = "EZPF"; // EZ Packed File??
        public const int DefaultSeed = 10;
        public const int BlocKSize = 0x800;

        public List<InstallEntry> Entries = new List<InstallEntry>();

        public Keyset _keys;
        public string _file;

        public InstallerUnpacker(Keyset keys, string file)
        {
            _keys = keys;
            _file = file;
        }

        public static InstallerUnpacker Load(Keyset keyset, string file)
        {
            Span<byte> header = stackalloc byte[0x10];
            using (var fs = new FileStream(file, FileMode.Open))
                fs.Read(header);

            keyset.CryptData(header, DefaultSeed);

            SpanReader sr = new SpanReader(header, Endian.Big);
            if (sr.ReadStringRaw(4) != Magic)
                return null;

            uint tocSize = sr.ReadUInt32();
            ulong fileCount = sr.ReadUInt64();

            InstallerUnpacker unpacker = new InstallerUnpacker(keyset, file);
            unpacker.LoadToC(tocSize, (uint)fileCount);

            return unpacker;
        }

        private void LoadToC(uint tocSize, uint fileCount)
        {
            using var fs = new FileStream(_file, FileMode.Open);
            byte[] tocBuffer = new byte[tocSize];
            fs.Read(tocBuffer);

            _keys.CryptData(tocBuffer, DefaultSeed);

            SpanReader sr = new SpanReader(tocBuffer, Endian.Big);
            sr.Position = 0x10;
            for (int i = 0; i < fileCount; i++)
            {
                sr.Position = 0x10 + (i * 0x10);
                var entry = InstallEntry.Read(ref sr);
                Entries.Add(entry);
            }
        }

        public void Unpack()
        {
            // Decryption to figure out
        }
    }
}
