using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

using GTToolsSharp.Encryption;
using GTToolsSharp.Entities;

namespace GTToolsSharp.Headers;

/// <summary>
/// GT7 superintendent header as written by builds after 1.00 (layout as found in 1.55):
/// little-endian fields instead of a big-endian bit stream, and a reordered indexer
/// (vertex count, seeds, g-values, then node count and the exists bitmap; the two rank
/// acceleration tables are no longer stored and are rebuilt here).
/// </summary>
internal class FileDeviceMPHSuperintendentHeaderV2 : FileDeviceMPHSuperintendentHeader
{
    public const int NodeSize = 0x18;

    public int JunkNodeCount { get; private set; }

    public override void Read(Span<byte> buffer)
    {
        ReadOnlySpan<byte> b = buffer;
        Magic = b[..4].ToArray();
        JulianTimestamp = BinaryPrimitives.ReadUInt64LittleEndian(b[0x08..]);
        SerialNumber = BinaryPrimitives.ReadUInt64LittleEndian(b[0x10..]);
        Flags = BinaryPrimitives.ReadUInt32LittleEndian(b[0x1C..]);
        FormatterCode = BinaryPrimitives.ReadUInt32LittleEndian(b[0x20..]);
        if (FormatterCode != 0x11160000)
            Program.Log("Warning: Superintendent formatter code does not match 0x11160000");

        IndexDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(b[0x24..]);
        IndexDataSize = BinaryPrimitives.ReadUInt32LittleEndian(b[0x28..]);
        NodeInfoOffset = BinaryPrimitives.ReadUInt32LittleEndian(b[0x2C..]);
        NodeInfoSize = BinaryPrimitives.ReadUInt32LittleEndian(b[0x30..]);
        VolumeInfoOffset = BinaryPrimitives.ReadUInt32LittleEndian(b[0x34..]);
        VolumeInfoCount = BinaryPrimitives.ReadUInt32LittleEndian(b[0x38..]);
        DictInfoOffset = BinaryPrimitives.ReadUInt32LittleEndian(b[0x3C..]);
        DictInfoCount = BinaryPrimitives.ReadUInt32LittleEndian(b[0x40..]);

        // Volume records: name[16], PlayGo chunk u8, u16, size high byte, size low u32.
        VolumeInfo = new ClusterVolumeInfoMPH[VolumeInfoCount];
        for (int i = 0; i < VolumeInfoCount; i++)
        {
            ReadOnlySpan<byte> r = b.Slice((int)VolumeInfoOffset + i * 0x18, 0x18);
            VolumeInfo[i] = new ClusterVolumeInfoMPH
            {
                FileName = Encoding.ASCII.GetString(r[..0x10]).TrimEnd('\0'),
                PlayGoChunkIndex = r[0x10],
                Unk = BinaryPrimitives.ReadUInt16LittleEndian(r[0x11..]),
                VolumeSize = ((ulong)r[0x13] << 32) | BinaryPrimitives.ReadUInt32LittleEndian(r[0x14..]),
            };
        }

        ReadIndexer(b[(int)IndexDataOffset..]);

        Nodes = new MPHNodeInfo[Indexer.NodeCount];
        for (int i = 0; i < Indexer.NodeCount; i++)
        {
            var node = new MPHNodeInfo { NodeIndex = i };
            node.ReadV2(b.Slice((int)NodeInfoOffset + i * NodeSize, NodeSize));
            if (node.IsJunk)
                JunkNodeCount++;
            Nodes[i] = node;
        }
    }

    private void ReadIndexer(ReadOnlySpan<byte> s)
    {
        IndexerMPH ix = Indexer;
        GT7Keyset k = GT7Keyset.Current;
        ix.HashInit = k.MphHashInit;
        ix.HashMul = k.MphHashMul;
        ix.SeedMul = k.MphSeedMul;
        ix.SeedAdd = k.MphSeedAdd;
        ix.CharMul = k.MphCharMul;
        ix.CharAdd = k.MphCharAdd;
        ix.SignedChars = true;

        ix.VertexCount = BinaryPrimitives.ReadUInt32LittleEndian(s);
        for (int i = 0; i < 3; i++)
            ix.Seeds[i] = BinaryPrimitives.ReadUInt32LittleEndian(s[(4 + i * 4)..]);
        ix.GValueCount = BinaryPrimitives.ReadUInt64LittleEndian(s[0x10..]);
        int pos = 0x18;
        ix.GValues = s.Slice(pos, (int)ix.GValueCount).ToArray();
        pos += (int)ix.GValueCount;

        ix.NodeCount = BinaryPrimitives.ReadInt32LittleEndian(s[pos..]);
        ix.ExistsCount = BinaryPrimitives.ReadUInt64LittleEndian(s[(pos + 4)..]);
        pos += 12;
        ix.Exists = new int[ix.ExistsCount];
        for (int i = 0; i < ix.Exists.Length; i++)
            ix.Exists[i] = BinaryPrimitives.ReadInt32LittleEndian(s[(pos + i * 4)..]);

        // Rank tables: ACM256[w/8] = set bits in words before the 256-bit block,
        // ACM32[w] = set bits from the block start up to word w.
        ix.ExistsACM256Count = (ulong)((ix.Exists.Length + 7) / 8);
        ix.ExistsACM256 = new int[ix.ExistsACM256Count];
        ix.ExistsACM32Count = (ulong)ix.Exists.Length;
        ix.ExistsACM32 = new byte[ix.ExistsACM32Count];
        int total = 0, inBlock = 0;
        for (int w = 0; w < ix.Exists.Length; w++)
        {
            if (w % 8 == 0)
            {
                ix.ExistsACM256[w / 8] = total;
                inBlock = 0;
            }
            ix.ExistsACM32[w] = (byte)inBlock;
            int c = BitOperations.PopCount((uint)ix.Exists[w]);
            inBlock += c;
            total += c;
        }
    }

    public override void PrintInfo()
    {
        base.PrintInfo();
        Program.Log($"[>] Keyset: {GT7Keyset.Current?.Build} (post-1.00 index), junk nodes: {JunkNodeCount}");
    }
}
