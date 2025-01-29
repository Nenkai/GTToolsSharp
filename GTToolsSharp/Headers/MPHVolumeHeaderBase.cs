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

public abstract class MPHVolumeHeaderBase
{
    private const uint HeaderMagic = 0x6351745B;

    public abstract int HeaderSize { get; }

    public byte[] Magic;
    public ClusterVolumeInfoMPH[] VolumeInfo { get; set; }

    /// <summary>
    /// Also Patch Sequence. Shared across versions for each platforms in GT7
    /// </summary>
    public ulong SerialNumber { get; set; }

    public static MPHVolumeHeaderType Detect(Span<byte> header)
    {
        SpanReader sr = new SpanReader(header, Endian.Big);

        var magic = sr.ReadUInt32();
        if (magic == FileDeviceMPHSuperintendentHeader.HeaderMagic)
            return MPHVolumeHeaderType.MPH;
        
        return MPHVolumeHeaderType.Unknown;
    }

    public static MPHVolumeHeaderBase Load(Stream input, GTVolumeMPH parentVolume, MPHVolumeHeaderType volHeaderType, out byte[] headerBytes)
    {
        MPHVolumeHeaderBase header = volHeaderType switch
        {
            MPHVolumeHeaderType.MPH => new FileDeviceMPHSuperintendentHeader(),
            _ => throw new Exception("Invalid volume type provided."),
        };

        if (input.Length < header.HeaderSize)
            throw new Exception("Input stream was too small for the header size.");

        if (volHeaderType == MPHVolumeHeaderType.MPH)
        {
            headerBytes = new byte[input.Length];
            input.ReadExactly(headerBytes);
            GTVolumeMPH.DecryptHeader(headerBytes.AsSpan());
        }
        else
            headerBytes = null;

        header.Read(headerBytes);
        return header;
    }

    public abstract void Read(Span<byte> buffer);

    public abstract byte[] Serialize();

    public abstract void PrintInfo();
}

public enum MPHVolumeHeaderType
{
    Unknown,

    /// <summary>
    /// GT7
    /// </summary>
    MPH,
}
