using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

namespace GTToolsSharp.BTree
{
    class FileEntryBTree : BTree<FileEntryBTree, FileEntryKey>
    {
        public FileEntryBTree(byte[] buffer, int offset)
            : base(buffer, offset)
        {
            
        }

        public void TraverseAndUnpack(EntryUnpacker unpacker)
        {
            SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position += _offsetStart;

            uint offsetAndCount = sr.ReadUInt32();

            uint nodeCount = sr.ReadUInt16();

            for (int i = 0; i < nodeCount; i++)
            {
                uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, high + 1);

                for (uint j = 0; j < high; ++j) // high is pretty much entry count
                {
                    uint offset = GetBitsAt(ref sr, j + 1);
                    var data = sr.GetReaderAtOffset((int)offset);

                    FileEntryKey key = new FileEntryKey();
                    key.OffsetFromTree = data.Position;

                    key = ReadKeyFromStream(key, ref data);
                    unpacker.UnpackFromKey(key);
                }

                sr.Position += (int)nextOffset;
            }
        }

        public void TraverseAndPack(EntryPacker packer)
        {
            SpanReader sr = new SpanReader(_buffer, Endian.Big);
            sr.Position += _offsetStart;

            uint offsetAndCount = sr.ReadUInt32();

            uint nodeCount = sr.ReadUInt16();

            for (int i = 0; i < nodeCount; i++)
            {
                uint high = GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = GetBitsAt(ref sr, high + 1);

                for (uint j = 0; j < high; ++j) // high is pretty much entry count
                {
                    uint offset = GetBitsAt(ref sr, j + 1);
                    var data = sr.GetReaderAtOffset((int)offset);

                    FileEntryKey key = new FileEntryKey();
                    key.OffsetFromTree = data.Position;

                    key = ReadKeyFromStream(key, ref data);
                    packer.PackFromKey(key);
                }

                sr.Position += (int)nextOffset;
            }
        }

        public override FileEntryKey ReadKeyFromStream(FileEntryKey key, ref SpanReader sr)
        {
            key.Flags = (EntryKeyFlags)sr.ReadByte();
            key.NameIndex = (uint)Utils.DecodeBitsAndAdvance(ref sr);
            key.FileExtensionIndex = key.Flags.HasFlag(EntryKeyFlags.File) ? (uint)Utils.DecodeBitsAndAdvance(ref sr) : 0;
            key.DirEntryIndex = (uint)Utils.DecodeBitsAndAdvance(ref sr);

            return key;
        }

        public override int EqualToKeyCompareOp(FileEntryKey key, ref SpanReader sr)
        {
            uint nameIndex = sr.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = sr.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;

            return 0;
        }

        public override int LessThanKeyCompareOp(FileEntryKey key, ref SpanReader sr)
        {
            uint nameIndex = sr.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = sr.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;
            else
                throw new Exception("?????");
        }

        public override FileEntryKey SearchByKey(ref SpanReader sr)
        {
            throw new NotImplementedException();
        }
    }
}
