using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;
using Syroot.BinaryData;

using PDTools.Utils;

namespace GTToolsSharp.BTree;

[DebuggerDisplay("Count = {Entries.Count}")]
public abstract class BTree<TKey> where TKey : IBTreeKey<TKey>, new()
{
    public const int BTREE_PAGE_SIZE = 0x1000;

    protected Memory<byte> _buffer;
    private readonly PFSBTree _parentToC;

    public List<TKey> Entries = [];

    public BTree()
    {

    }

    public BTree(Memory<byte> buffer, PFSBTree parentToC)
    {
        _buffer = buffer;
        _parentToC = parentToC;
    }

    public TKey GetByIndex(uint index)
        => Entries[(int)index];

    public void LoadEntries()
    {
        BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

        byte indexPageCount = treeStream.ReadByte();
        uint indexPagesOffset = (uint)treeStream.ReadBits(24);
        short pageCount = treeStream.ReadInt16();

        // Iterate through all pages and all their keys
        for (int i = 0; i < pageCount; i++)
        {
            int thisPageOffset = treeStream.Position;

            bool moreThanOneKey = treeStream.ReadBoolBit();
            uint keyCount = (uint)treeStream.ReadBits(11);

            // 25/07/2021 - Not necessarily true - caused issues with 2.11 US Toc (GT5) - moreThanOneKey was true, keyCount was 0
            // Debug.Assert(moreThanOneKey && keyCount > 0, "More than one key flag set but key count is 0?");

            List<uint> keyOffsets = new List<uint>((int)keyCount);
            for (uint j = 0; j < keyCount; j++)
            {
                uint offset = (uint)treeStream.ReadBits(12);
                keyOffsets.Add(offset);
            }

            // Offset to the next page for the btree
            uint nextPageOffset = (uint)treeStream.ReadBits(12);

            for (int j = 0; j < keyCount; j++)
            {
                treeStream.Position = (int)(thisPageOffset + keyOffsets[j]);
                Debug.Assert(keyOffsets[j] < nextPageOffset, "Key offset was beyond next page offset?");

                // Parse key info
                TKey key = new TKey();
                key.Deserialize(ref treeStream, _parentToC);
                Entries.Add(key);
            }

            // Done with this page, go to next
            if (i != pageCount - 1)
                treeStream.Position = (int)(thisPageOffset + nextPageOffset);
        }
    }

    public void LoadEntriesOld()
    {
        BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

        byte indexPageCount = treeStream.ReadByte();
        uint pageCount = (uint)treeStream.ReadBits(12);
        uint unkOffset = (uint)treeStream.ReadBits(12);

        // Iterate through all pages and all their keys
        for (int i = 0; i < pageCount + 1; i++)
        {
            int segPos = treeStream.Position;

            bool moreThanOneKey = treeStream.ReadBoolBit();
            uint keyCount = (uint)treeStream.ReadBits(11);

            Debug.Assert(moreThanOneKey && keyCount > 0);

            List<uint> keyOffsets = new List<uint>((int)keyCount);
            for (uint j = 0; j < keyCount; j++)
            {
                uint offset = (uint)treeStream.ReadBits(12);
                keyOffsets.Add(offset);
            }

            for (int j = 0; j < keyCount; j++)
            {
                treeStream.Position = (int)(segPos + keyOffsets[j]);

                // Parse key info
                TKey key = new TKey();
                key.Deserialize(ref treeStream, _parentToC);
                Entries.Add(key);
            }
        }
    }

