using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Utils;

namespace GTToolsSharp.BTree
{
    public class FileEntryBTree : BTree<FileEntryKey>
    {
        public FileEntryBTree() { }
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
                uint high = CryptoUtils.GetBitsAt(ref sr, 0) & 0x7FFu;
                uint nextOffset = CryptoUtils.GetBitsAt(ref sr, high + 1);

                for (uint j = 0; j < high; j++) // high is pretty much entry count
                {
                    uint offset = CryptoUtils.GetBitsAt(ref sr, j + 1);
                    var data = sr.GetReaderAtOffset((int)offset);

                    FileEntryKey key = new FileEntryKey();
                    key.OffsetFromTree = data.Position;

                    key.Deserialize(ref data);
                    unpacker.UnpackFromKey(key);
                }

                sr.Position += (int)nextOffset;
            }
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

        public void ResortByNameIndexes()
            => Entries.Sort((x, y) => x.NameIndex.CompareTo(y.NameIndex));

        public FileEntryKey GetFolderEntryByNameIndex(uint nameIndex)
        {
            foreach (var entry in Entries)
            {
                if (entry.NameIndex == nameIndex && entry.FileExtensionIndex == 0)
                    return entry;
            }

            return null;
        }

        public void Serialize(BinaryStream bTreeWriter, uint fileNameCount, uint extensionCount)
        {
            uint lastKeyIndex = 0;
            ushort segmentCount = 0;

            List<uint> extNameIndexes = new List<uint>();
            List<uint> nextKeyIndexes = new List<uint>();
            List<uint> nodeOffsets = new List<uint>();

            uint childOffset = (uint)6;
            uint treeStartOffset = (uint)bTreeWriter.Position;

            bTreeWriter.Position += 6; // Skip segment count

            bool writeNameExt = true;

            // Go through a segment, everytime
            // Each segment contains indexes, and strings
            while (lastKeyIndex < Entries.Count)
            {
                int keySegmentIndex = (int)lastKeyIndex;
                uint keysThisSegment = 0;

                List<uint> currentSegmentOffsets = new List<uint>();

                // Streams for the current segment
                using var offsetsBuffer = new MemoryStream();
                using var offsetsBufferWriter = new BinaryStream(offsetsBuffer, ByteConverter.Big);
                using var keyTreeBuffer = new MemoryStream();
                using var keyTreeBufferWriter = new BinaryStream(keyTreeBuffer, ByteConverter.Big);

                while (keySegmentIndex < Entries.Count)
                {
                    uint offsetAligned = ((keysThisSegment + 4u) * 12) / 8u;
                    // Is Odd
                    if ((keysThisSegment + 4 & 1) == 0)
                        offsetAligned--;

                    uint keyLength = 0;
                    if (keySegmentIndex < Entries.Count)
                        keyLength = Entries[keySegmentIndex].GetSerializedKeySize();

                    if (keyTreeBufferWriter.Position + offsetAligned + keyLength >= (GTVolumeTOC.SEGMENT_SIZE * 2) || keySegmentIndex + 1 == Entries.Count)
                    {
                        bool writeNext = false;
                        byte[] tempSegmentBuffer = Array.Empty<byte>();
                        if (keySegmentIndex + 1 == Entries.Count)
                        {
                            if (offsetAligned + keyTreeBufferWriter.Position + keyLength >= (GTVolumeTOC.SEGMENT_SIZE * 2))
                            {
                                // We need to start a new segment
                                using var nextSegmentOffsetsBuffer = new MemoryStream();
                                using var nextSegmentOffsetsBufferWriter = new BinaryStream(nextSegmentOffsetsBuffer, ByteConverter.Big);
                                using var nextSegmentKeyBuffer = new MemoryStream();
                                using var nextSegmentKeyBufferWriter = new BinaryStream(nextSegmentKeyBuffer, ByteConverter.Big);

                                CryptoUtils.WriteBitsAt(nextSegmentKeyBufferWriter, GTVolumeTOC.SEGMENT_SIZE + 1, 0); // Size to the next segment
                                CryptoUtils.WriteBitsAt(nextSegmentKeyBufferWriter, currentSegmentOffsets[0] + 5, 1); // Offset
                                CryptoUtils.WriteBitsAt(nextSegmentKeyBufferWriter, keyLength + 5, 2); // Offset
                                nextSegmentOffsetsBufferWriter.Write(nextSegmentKeyBuffer.ToArray());
                                Entries[keySegmentIndex].Serialize(nextSegmentOffsetsBufferWriter);
                                segmentCount++;

                                nextKeyIndexes.Add(Entries[keySegmentIndex].NameIndex);
                                extNameIndexes.Add(Entries[keySegmentIndex].FileExtensionIndex);
                                tempSegmentBuffer = nextSegmentOffsetsBuffer.ToArray();

                                writeNext = true;
                                writeNameExt = true;
                            }
                            else
                            {
                                currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                                Entries[keySegmentIndex].Serialize(keyTreeBufferWriter);
                                keysThisSegment++;
                            }
                        }

                        // Write the key offset
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, keysThisSegment + GTVolumeTOC.SEGMENT_SIZE, 0);

                        // Write "high", the node count
                        for (int o = 0; o < keysThisSegment; o++)
                            CryptoUtils.WriteBitsAt(offsetsBufferWriter, currentSegmentOffsets[o] + (((keysThisSegment + 3) * 12) / 8) - 1, (uint)o + 1u);

                        offsetAligned = (((keysThisSegment + 3) * 12) / 8) - 1;
                        CryptoUtils.WriteBitsAt(offsetsBufferWriter, offsetAligned + (uint)keyTreeBuffer.Length, keysThisSegment + 1);
                        bTreeWriter.Write(offsetsBuffer.ToArray());
                        bTreeWriter.Write(keyTreeBuffer.ToArray());

                        if (writeNext)
                        {
                            bTreeWriter.Write(tempSegmentBuffer);
                            keysThisSegment++;
                        }

                        nodeOffsets.Add((uint)((bTreeWriter.Position - treeStartOffset) - offsetsBufferWriter.Length - keyTreeBufferWriter.Length - tempSegmentBuffer.Length));
                        if (writeNext)
                            nodeOffsets.Add((uint)((bTreeWriter.Position - treeStartOffset) - tempSegmentBuffer.Length));

                        segmentCount++;
                        writeNameExt = false;
                        break;
                    }

                    if (!writeNameExt)
                    {
                        nextKeyIndexes.Add(Entries[keySegmentIndex].NameIndex);
                        extNameIndexes.Add(Entries[keySegmentIndex].FileExtensionIndex);
                        writeNameExt = true;
                    }

                    currentSegmentOffsets.Add((uint)keyTreeBufferWriter.Position);
                    Entries[keySegmentIndex].Serialize(keyTreeBufferWriter);
                    keysThisSegment++;
                    keySegmentIndex++;
                }

                lastKeyIndex += keysThisSegment;
            }

