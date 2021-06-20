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
using static GTToolsSharp.Utils.CryptoUtils;

using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class FileInfoBTree : BTree<FileInfoKey>
    {
        public FileInfoBTree(Memory<byte> buffer)
            : base(buffer)
        {

        }


        public uint SearchIndexByKey(FileInfoKey key)
        {
            // We are searching in index blocks
            BitStream stream = new BitStream(BitStreamMode.Read, _buffer.Span);

            uint indexBlockCount = (uint)stream.ReadByte();
            int indexBlockOffset = (int)stream.ReadBits(24);

            stream.Position = indexBlockOffset;

            SearchResult res = new SearchResult();

            Span<byte> data = default;
            for (uint i = indexBlockCount; i != 0; i--)
            {
                data = SearchWithComparison(ref stream, indexBlockCount, key, res, SearchCompareMethod.LessThan);
                if (data.IsEmpty)
                    goto DONE;

                BitStream indexDataStream = new BitStream(BitStreamMode.Read, data);
                res.maxIndex = (uint)indexDataStream.ReadVarInt();
                uint nextSegmentOffset = (uint)indexDataStream.ReadVarInt();

                stream.Position = (int)nextSegmentOffset;
            }

            // Search within regular blocks
            data = SearchWithComparison(ref stream, 0, key, res, SearchCompareMethod.EqualTo);

            DONE:
            if (indexBlockCount == 0)
                res.upperBound = 0;

            if (!data.IsEmpty)
            {
                uint index = (res.maxIndex - res.upperBound + res.lowerBound);
                key.KeyOffset = (uint)(_buffer.Length - data.Length);

                BitStream keyStream = new BitStream(BitStreamMode.Read, data);
                key.Deserialize(ref keyStream);
                return index;
            }
            else
                return FileInfoKey.InvalidIndex;
            
            return 0;
        }

        public FileInfoKey GetByFileIndex(uint fileIndex)
            => Entries.FirstOrDefault(e => e.FileIndex == fileIndex);

        public override int LessThanKeyCompareOp(FileInfoKey key, Span<byte> data) 
	    {
            BitStream bitStream = new BitStream(BitStreamMode.Read, data);

            uint nodeIndex = (uint)bitStream.ReadVarInt();
            if (key.FileIndex < nodeIndex)
                return -1;
            else 
                return 1;
        }

        public override int EqualToKeyCompareOp(FileInfoKey key, Span<byte> data)
        {
            BitStream bitStream = new BitStream(BitStreamMode.Read, data);

            bitStream.ReadByte(); // Skip flag
            uint nodeIndex = (uint)bitStream.ReadVarInt();
            if (key.FileIndex < nodeIndex)
                return -1;
            else if (key.FileIndex > nodeIndex)
                return 1;
            else
                return 0;
        }

        public override void Serialize(ref BitStream stream, GTVolumeTOC parentTOC)
        {
            int baseTreePos = stream.Position;
            stream.Position += 6;

            short segmentCount = 0; // 1 per 0x1000
            int index = 0; // To keep track of which key we are currently at

            // Following lists is for writing index blocks later on
            BitStream indexStream = new BitStream(BitStreamMode.Write);
            IndexWriter<FileInfoKey> indexWriter = new IndexWriter<FileInfoKey>();

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
                    FileInfoKey key = Entries[index];
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
                    // For info btrees, the *file* index within the list is what we write for the game to search
                    indexWriter.AddIndex(ref indexStream, (int)Entries[index].FileIndex, baseSegmentPos - baseTreePos, Entries[index - 1], Entries[index]); 
                }

                // Finish up segment header
                stream.Position = baseSegmentPos;
                stream.WriteBoolBit(true);
                stream.WriteBits((ulong)segmentKeyCount, 11);

                int tocSize = MiscUtils.MeasureBytesTakenByBits((segmentKeyCount * 12) + 12 + 12);
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
                var k = new FileInfoKey();
                FileInfoKey lastKey = k.GetLastIndex();

                indexWriter.Finalize(ref indexStream, baseSegmentPos - baseTreePos, (int)Entries[^1].FileIndex + 1, lastKey); // Last index
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

        public override FileInfoKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }
    }
}
