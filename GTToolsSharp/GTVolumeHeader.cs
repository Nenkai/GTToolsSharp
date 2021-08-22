using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;

using Syroot.BinaryData.Memory;
using Syroot.BinaryData.Core;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents a header for a volume which contains general information for the volume.
    /// </summary>
    public class GTVolumeHeader
    {
        private static readonly byte[] HeaderMagic = { 0x5B, 0x74, 0x51, 0x62 };
        private static readonly byte[] OldHeaderMagic = { 0x5B, 0x74, 0x51, 0x61 };

        public const uint GTSPMagicEncryptKey = 0x9AEFDE67;

        private const uint HeaderSize = 0xA0; // 160

        /// <summary>
        /// Magic for the header.
        /// </summary>
        public byte[] Magic { get; private set; } = HeaderMagic;

        /// <summary>
        /// Entry index for the TOC, also used as the seed to find the toc path.
        /// </summary>
        public uint TOCEntryIndex { get; set; }

        /// <summary>
        /// Compressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint CompressedTOCSize { get; set; }

        /// <summary>
        /// Uncompressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint TOCSize { get; set; }

        public ulong PFSVersion { get; set; }

        /// <summary>
        /// Total size of the volume.
        /// </summary>
        public ulong TotalVolumeSize { get; set; }

        /// <summary>
        /// Title ID for this header.
        /// </summary>
        public string TitleID { get; set; }

        public bool HasCustomGameID { get; set; }
        /// <summary>
        /// Reads a header from a byte buffer.
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        public static GTVolumeHeader FromStream(Span<byte> header)
        {
            var gtHeader = new GTVolumeHeader();

            SpanReader sr = new SpanReader(header, Endian.Big);

            byte[] magic = sr.ReadBytes(4);

            bool isGTSP = false;
            if (!magic.AsSpan().SequenceEqual(HeaderMagic.AsSpan()) && !magic.AsSpan().SequenceEqual(OldHeaderMagic.AsSpan()))
            {
                uint magicInt = BinaryPrimitives.ReadUInt32BigEndian(magic);
                if ((magicInt ^ GTSPMagicEncryptKey) != 0x5B745162)
                    return null;
                else
                    isGTSP = true;
            }

            bool isOld = magic.AsSpan().SequenceEqual(OldHeaderMagic.AsSpan());

            if (!isOld)
            {
                if (isGTSP)
                {
                    sr.Endian = Endian.Little;
                    ulong julianBuiltTime = sr.ReadUInt64();
                    ulong serialNumber = sr.ReadUInt64(); // Also patch sequence

                    sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_0-3
                    sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_4-7
                    sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_8-b
                    sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_c-f

                    sr.Position = 0xF0;
                    int node = sr.ReadInt32();
                    int compressedSize = sr.ReadInt32();
                    int expandedSize = sr.ReadInt32();
                    int volListCount = sr.ReadInt32();

                    for (int i = 0; i < volListCount; i++)
                    {
                        sr.ReadStringRaw(16); // Name
                        sr.ReadUInt64(); // Size
                    }
                }
                else
                {
                    gtHeader.TOCEntryIndex = sr.ReadUInt32();
                    gtHeader.CompressedTOCSize = sr.ReadUInt32();
                    gtHeader.TOCSize = sr.ReadUInt32();
                    gtHeader.PFSVersion = sr.ReadUInt64();
                    gtHeader.TotalVolumeSize = sr.ReadUInt64();
                    gtHeader.TitleID = sr.ReadString0();
                }
            }
            else
            {
                sr.Position += 4;
                gtHeader.TOCEntryIndex = sr.ReadUInt32();
                gtHeader.CompressedTOCSize = sr.ReadUInt32();
                gtHeader.TOCSize = sr.ReadUInt32();
            }

            return gtHeader;
        }

        public byte[] Serialize()
        {
            byte[] newHeader = new byte[HeaderSize];
            SpanWriter sw = new SpanWriter(newHeader, Endian.Big);
            sw.WriteBytes(HeaderMagic);
            sw.WriteUInt32(TOCEntryIndex);
            sw.WriteUInt32(CompressedTOCSize);
            sw.WriteUInt32(TOCSize);
            sw.WriteUInt64(PFSVersion);
            sw.WriteUInt64(TotalVolumeSize);

            if (HasCustomGameID)
                sw.WriteStringRaw(TitleID);
            else
            {
                string newTitle = TitleID.Split('|')[0].TrimEnd() + $" | Last repacked with GTToolsSharp: {DateTimeOffset.UtcNow:G}";
                sw.WriteStringRaw(newTitle.Length < 128 ? newTitle : TitleID);
            }

            return newHeader;
        }
    }
}
