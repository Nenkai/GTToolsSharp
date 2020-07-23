using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;

using GTToolsSharp.Utils;
using static GTToolsSharp.Utils.CryptoUtils;
namespace GTToolsSharp.BTree
{
    public class FileEntryKey : IBTreeKey
    {
        /// <summary>
        /// Offset from the beginning of the FileEntry Tree table.
        /// </summary>
        public long OffsetFromTree;

        public EntryKeyFlags Flags;
        public uint NameIndex;
        public uint FileExtensionIndex;
        public uint EntryIndex;

        public void Deserialize(ref SpanReader sr)
        {
            Flags = (EntryKeyFlags)sr.ReadByte();
            NameIndex = (uint)CryptoUtils.DecodeBitsAndAdvance(ref sr);
            FileExtensionIndex = Flags.HasFlag(EntryKeyFlags.File) ? (uint)CryptoUtils.DecodeBitsAndAdvance(ref sr) : 0;
            EntryIndex = (uint)CryptoUtils.DecodeBitsAndAdvance(ref sr);
        }

        public void Serialize(BinaryStream bs)
        {
            bs.WriteByte((byte)Flags);
            EncodeAndAdvance(bs, NameIndex);
            if (Flags.HasFlag(EntryKeyFlags.File))
                EncodeAndAdvance(bs, FileExtensionIndex);

            EncodeAndAdvance(bs, EntryIndex);
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
            => $"Flags: {Flags}, NameIndex: {NameIndex}, FileExtensionIndex: {FileExtensionIndex}, EntryIndex: {EntryIndex}";

    }

    [Flags]
    public enum EntryKeyFlags
    {
        Directory = 0x01,
        File = 0x02,
    }
}
