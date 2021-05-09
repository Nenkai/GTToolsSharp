using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;

namespace GTToolsSharp.PackedFileInstaller
{
    public class InstallEntry
    {
        public string Path { get; set; }
        public long FileSize { get; set; }
        public int BlockIndex { get; set; }
        public int PathNameOffset { get; set; }

        public static InstallEntry Read(ref SpanReader sr)
        {
            InstallEntry entry = new InstallEntry();

            entry.FileSize = sr.ReadInt64();
            entry.BlockIndex = sr.ReadInt32();
            entry.PathNameOffset = sr.ReadInt32();

            sr.Position = entry.PathNameOffset;
            entry.Path = sr.ReadString0();

            return entry;
        }

        public override string ToString()
            => $"{Path} (FileSize: {FileSize}, Block Index: {BlockIndex})";
    }
}
