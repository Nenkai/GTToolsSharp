
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
}
