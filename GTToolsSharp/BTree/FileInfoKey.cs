using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;
using System.IO;
using Syroot.BinaryData.Memory;
using Syroot.BinaryData;

using GTToolsSharp.Headers;
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
        /// File Sector Offset within the entire volume file.
        /// </summary>
        public uint SectorOffset { get; set; }

        public const uint InvalidIndex = uint.MaxValue;
        
        /// <summary>
        /// For GT7SP
        /// </summary>
        public bool UsesMultipleVolumes { get; set; }

        /// <summary>
        /// To which volume this file belongs to (GT7SP)
        /// </summary>
        public uint VolumeIndex { get; set; } = unchecked((uint)-1);

        public FileInfoKey() { }
        public FileInfoKey(uint fileIndex)
        {
            FileIndex = fileIndex;
        }


        public void Deserialize(ref BitStream stream, GTVolumeTOC parentToC)
        {
            Flags = (FileInfoFlags)stream.ReadByte();
            FileIndex = (uint)stream.ReadVarInt();
            CompressedSize = (uint)stream.ReadVarInt();

            if (Flags.HasFlag(FileInfoFlags.Compressed) || (int)Flags == 34)
                UncompressedSize = (uint)stream.ReadVarInt();
            else
                UncompressedSize = CompressedSize;
        
            if (parentToC.ParentHeader is FileDeviceGTFS3Header)
                VolumeIndex = (uint)stream.ReadVarInt();
            SectorOffset = (uint)stream.ReadVarInt();
        }

        public void Serialize(ref BitStream bs)
        {
            bs.WriteByte((byte)Flags);
            bs.WriteVarInt((int)FileIndex);
            bs.WriteVarInt((int)CompressedSize);
            if (Flags.HasFlag(FileInfoFlags.Compressed))
                bs.WriteVarInt((int)UncompressedSize);

            if (UsesMultipleVolumes)
                bs.WriteVarInt((int)VolumeIndex);

            bs.WriteVarInt((int)SectorOffset);
        }

        public uint GetSerializedKeySize()
        {
            uint keyLength = 1;
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)FileIndex);
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)CompressedSize);
            if (Flags.HasFlag(FileInfoFlags.Compressed))
                keyLength += (uint)BitStream.GetSizeOfVarInt((int)UncompressedSize);

            if (UsesMultipleVolumes)
                keyLength += (uint)BitStream.GetSizeOfVarInt((int)VolumeIndex);

            keyLength += (uint)BitStream.GetSizeOfVarInt((int)SectorOffset);

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
        {
            return $"Flags: {Flags} | FileIndex: {FileIndex} ({PDIPFSPathResolver.GetPathFromSeed(FileIndex)}) | SegIndex: {SectorOffset}, ZSize: {CompressedSize:X8} | Size: {UncompressedSize:X8} | VolIndex: {VolumeIndex}";
        }
            

        public FileInfoKey CompareGetDiff(FileInfoKey key)
        {
            return key;
        }
    }

    [Flags]
    public enum FileInfoFlags
    {
        Uncompressed = 0x0,
        Compressed = 0x01,
        CustomSalsaCrypt = 0x02,
        a = 0x04,
        b = 0x08,
        c = 0x10,
        d = 0x20,
    }
}
