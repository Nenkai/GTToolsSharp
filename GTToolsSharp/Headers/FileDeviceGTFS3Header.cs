using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

using PDTools.Utils;

namespace GTToolsSharp.Headers;

public class FileDeviceGTFS3Header : PFSVolumeHeaderBase
{
    private static readonly byte[] HeaderMagic = [0x5B, 0x74, 0x51, 0x62];

    public override int HeaderSize => 0xA60;

    public ulong JulianBuiltTime { get; set; }

    public VolEntryGTFS3[] VolList { get; set; }

    public record VolEntryGTFS3(string Name, ulong Size);

    public override void Read(Span<byte> buffer)
    {
        SpanReader sr = new SpanReader(buffer, Endian.Big);
        Magic = sr.ReadBytes(4); // Magic

        JulianBuiltTime = sr.ReadUInt64();
        SerialNumber = sr.ReadUInt64(); // Also patch sequence

        sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_0-3
        sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_4-7
        sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_8-b
        sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); sr.ReadInt32(); // reserve_c-f

        sr.Position = 0xF0;
        ToCNodeIndex = sr.ReadUInt32();
        CompressedTOCSize = sr.ReadUInt32();
        ExpandedTOCSize = sr.ReadUInt32();
        uint volListCount = sr.ReadUInt32();

        VolList = new VolEntryGTFS3[(int)volListCount];

        for (int i = 0; i < volListCount; i++)
        {
            byte[] name = sr.ReadBytes(16);
            ulong size = sr.ReadUInt64();
            VolList[i] = new VolEntryGTFS3(GetActualVolFileName(name), size);
        }
    }

    public override byte[] Serialize()
    {
        byte[] header = new byte[HeaderSize];
        SpanWriter sw = new SpanWriter(header, Endian.Big);
        sw.WriteBytes(HeaderMagic);
        sw.WriteUInt64(JulianBuiltTime);
        sw.WriteUInt64(SerialNumber);
        sw.Position += sizeof(uint) * 16;

        sw.Position = 0xF0;
        sw.WriteUInt32(ToCNodeIndex);
        sw.WriteUInt32(CompressedTOCSize);
        sw.WriteUInt32(ExpandedTOCSize);
        sw.WriteInt32(VolList.Length);
        for (int i = 0; i < VolList.Length; i++)
        {
            // TODO
            throw new NotImplementedException();
        }

        return header;
    }

    private static string GetActualVolFileName(byte[] nameBytes)
    {
        Span<byte> nameSpan = nameBytes.AsSpan();
        for (int i = 0; i < 4; i++)
        {
            Span<byte> currentPart = nameSpan.Slice(i * sizeof(uint), sizeof(uint));
            currentPart.Reverse();
        }

        string s = Encoding.ASCII.GetString(nameSpan);
        return s.TrimEnd('\0');
    }

    public override void PrintInfo()
    {
        Program.Log($"[>] PFS Version/Serial No: '{SerialNumber}' ({new DateTime(2001, 1, 1) + TimeSpan.FromSeconds(SerialNumber)})");
        Program.Log($"[>] Table of Contents Entry Index: {ToCNodeIndex}");
        Program.Log($"[>] TOC Size: 0x{CompressedTOCSize:X8} bytes (0x{ExpandedTOCSize:X8} expanded)");
    }
}