            nextKeyIndexes.Add(fileNameCount);
            extNameIndexes.Add(extensionCount);

            if (segmentCount > 1)
            {
                childOffset = (uint)(bTreeWriter.Position - treeStartOffset);
                SerializeChildren(bTreeWriter, nextKeyIndexes, extNameIndexes, nodeOffsets);
            }

            long tempPos = bTreeWriter.Position;
            bTreeWriter.Position = treeStartOffset;

            bTreeWriter.WriteByte(segmentCount > 1 ? (byte)1 : (byte)0);
            bTreeWriter.WriteUInt24BE(childOffset);
            bTreeWriter.Write(segmentCount);

            bTreeWriter.Position = tempPos;
        }

        private static void SerializeChildren(BinaryStream srcStream, List<uint> nextIndexes, List<uint> extIndexes, List<uint> segOffsets)
        {
            using var childrenBuffer = new MemoryStream();
            using var childrenBufferWriter = new BinaryStream(childrenBuffer, ByteConverter.Big);

            List<uint> segmentOffsets = new List<uint>();
            byte[] entryBuffer = Array.Empty<byte>();
            uint entryCount = 0;

            using var entryBufferStream = new MemoryStream();
            using var entryBufferWriter = new BinaryStream(entryBufferStream, ByteConverter.Big);

            for (int i = 0; i < nextIndexes.Count; i++)
            {
                segmentOffsets.Add((uint)entryBufferWriter.BaseStream.Position);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, nextIndexes[i]);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, extIndexes[i]);
                CryptoUtils.EncodeAndAdvance(entryBufferWriter, segOffsets[i]);
                entryCount++;
            }
            entryBuffer = entryBufferStream.ToArray();
            
            CryptoUtils.WriteBitsAt(childrenBufferWriter, entryCount, 0);

            for (uint i = 0; i < entryCount; i++)
                CryptoUtils.WriteBitsAt(childrenBufferWriter, segmentOffsets[(int)i] + (((entryCount + 1) * 12) / 8) + 2, i + 1);

            uint remainingTillNextSegment = ((entryCount + 3) * 12) / 8 - 1;
            CryptoUtils.WriteBitsAt(childrenBufferWriter, remainingTillNextSegment + (uint)entryBuffer.Length, entryCount + 1);
            childrenBufferWriter.Write(entryBuffer);
            childrenBufferWriter.WriteUInt16(0);
            srcStream.Write(childrenBuffer.ToArray());
        }

    }
}
