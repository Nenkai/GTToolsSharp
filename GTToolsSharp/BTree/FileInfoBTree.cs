using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Buffers;
using System.Diagnostics;

using PDTools.Utils;

namespace GTToolsSharp.BTree;

public class FileInfoBTree : BTree<FileInfoKey>
{
    public FileInfoBTree(Memory<byte> buffer, PFSBTree parentToC)
        : base(buffer, parentToC)
    {
        
    }

    public uint SearchIndexByKey(FileInfoKey key)
    {
        // We are searching in index blocks
        BitStream stream = new BitStream(BitStreamMode.Read, _buffer.Span);

        uint indexBlockCount = stream.ReadByte();
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
            uint nextPageOffset = (uint)indexDataStream.ReadVarInt();

            stream.Position = (int)nextPageOffset;
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
            //key.Deserialize(ref keyStream);
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

    public override FileInfoKey SearchByKey(Span<byte> data)
    {
        throw new NotImplementedException();
    }
}
