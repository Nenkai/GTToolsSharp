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

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class FileEntryKey : IBTreeKey<FileEntryKey>
    {
        /// <summary>
        /// Offset from the beginning of the FileEntry Tree table.
        /// </summary>
        public long OffsetFromTree;

        public EntryKeyFlags Flags;
        public uint NameIndex;
        public uint FileExtensionIndex;
        public uint EntryIndex;

        public void Deserialize(ref BitStream stream, GTVolumeTOC parentToC)
        {
            Flags = (EntryKeyFlags)stream.ReadByte();
            NameIndex = (uint)stream.ReadVarInt();
            FileExtensionIndex = Flags.HasFlag(EntryKeyFlags.File) ? (uint)stream.ReadVarInt() : 0;
            EntryIndex = (uint)stream.ReadVarInt();
        }

        public void Serialize(ref BitStream bs)
        {
            bs.WriteByte((byte)Flags);
            bs.WriteVarInt((int)NameIndex);
            if (Flags.HasFlag(EntryKeyFlags.File))
                bs.WriteVarInt((int)FileExtensionIndex);

            bs.WriteVarInt((int)EntryIndex);
        }

        public uint GetSerializedKeySize()
        {
            uint keyLength = 1;
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)NameIndex);
            if (Flags.HasFlag(EntryKeyFlags.File))
                keyLength += (uint)BitStream.GetSizeOfVarInt((int)FileExtensionIndex);
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)EntryIndex);

            return keyLength;
        }

        public FileEntryKey GetLastIndex()
        {
            return new FileEntryKey();
        }

        public void SerializeIndex(ref BitStream stream)
        {
            stream.WriteVarInt((int)FileExtensionIndex);
        }

        public uint GetSerializedIndexSize()
        {
            return 0;
        }

        public override string ToString()
            => $"Flags: {Flags}, NameIndex: {NameIndex}, FileExtensionIndex: {FileExtensionIndex}, EntryIndex: {EntryIndex}";

        public FileEntryKey CompareGetDiff(FileEntryKey key)
        {
            return key;
        }
    }

    [Flags]
    public enum EntryKeyFlags
    {
        Directory = 0x01,
        File = 0x02,
    }
}
