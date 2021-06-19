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

        public void Deserialize(ref BitStream stream)
        {
            Flags = (EntryKeyFlags)stream.ReadByte();
            NameIndex = (uint)stream.ReadVarInt();
            FileExtensionIndex = Flags.HasFlag(EntryKeyFlags.File) ? (uint)stream.ReadVarInt() : 0;
            EntryIndex = (uint)stream.ReadVarInt();
        }

        public void Serialize(ref BitStream bs)
        {
            /*
            bs.WriteByte((byte)Flags);
            EncodeAndAdvance(bs, NameIndex);
            if (Flags.HasFlag(EntryKeyFlags.File))
                EncodeAndAdvance(bs, FileExtensionIndex);

            EncodeAndAdvance(bs, EntryIndex);
            */
        }

        public uint GetSerializedKeySize()
        {
            uint keyLength = 1;
            keyLength += (uint)BitStream.GetSizeOfVarInt((int)NameIndex);
            if (Flags.HasFlag(EntryKeyFlags.File))
                keyLength += (uint)BitStream.GetSizeOfVarInt((int)FileExtensionIndex);
            EntryIndex += (uint)BitStream.GetSizeOfVarInt((int)NameIndex);

            return keyLength;
        }

        public FileEntryKey GetLastIndex()
        {
            return default(FileEntryKey);
        }

        public void SerializeIndex(ref BitStream stream)
        {
            
        }

        public uint GetSerializedIndexSize()
        {
            return 0;
        }

        public override string ToString()
            => $"Flags: {Flags}, NameIndex: {NameIndex}, FileExtensionIndex: {FileExtensionIndex}, EntryIndex: {EntryIndex}";

        public FileEntryKey CompareGetDiff(FileEntryKey key)
        {
            throw new NotImplementedException();
        }
    }

    [Flags]
    public enum EntryKeyFlags
    {
        Directory = 0x01,
        File = 0x02,
    }
}
