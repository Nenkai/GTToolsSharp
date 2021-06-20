using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;
using System.IO;
using Syroot.BinaryData.Memory;
using Syroot.BinaryData;

using GTToolsSharp.Utils;
using static GTToolsSharp.Utils.CryptoUtils;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    /// <summary>
    /// Represents a key that holds relational btree data and file sizes.
    /// </summary>
    public class FileInfoKey : IBTreeKey<FileInfoKey>
    {

        public uint KeyOffset;

        public FileInfoFlags Flags { get; set; }
        public uint FileIndex { get; set; } = InvalidIndex;

        /// <summary>
        /// Compressed size of that file. If not compressed (<see cref="FileInfoFlags.Compressed"/>), use <see cref="UncompressedSize"/>.
        /// </summary>
        public uint CompressedSize { get; set; }
        public uint UncompressedSize { get; set; }

        /// <summary>
        /// Segment size within the entire volume file.
        /// </summary>
        public uint SegmentIndex { get; set; }

        public const uint InvalidIndex = uint.MaxValue;

        public FileInfoKey() { }
        public FileInfoKey(uint fileIndex)
        {
            FileIndex = fileIndex;
        }


        public void Deserialize(ref BitStream stream)
        {
            Flags = (FileInfoFlags)stream.ReadByte();

            FileIndex = (uint)stream.ReadVarInt();
            CompressedSize = (uint)stream.ReadVarInt();
            UncompressedSize = Flags.HasFlag(FileInfoFlags.Compressed) ? (uint)stream.ReadVarInt() : CompressedSize;

            SegmentIndex = (uint)stream.ReadVarInt();
        }

        public void Serialize(ref BitStream bs)
        {
            bs.WriteByte((byte)Flags);
            bs.WriteVarInt((int)FileIndex);
            bs.WriteVarInt((int)CompressedSize);
            if (Flags.HasFlag(FileInfoFlags.Compressed))
                bs.WriteVarInt((int)UncompressedSize);

            bs.WriteVarInt((int)SegmentIndex);
        }

        public uint GetSerializedKeySize()
        {
            uint keyLength = 1;
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)FileIndex);
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)CompressedSize);
            if (Flags.HasFlag(FileInfoFlags.Compressed))
                keyLength += (uint)BitStream.GetSizeOfVarInt((int)UncompressedSize);
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)SegmentIndex);

            return keyLength;
        }

        public FileInfoKey GetLastIndex()
        {
            return default(FileInfoKey);
        }

        public void SerializeIndex(ref BitStream stream)
        {

        }

        public uint GetSerializedIndexSize()
        {
            return 0;
        }

        public override string ToString()
            => $"Flags: {Flags} FileIndex: {FileIndex} ({PDIPFSPathResolver.GetPathFromSeed(FileIndex)}), SegmentIndex: {SegmentIndex}, CompressedSize: {CompressedSize}, UncompSize: {UncompressedSize}";

        public FileInfoKey CompareGetDiff(FileInfoKey key)
        {
            return key;
        }
    }

    [Flags]
    public enum FileInfoFlags
    {
        Uncompressed,
        Compressed,
        CustomSalsaCrypt,
    }
}
