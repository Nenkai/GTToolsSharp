using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.IO;

using GTToolsSharp.Encryption;

using Syroot.BinaryData.Core;
using Syroot.BinaryData;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.Headers
{
    public abstract class VolumeHeaderBase
    {
        private static readonly byte[] HeaderMagic = { 0x5B, 0x74, 0x51, 0x62 };
        private static readonly byte[] OldHeaderMagic = { 0x5B, 0x74, 0x51, 0x61 };

        public abstract int HeaderSize { get; }

        public byte[] Magic;

        /// <summary>
        /// Entry index for the TOC, also used as the seed to find the toc path.
        /// </summary>
        public uint ToCNodeIndex { get; set; }

        /// <summary>
        /// Compressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint CompressedTOCSize { get; set; }

        /// <summary>
        /// Uncompressed size in bytes for the segment of the table of contents.
        /// </summary>
        public uint ExpandedTOCSize { get; set; }

        /// <summary>
        /// Also Patch Sequence
        /// </summary>
        public ulong SerialNumber { get; set; }

        public const uint GTSPMagicEncryptKey = 0x9AEFDE67;

        public static VolumeHeaderType Detect(Span<byte> header)
        {
            SpanReader sr = new SpanReader(header, Endian.Big);

            byte[] magic = sr.ReadBytes(4);
            if (magic.AsSpan().SequenceEqual(OldHeaderMagic.AsSpan()))
                return VolumeHeaderType.PFS;

            if (magic.AsSpan().SequenceEqual(HeaderMagic.AsSpan()))
                return VolumeHeaderType.PFS2;

            uint magicInt = BinaryPrimitives.ReadUInt32BigEndian(magic);
            if ((magicInt ^ GTSPMagicEncryptKey) == 0x5B745162)
                return VolumeHeaderType.PFS3;
            else
                return VolumeHeaderType.Unknown;

        }

        public static VolumeHeaderBase Load(Stream input, GTVolume parentVolume, VolumeHeaderType volHeaderType)
        {
            VolumeHeaderBase header = volHeaderType switch
            {
                VolumeHeaderType.PFS => new FileDeviceGTFSHeader(),
                VolumeHeaderType.PFS2 => new FileDeviceGTFS2Header(),
                VolumeHeaderType.PFS3 => new FileDeviceGTFS3Header(),
                _ => throw new Exception("Invalid volume type provided."),
            };

            if (input.Length < header.HeaderSize)
                throw new Exception("Input stream was too small for the header size.");

            var headerBytes = input.ReadBytes(header.HeaderSize);
            parentVolume.DecryptHeader(headerBytes, GTVolume.BASE_VOLUME_ENTRY_INDEX);
            header.Read(headerBytes);
            return header;
        }

        public abstract void Read(Span<byte> buffer);

        public abstract byte[] Serialize();
    }

    public enum VolumeHeaderType
    {
        Unknown,

        /// <summary>
        /// GT Prologue JP Demo
        /// </summary>
        PFS,

        /// <summary>
        /// GT5(P), GT6
        /// </summary>
        PFS2,

        /// <summary>
        /// GT7SP
        /// </summary>
        PFS3,
    }
}
