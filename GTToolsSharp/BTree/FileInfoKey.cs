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

namespace GTToolsSharp.BTree
{
    /// <summary>
    /// Represents a key that holds relational btree data and file sizes.
    /// </summary>
    public class FileInfoKey : IBTreeKey
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


        public void Deserialize(ref SpanReader sr)
        {
            Flags = (FileInfoFlags)sr.ReadByte();

            FileIndex = (uint)DecodeBitsAndAdvance(ref sr);
            CompressedSize = (uint)DecodeBitsAndAdvance(ref sr);
            UncompressedSize = Flags.HasFlag(FileInfoFlags.Compressed) ? (uint)DecodeBitsAndAdvance(ref sr) : CompressedSize;

            SegmentIndex = (uint)DecodeBitsAndAdvance(ref sr);
        }

        public void Serialize(BinaryStream bs)
        {
            bs.WriteByte((byte)Flags);
            EncodeAndAdvance(bs, FileIndex);
            EncodeAndAdvance(bs, CompressedSize);
            if (Flags.HasFlag(FileInfoFlags.Compressed))
                EncodeAndAdvance(bs, UncompressedSize);

            EncodeAndAdvance(bs, SegmentIndex);
        }

        public uint GetSerializedKeySize()
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(20); // Average size should do
            using var keySizeMeasurer = new MemoryStream(data);
            using var keyBufferWriter = new BinaryStream(keySizeMeasurer, ByteConverter.Big);
            Serialize(keyBufferWriter);
            uint keyLength = (uint)keySizeMeasurer.Position;
            ArrayPool<byte>.Shared.Return(data, true);
            return keyLength;
        }

        public override string ToString()
            => $"Flags: {Flags} FileIndex: {FileIndex} ({PDIPFSPathResolver.GetPathFromSeed(FileIndex)}), SegmentIndex: {SegmentIndex}, CompressedSize: {CompressedSize}, UncompSize: {UncompressedSize}";

    }

    [Flags]
    public enum FileInfoFlags
    {
        Uncompressed,
        Compressed
    }
}
