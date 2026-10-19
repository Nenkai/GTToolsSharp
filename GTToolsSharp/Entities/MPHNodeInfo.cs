
using PDTools.Utils;

namespace GTToolsSharp.Entities;

public class MPHNodeInfo
{
    public int NodeIndex { get; set; }

    public uint EntryHash { get; set; }
    public uint Nonce { get; set; }
    public uint CompressedSize { get; set; }
    public uint UncompressedSize;
    public MPHNodeFormat Format { get; set; }
    public bool ForcedEncryptionIfPlain { get; set; }
    public MPHNodeKind Kind { get; set; }
    public MPHNodeAlgo Algo { get; set; }

    public byte ExtraFlags { get; set; }

    public uint SectorPlusVolumeIndexBits { get; set; }

    public void Read(ref BitStream bs)
    {
        EntryHash = bs.ReadUInt32();
        uint compressedSizeLow = bs.ReadUInt32();
        Nonce = bs.ReadUInt32();
        uint uncompressedSizeLow = bs.ReadUInt32();

        SectorPlusVolumeIndexBits = bs.ReadUInt32();

        CompressedSize = (uint)(bs.ReadByte() << 32) | compressedSizeLow;

        Format = (MPHNodeFormat)bs.ReadBits(2);
        ForcedEncryptionIfPlain = bs.ReadBoolBit();
        bs.ReadBoolBit(); // No idea

        Kind = (MPHNodeKind)bs.ReadBits(2);
        Algo = (MPHNodeAlgo)bs.ReadBits(2);

        UncompressedSize = (uint)(bs.ReadByte() << 32) | uncompressedSizeLow;

        ExtraFlags = (byte)bs.ReadByte();
    }

    /// <summary>Post-1.00 filler entry (random bytes, extra flags other than 0x80).</summary>
    public bool IsJunk { get; set; }

    /// <summary>
    /// Post-1.00 24-byte little-endian node: hash, compressed low, nonce, uncompressed low,
    /// sector|volume, compressed high, uncompressed high, flags, extra. Flag bits LSB-first:
    /// 0-1 format, 4-5 kind (0x10 = fragmented), 6-7 algorithm. Every cluster node is encrypted,
    /// stored (flags 0x00) ones included.
    /// </summary>
    public void ReadV2(System.ReadOnlySpan<byte> r)
    {
        EntryHash = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r);
        uint compressedLow = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
        Nonce = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r[8..]);
        uint uncompressedLow = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r[12..]);
        SectorPlusVolumeIndexBits = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r[16..]);
        CompressedSize = compressedLow;
        UncompressedSize = uncompressedLow;
        CompressedSizeHigh = r[20];
        UncompressedSizeHigh = r[21];

        byte flags = r[22];
        Format = (MPHNodeFormat)(flags & 3);
        Kind = (MPHNodeKind)((flags >> 4) & 3);
        Algo = (MPHNodeAlgo)(flags >> 6);
        ForcedEncryptionIfPlain = true;

        ExtraFlags = r[23];
        IsJunk = ExtraFlags != 0x80;
        if (IsJunk)
            ExtraFlags = 0;
    }

    public byte CompressedSizeHigh { get; set; }
    public byte UncompressedSizeHigh { get; set; }

    public ulong UncompressedSize64 => ((ulong)UncompressedSizeHigh << 32) | UncompressedSize;

    public uint GetSectorIndex()
    {
        return SectorPlusVolumeIndexBits & 0b1_11111111_11111111_11111111; // 25 bits
    }

    public byte GetVolumeIndex()
    {
        return (byte)(SectorPlusVolumeIndexBits >> 25); // 7 bits
    }

    public byte GetDictIndex()
    {
        return (byte)(ExtraFlags & 0x1111); // 4 bits
    }

    public bool IsFromClusterVolume()
    {
        return (ExtraFlags & 0x80) != 0;
    }

    public bool IsFromPFSStyleFile()
    {
        return (ExtraFlags & 0x80) == 0;
    }
}

public enum MPHNodeFormat
{
    PLAIN,
    PFS,
    PZ1,
    PZ2
}

public enum MPHNodeKind
{
    LUMP,
    FRAG,
}

public enum MPHNodeAlgo
{
    ZLIB,
    ZSTD,
    KRAKEN,
}
