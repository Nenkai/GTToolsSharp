
using PDTools.Utils;

namespace GTToolsSharp.Entities
{
    public class MPHNodeInfo
    {
        public int NodeIndex { get; set; }

        public uint EntryHash { get; set; }
        public uint Nonce { get; set; }
        public uint CompressedSize { get; set; }
        public uint UncompressedSize;
        public uint SectorIndex { get; set; }
        public byte VolumeIndex { get; set; }
        public MPHNodeFormat Format { get; set; }
        public bool ForcedEncryptionIfPlain { get; set; }
        public MPHNodeKind Kind { get; set; }
        public MPHNodeAlgo Algo { get; set; }
        public byte ExtraFlags { get; set; }

        public void Read(ref BitStream bs)
        {
            EntryHash = bs.ReadUInt32();
            uint compressedSizeLow = bs.ReadUInt32();
            Nonce = bs.ReadUInt32();
            uint uncompressedSizeLow = bs.ReadUInt32();

            SectorIndex = (uint)bs.ReadBits(25);
            VolumeIndex = (byte)bs.ReadBits(7);

            CompressedSize = (uint)(bs.ReadByte() << 32) | compressedSizeLow;

            Format = (MPHNodeFormat)bs.ReadBits(2);
            ForcedEncryptionIfPlain = bs.ReadBoolBit();
            bs.ReadBoolBit(); // No idea

            Kind = (MPHNodeKind)bs.ReadBits(2);
            Algo = (MPHNodeAlgo)bs.ReadBits(2);

            UncompressedSize = (uint)(bs.ReadByte() << 32) | uncompressedSizeLow;

            // 4 bits dictionary index
            ExtraFlags = (byte)bs.ReadByte();
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
}
