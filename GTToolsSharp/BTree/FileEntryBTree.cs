using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;
using System.Diagnostics;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using GTToolsSharp.Utils;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class FileEntryBTree : BTree<FileEntryKey>
    {
        public FileEntryBTree() { }
        public FileEntryBTree(Memory<byte> buffer)
            : base(buffer)
        {
            
        }

        public override int EqualToKeyCompareOp(FileEntryKey key, Span<byte> data)
        {
            BitStream stream = new BitStream(BitStreamMode.Read, data);

            uint nameIndex = stream.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = stream.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;

            return 0;
        }

        public override int LessThanKeyCompareOp(FileEntryKey key, Span<byte> data)
        {
            BitStream stream = new BitStream(BitStreamMode.Read, data);

            uint nameIndex = stream.ReadUInt32();
            if (key.NameIndex < nameIndex)
                return -1;
            else if (key.NameIndex > nameIndex)
                return 1;

            uint extIndex = stream.ReadUInt32();

            if (key.FileExtensionIndex < extIndex)
                return -1;
            else if (key.FileExtensionIndex > extIndex)
                return 1;
            else
                throw new Exception("?????");
        }

        public override FileEntryKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }


        public void ResortByNameIndexes()
        {
            /* Well, quicksort can go to hell. 
             * Wasted 2 days of mine because this crap would reorder entries that werent needed to be 
             * OrderBy doesnt touch them as it should be 
             */
            // Entries.Sort((x, y) => x.NameIndex.CompareTo(y.NameIndex)); 
            Entries = Entries.OrderBy(e => e.NameIndex).ToList();
        }

        public FileEntryKey GetFolderEntryByNameIndex(uint nameIndex)
        {
            foreach (var entry in Entries)
            {
                if (entry.NameIndex == nameIndex && entry.FileExtensionIndex == 0)
                    return entry;
            }

            return null;
        }

        public override void Serialize(ref BitStream stream, GTVolumeTOC parentTOC)
        {
            int baseTreePos = stream.Position;
            stream.Position += 6;

            short segmentCount = 0; // 1 per 0x1000
            int index = 0; // To keep track of which key we are currently at

            // Following lists is for writing index blocks later on
            BitStream indexStream = new BitStream(BitStreamMode.Write);
            IndexWriter<FileEntryKey> indexWriter = new IndexWriter<FileEntryKey>();

            int baseSegmentPos = stream.Position;
            while (index < Entries.Count)
            {
                int segmentKeyCount = 0;
                baseSegmentPos = stream.Position;

                BitStream entryWriter = new BitStream(BitStreamMode.Write, 1024);

                // To keep track of where the keys are located to write them in the toc
                List<int> segmentKeyOffsets = new List<int>();

                // Write keys in 0x1000 segments
                while (index < Entries.Count)
                {
                    FileEntryKey key = Entries[index];
                    uint keySize = key.GetSerializedKeySize();

                    // Get the current segment size - apply size of key offsets (12 bits each) (extra (+ 12 + 12) due to segment header and next segment offset)
                    int currentSizeTaken = MiscUtils.MeasureBytesTakenByBits(((double)segmentKeyCount * 12) + 12 + 12);
                    currentSizeTaken += entryWriter.Position; // And size of key data themselves

                    // Segment size exceeded?
                    if (currentSizeTaken + (keySize + 2) >= BTREE_SEGMENT_SIZE) // Extra 2 to fit the 12 bits as short
                    {
                        // Segment's done, move on to next
                        break;
                    }

                    // To build up the segment's TOC when its filled or done
                    segmentKeyOffsets.Add(entryWriter.Position);

                    // Serialize the key
                    key.Serialize(ref entryWriter);

                    // Move on to next
                    segmentKeyCount++;
                    index++;
                }

                // Avoid writing the end yet
                if (index < Entries.Count)
                {
                    // For info btrees, the next *entry* index within the list is what we write for the game to search
                    indexWriter.AddIndex(ref indexStream, (int)Entries[index].NameIndex, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]);
                }

                // Finish up segment header
                stream.Position = baseSegmentPos;
                stream.WriteBoolBit(true);
                stream.WriteBits((ulong)segmentKeyCount, 11);

                int tocSize = MiscUtils.MeasureBytesTakenByBits(((double)segmentKeyCount * 12) + 12 + 12);
                for (int i = 0; i < segmentKeyCount; i++)
                {
                    // Translate each key offset to segment relative offsets
                    stream.WriteBits((ulong)(tocSize + segmentKeyOffsets[i]), 12);
                }

                Span<byte> entryBuffer = entryWriter.GetSpan();
                int nextSegmentOffset = tocSize + entryBuffer.Length;

                Debug.Assert(nextSegmentOffset < BTREE_SEGMENT_SIZE, "Next segment offset beyond segment size?");

                stream.WriteBits((ulong)nextSegmentOffset, 12);

                // Write key data
                stream.WriteByteData(entryWriter.GetSpan());

                segmentCount++;
            }

            // Write index blocks that links all segments together 
            int indexBlocksOffset = stream.Position - baseTreePos;

            if (segmentCount > 1)
            {
                FileEntryKey t = new FileEntryKey();
                var lastEntryIndex = t.GetLastIndex();
                lastEntryIndex.FileExtensionIndex = (uint)(parentTOC.Extensions.Entries.Count);

                indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, parentTOC.FileNames.Entries.Count, lastEntryIndex); // Last index of file name & extension tree
                stream.WriteByteData(indexStream.GetSpan());
            }

            // Align the tree to nearest 0x04
            stream.Align(0x04);

            int endPos = stream.Position;
            stream.Position = baseTreePos;

            // Finish tree header
            stream.WriteByte(indexWriter.SegmentCount);
            stream.WriteBits(segmentCount > 1 ? (ulong)indexBlocksOffset : 6, 24); // If theres no index block, just point to the first and only segment
            stream.WriteInt16(segmentCount);

            // Done. Move to bottom.
            stream.Position = endPos;
        }

    }
}
