using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        private const uint HeaderSize = 0xA0; // 160

        /// <summary>
        /// Magic for the header.
        /// </summary>
        public byte[] Magic { get; private set; } = HeaderMagic;

        /// <summary>
        /// Also used as seed to decrypt the table of contents.
        /// </summary>
        public uint LastIndex { get; set; }

        /// <summary>
        /// Compressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint CompressedTOCSize { get; set; }

        /// <summary>
        /// Uncompressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint TOCSize { get; set; }

        public ulong Unk { get; private set; }

        /// <summary>
        /// Total size of the volume.
        /// </summary>
        public ulong TotalVolumeSize { get; set; }

        /// <summary>
        /// Title ID for this header.
        /// </summary>
        public string TitleID { get; set; }

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
            if (!magic.AsSpan().SequenceEqual(HeaderMagic.AsSpan()))
            {
                Console.WriteLine($"[X] Volume file Magic did not match, found ({string.Join('-', magic.Select(e => e.ToString("X2")))}) - make sure your keys are valid.");
                return null;
            }

            gtHeader.LastIndex = sr.ReadUInt32();
            gtHeader.CompressedTOCSize = sr.ReadUInt32();
            gtHeader.TOCSize = sr.ReadUInt32();
            gtHeader.Unk = sr.ReadUInt64();
            gtHeader.TotalVolumeSize = sr.ReadUInt64();
            gtHeader.TitleID = sr.ReadString0();

            return gtHeader;
        }

        public byte[] Serialize()
        {
            byte[] newHeader = new byte[HeaderSize];
            SpanWriter sw = new SpanWriter(newHeader, Endian.Big);
            sw.WriteBytes(HeaderMagic);
            sw.WriteUInt32(LastIndex);
            sw.WriteUInt32(CompressedTOCSize);
            sw.WriteUInt32(TOCSize);
            sw.WriteUInt64(Unk);
            sw.WriteUInt64(TotalVolumeSize);

            string newTitle = TitleID.Split('|')[0].TrimEnd() + $" | Last repacked with GTToolsSharp: {DateTimeOffset.UtcNow:G}";
            sw.WriteStringRaw(newTitle.Length < 128 ? newTitle : TitleID);

            return newHeader;
        }
    }
}