    public bool TryFindIndex(uint index, out TKey key)
    {
        BitStream treeStream = new BitStream(BitStreamMode.Read, _buffer.Span);

        byte hasEntries = treeStream.ReadByte();
        uint indexBlockOffset = (uint)treeStream.ReadBits(24);
        short pageCount = treeStream.ReadInt16();

        for (uint i = 0u; i < pageCount; ++i)
        {
            int thisPageOffset = treeStream.Position;

            bool moreThanOneKey = treeStream.ReadBoolBit();
            uint keyCount = (uint)treeStream.ReadBits(11);

            if (index < keyCount)
            {
                treeStream.SeekToBit((int)((treeStream.Position * 8) + (index + 1) * 12));
                int keyOffset = (int)treeStream.ReadBits(12);

                treeStream.SeekToByte(thisPageOffset + keyOffset);

                key = new TKey();
                key.Deserialize(ref treeStream, _parentToC);
                return key != null;
            }

            index -= keyCount;

            uint nextPageOffset = (uint)treeStream.ReadBits(12);
            treeStream.SeekToByte((int)(thisPageOffset + nextPageOffset));
        }

        key = default;
        return false;
    }

    public abstract TKey SearchByKey(Span<byte> data);

    public abstract int LessThanKeyCompareOp(TKey key, Span<byte> data);

    public abstract int EqualToKeyCompareOp(TKey key, Span<byte> data);

    public Span<byte> SearchWithComparison(scoped ref BitStream stream, uint count, TKey key, SearchResult res, SearchCompareMethod method)
    {
        int segPos = stream.Position;

        bool moreThanOneKey = stream.ReadBoolBit();
        uint high = (uint)stream.ReadBits(11); // Page key count

        uint low = 0;
        uint index = 0;

        res.upperBound = high;

        Span<byte> keyData = default;

        // BSearch page to compare with our target key
        while (low < high)
        {
            uint mid = low + (high - low) / 2;
            index = mid + 1;

            stream.SeekToBit((int)((segPos * 8) + index * 12));
            int keyOffset = (int)stream.ReadBits(12);
            stream.SeekToByte(segPos + keyOffset);

            keyData = stream.GetSpanFromCurrentPosition();

            int ret;
            if (method == SearchCompareMethod.LessThan)
                ret = LessThanKeyCompareOp(key, keyData);
            else if (method == SearchCompareMethod.EqualTo)
                ret = EqualToKeyCompareOp(key, keyData);
            else
                throw new ArgumentException($"Invalid search method provided '{method}'");

            if (ret == 0)
            {
                res.lowerBound = mid;
                res.index = mid;
                return keyData;
            }
            else if (ret > 0)
                low = index;
            else if (ret < 0)
            {
                high = mid;
                index = mid;
            }
        }

        res.lowerBound = index;
        res.index = ~0u;

        if (count != 0 && index != res.upperBound)
        {
            stream.SeekToBit((int)((segPos * 8) + (index + 1) * 12));
            int keyOffset = (int)stream.ReadBits(12);

            stream.SeekToByte(segPos + keyOffset);
            keyData = stream.GetSpanFromCurrentPosition();
        }

        return keyData;
    }

