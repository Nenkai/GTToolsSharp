using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    public class FileInfoKey
    {
        public uint KeyOffset;

        public uint Flags { get; set; }
        public uint FileIndex { get; set; } = InvalidIndex;
        public uint CompressedSize { get; set; }
        public uint UncompressedSize { get; set; }
        public uint VolumeIndex { get; set; } = InvalidIndex;
        public uint SectorIndex { get; set; }

        public const uint InvalidIndex = uint.MaxValue;

        public FileInfoKey(uint nodeIndex)
        {
            FileIndex = nodeIndex;
        }

        public override string ToString()
            => $"Flags: {Flags} NodeIndex: {FileIndex} ({PDIPFSPathResolver.GetPathFromSeed(FileIndex)}), VolumeIndex: {VolumeIndex}, SectorIndex: {SectorIndex}, CompressedSize: {CompressedSize}, UncompSize: {UncompressedSize}";
    }
}
