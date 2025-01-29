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

using GTToolsSharp.Volumes;

namespace GTToolsSharp.Headers;

public abstract class PFSVolumeHeaderBase
{
    private static readonly byte[] HeaderMagic = [0x5B, 0x74, 0x51, 0x62];
    private static readonly byte[] OldHeaderMagic = [0x5B, 0x74, 0x51, 0x61];

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

    public static PFSVolumeHeaderType Detect(Span<byte> header)
    {
        SpanReader sr = new SpanReader(header, Endian.Big);

        byte[] magic = sr.ReadBytes(4);
        if (magic.AsSpan().SequenceEqual(OldHeaderMagic.AsSpan()))
            return PFSVolumeHeaderType.PFS;

        if (magic.AsSpan().SequenceEqual(HeaderMagic.AsSpan()))
            return PFSVolumeHeaderType.PFS2;

        uint magicInt = BinaryPrimitives.ReadUInt32BigEndian(magic);
        if ((magicInt ^ GTSPMagicEncryptKey) == 0x5B745162)
            return PFSVolumeHeaderType.PFS3;

        return PFSVolumeHeaderType.Unknown;
    }

    public static PFSVolumeHeaderBase Load(Stream input, GTVolumePFS parentVolume, PFSVolumeHeaderType volHeaderType, out byte[] headerBytes)
    {
        PFSVolumeHeaderBase header = volHeaderType switch
        {
            PFSVolumeHeaderType.PFS => new FileDeviceGTFSHeader(),
            PFSVolumeHeaderType.PFS2 => new FileDeviceGTFS2Header(),
            PFSVolumeHeaderType.PFS3 => new FileDeviceGTFS3Header(),
            _ => throw new Exception("Invalid volume type provided."),
        };

        if (input.Length < header.HeaderSize)
            throw new Exception("Input stream was too small for the header size.");

        if (volHeaderType == PFSVolumeHeaderType.PFS)
        {
            headerBytes = new byte[header.HeaderSize];
            input.ReadExactly(headerBytes);
            parentVolume.DecryptHeaderGT5PDemo(headerBytes);
        }
        else
        {
            headerBytes = new byte[header.HeaderSize];
            input.ReadExactly(headerBytes);
            parentVolume.DecryptHeader(headerBytes, GTVolumePFS.BASE_VOLUME_ENTRY_INDEX);
        }

        header.Read(headerBytes);
        return header;
    }

    public abstract void Read(Span<byte> buffer);

    public abstract byte[] Serialize();

    public abstract void PrintInfo();
}

public enum PFSVolumeHeaderType
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