    public void Serialize(ref BitStream stream, PFSBTree parentTOC)
    {
        int baseTreePos = stream.Position;
        stream.Position += 6;

        short pageCount = 0; // 1 per 0x1000
        int index = 0; // To keep track of which key we are currently at

        // Following lists is for writing index blocks later on
        BitStream indexStream = new BitStream();
        IndexWriter<TKey> indexWriter = new IndexWriter<TKey>();

        int basePageOffset = stream.Position;
        while (index < Entries.Count)
        {
            int pageKeyCount = 0;
            basePageOffset = stream.Position;

            BitStream entryWriter = new BitStream(1024);

            // To keep track of where the keys are located to write them in the toc
            List<int> pageKeyOffsets = [];

            // Write keys in 0x1000 pages
            while (index < Entries.Count)
            {
                TKey key = Entries[index];
                uint keySize = key.GetSerializedKeySize();

                // Get the current page size - apply size of key offsets (12 bits each) (extra (+ 12 + 12) due to page header and page offset)
                int currentSizeTaken = MiscUtils.MeasureBytesTakenByBits(((double)pageKeyCount * 12) + 12 + 12);
                currentSizeTaken += entryWriter.Position; // And size of key data themselves

                // Page size exceeded?
                if (currentSizeTaken + (keySize + 2) >= BTREE_PAGE_SIZE) // Extra 2 to fit the 12 bits as short
                {
                    // Page's done, move on to next
                    break;
                }

                // To build up the page's TOC when its filled or done
                pageKeyOffsets.Add(entryWriter.Position);

                // Serialize the key
                key.Serialize(ref entryWriter);

                // Move on to next
                pageKeyCount++;
                index++;
            }

            // Avoid writing the end yet
            if (index < Entries.Count)
            {
                // For info btrees, the *file* index within the list is what we write for the game to search
                TKey current = Entries[index];

                if (current is FileInfoKey fileInfoKey)
                    indexWriter.AddIndex(ref indexStream, fileInfoKey.FileIndex, (uint)(basePageOffset - baseTreePos), Entries[index - 1], Entries[index]);
                else if (current is FileEntryKey fileEntryKey)
                    indexWriter.AddIndex(ref indexStream, fileEntryKey.NameIndex, (uint)(basePageOffset - baseTreePos), Entries[index - 1], Entries[index]);
                else if (current is StringKey)
                    indexWriter.AddIndex(ref indexStream, (uint)index, (uint)(basePageOffset - baseTreePos), Entries[index - 1], Entries[index]);
            }


            // Finish up page header
            stream.Position = basePageOffset;
            stream.WriteBoolBit(true);
            stream.WriteBits((ulong)pageKeyCount, 11);

            int tocSize = MiscUtils.MeasureBytesTakenByBits((pageKeyCount * 12) + 12 + 12);
            for (int i = 0; i < pageKeyCount; i++)
            {
                // Translate each key offset to page relative offsets
                stream.WriteBits((ulong)(tocSize + pageKeyOffsets[i]), 12);
            }

            Span<byte> entryBuffer = entryWriter.GetSpanToCurrentPosition();
            int nextPageOffset = tocSize + entryBuffer.Length;

            Debug.Assert(nextPageOffset < BTREE_PAGE_SIZE, "Next segment offset beyond segment size?");

            stream.WriteBits((ulong)nextPageOffset, 12);

            // Write key data
            stream.WriteByteData(entryBuffer);

            pageCount++;
        }

        // Write index blocks that links all pages together 
        int indexBlocksOffset = stream.Position - baseTreePos;

        if (pageCount > 1)
        {
            // Hack to check current type
            var k = new TKey();
            TKey lastKey = k.GetLastEntryAsIndex(); // This should be static but w/e

            if (k is FileEntryKey lastEntryIndex)
            {
                lastEntryIndex.FileExtensionIndex = (uint)(parentTOC.Extensions.Entries.Count);
                indexWriter.Finalize(ref indexStream, (uint)(basePageOffset - baseTreePos), (uint)parentTOC.FileNames.Entries.Count, lastKey); // Last index of file name & extension tree
            }
            else if (k is FileInfoKey)
                indexWriter.Finalize(ref indexStream, (uint)(basePageOffset - baseTreePos), (Entries[^1] as FileInfoKey).FileIndex + 1, lastKey); // Last index
            else if (k is StringKey)
                indexWriter.Finalize(ref indexStream, (uint)(basePageOffset - baseTreePos), (uint)Entries.Count, lastKey); // Last index

            stream.WriteByteData(indexStream.GetSpanToCurrentPosition());
        }

        // Align the tree to nearest 0x04
        stream.Align(0x04);

        int endPos = stream.Position;
        stream.Position = baseTreePos;

        // Finish tree header
        stream.WriteByte(indexWriter.PageCount);
        stream.WriteBits(pageCount > 1 ? (ulong)indexBlocksOffset : 6, 24); // If theres no index block, just point to the first and only page
        stream.WriteInt16(pageCount);

        // Done. Move to bottom.
        stream.Position = endPos;
    }

    public enum SearchCompareMethod
    {
        LessThan,
        EqualTo,
    }
}

public enum BTreeEndian
{
    Little,
    Big
}
