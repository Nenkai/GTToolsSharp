using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PDTools.Utils;

namespace GTToolsSharp.BTree;

public class IndexWriter<TKey> where TKey : IBTreeKey<TKey>, new()
{
    public const int BTREE_PAGE_SIZE = 0x1000;

    public int CurrentDataLength = 0;

    public byte PageCount = 0;
    public List<BTreeIndex> CurrentIndices { get; set; } = [];

    public IndexWriter()
    {

    }

    public void AddIndex(ref BitStream stream, uint keyIndex, uint pageOffset, TKey prevKey, TKey nextKey)
    {
        // (extra (+ 12 + 12) bits due to page header and page segment offset)
        //                                                                                        v + 1 as we include the new entry
        int newIndexPageSize = MiscUtils.MeasureBytesTakenByBits(((double)(CurrentIndices.Count + 1) * 12) + 12 + 12);
        newIndexPageSize += CurrentDataLength;

        var diffKey = prevKey.CompareGetDiff(nextKey);
        var index = new BTreeIndex(keyIndex, pageOffset, diffKey);
        uint indexSize = MeasureIndexEntrySize(index);

        if (newIndexPageSize + indexSize >= BTREE_PAGE_SIZE) // Can we fit index?
        {
            // Nope, we are about to start a new one
            WriteBlock(ref stream);
            CurrentIndices.Clear();
            CurrentDataLength = 0;
        }
        
        CurrentIndices.Add(index);
        if (CurrentIndices.Count == 1)
            PageCount++;

        CurrentDataLength += (int)indexSize;
    }

    public void Finalize(ref BitStream stream, uint pageOffset, uint lastIndex, TKey lastKeyIndex)
    {
        var lastIndexEntry = new BTreeIndex(lastIndex, pageOffset, lastKeyIndex);
        CurrentIndices.Add(lastIndexEntry);
        WriteBlock(ref stream);
    }

    private static uint MeasureIndexEntrySize(BTreeIndex index)
    {
        uint size = BitStream.GetSizeOfVarInt(index.KeyIndex);
        size += index.Key.GetSerializedIndexSize();
        size += BitStream.GetSizeOfVarInt(index.PageOffset);
        return size;
    }

    private void WriteBlock(ref BitStream stream)
    {
        int baseIndexBlockPos = stream.Position;

        // Write entries first
        BitStream indexEntryWriter = new BitStream();
        List<int> indexEntryOffsets = [];
        for (int i = 0; i < CurrentIndices.Count; i++)
        {
            indexEntryOffsets.Add(indexEntryWriter.Position);

            indexEntryWriter.WriteVarInt(CurrentIndices[i].KeyIndex);
            CurrentIndices[i].Key?.SerializeIndex(ref indexEntryWriter); // Only for strings
            indexEntryWriter.WriteVarInt(CurrentIndices[i].PageOffset);
        }

        // Done writing entries, we can write the index header
        stream.WriteBits((ulong)CurrentIndices.Count, 12);

        int tocSize = MiscUtils.MeasureBytesTakenByBits((double)(CurrentIndices.Count * 12) + 12 + 12); // Entry count (12 bits) + offset array (12 * off count) + rem next page (12)
        for (int i = 0; i < CurrentIndices.Count; i++)
            stream.WriteBits((ulong)(tocSize + indexEntryOffsets[i]), 12);
        stream.WriteBits((ulong)(tocSize + indexEntryWriter.Length), 12);
        stream.AlignToNextByte();

        // Finally add the data
        stream.Position = baseIndexBlockPos + tocSize;
        stream.WriteByteData(indexEntryWriter.GetSpanToCurrentPosition());
    }

    public record BTreeIndex(uint KeyIndex, uint PageOffset, IBTreeKey<TKey> Key);
}
