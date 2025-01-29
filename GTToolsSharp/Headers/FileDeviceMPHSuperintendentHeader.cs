using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using PDTools.Utils;

using GTToolsSharp.Entities;

namespace GTToolsSharp.Headers;

internal class FileDeviceMPHSuperintendentHeader : MPHVolumeHeaderBase
{
    public override int HeaderSize => 0x800;

    private static readonly uint HeaderMagic = 0x5B745163;

    public ulong JulianTimestamp { get; set; }

    // 0x01 = Encrypt
    public uint Flags { get; set; }
    public uint FormatterCode { get; set; }

    public IndexerMPH Indexer = new IndexerMPH();
    public MPHNodeInfo[] Nodes { get; set; }

    public uint IndexDataOffset { get; set; }
    public uint IndexDataSize { get; set; }
    public uint NodeInfoOffset { get; set; }
    public uint NodeInfoSize { get; set; }

    public uint VolumeInfoCount { get; set; }
    public uint VolumeInfoOffset { get; set; }

    public uint DictInfoCount { get; set; }
    public uint DictInfoOffset { get; set; }

    public const uint NR_DICT_MAX = 16;

    public override void Read(Span<byte> buffer)
    {
        BitStream bs = new BitStream(BitStreamMode.Read, buffer, BitStreamSignificantBitOrder.MSB);

        Magic = new byte[4];
        bs.ReadIntoByteArray(4, Magic, 8);

        int unk = bs.ReadInt32(); // Random value?
        JulianTimestamp = bs.ReadUInt64();
        SerialNumber = bs.ReadUInt64();
        int unk2 = bs.ReadInt32(); // Random value 2?
        Flags = bs.ReadUInt32();
        FormatterCode = bs.ReadUInt32();

        if (FormatterCode != 0x11160000)
            Program.Log("Warning: Superintendent formatter code does not match 0x11160000");

        IndexDataOffset = bs.ReadUInt32();
        IndexDataSize = bs.ReadUInt32();
        NodeInfoOffset = bs.ReadUInt32();
        NodeInfoSize = bs.ReadUInt32();
        
        if (SerialNumber > 660770580)
        {
            VolumeInfoOffset = bs.ReadUInt32();
            VolumeInfoCount = bs.ReadUInt32();
            DictInfoOffset = bs.ReadUInt32();
            DictInfoCount = bs.ReadUInt32();
            
            byte[] digest = new byte[0x10];
            bs.ReadIntoByteArray(digest.Length, digest, 8);

            bs.Position = (int)VolumeInfoOffset;
        }
        else
        {
            VolumeInfoCount = bs.ReadUInt32();
        }
            

        VolumeInfo = new ClusterVolumeInfoMPH[VolumeInfoCount];
        for (var i = 0; i < VolumeInfoCount; i++)
        {
            ClusterVolumeInfoMPH clusterVolume = new ClusterVolumeInfoMPH();
            clusterVolume.Read(ref bs);
            VolumeInfo[i] = clusterVolume;
        }

        bs.Position = (int)IndexDataOffset;
        Indexer.Read(ref bs);

        bs.Position = (int)NodeInfoOffset;
        Nodes = new MPHNodeInfo[Indexer.NodeCount];
        for (var i = 0; i < Indexer.NodeCount; i++)
        {
            MPHNodeInfo node = new MPHNodeInfo();
            node.NodeIndex = i;
            node.Read(ref bs);
            Nodes[i] = node;
        }
    }

    public override byte[] Serialize()
    {
        throw new NotImplementedException();
    }

    public override void PrintInfo()
    {
        Program.Log($"[>] PFS Version/Serial No: '{SerialNumber}'");
        Program.Log($"[>] Serial Date: {new DateTime(2001, 1, 1) + TimeSpan.FromSeconds(SerialNumber)}");
        Program.Log($"[>] Formatter Code: 0x{FormatterCode:X8}");
        Program.Log($"[>] VOL Count: {VolumeInfo.Length}");
        Program.Log($"[>] Flags: 0x{Flags:X8}");

        Program.Log($"[>] Index Data Offset: 0x{IndexDataOffset:X8}");
        Program.Log($"[>] Index Data Size: 0x{IndexDataSize:X8}");
        Program.Log($"[>] Node Info Offset: 0x{NodeInfoOffset:X8}");
        Program.Log($"[>] Node Info Size: 0x{NodeInfoSize:X8}");
        Program.Log($"[>] Volume Info Offset: 0x{VolumeInfoOffset}");
        Program.Log($"[>] Volume Info Count: {VolumeInfoCount}");
        Program.Log($"[>] Dict Info Offset: 0x{DictInfoOffset}");
        Program.Log($"[>] Dict Info Count: {DictInfoCount}");
    }

    public MPHNodeInfo GetNodeByPath(string path)
    {
        var nodeIndex = Indexer.GetNodeIndex(path);
        var node = GetNodeByIndex(nodeIndex);

        if (node == null)
            return null;

        var candiateHash = fnv1a(path);
        if (node.EntryHash == candiateHash)
            return node;

        return null;
    }

    public static uint fnv1a(string str)
    {
        uint result = 0x811c9dc5, prime = 16777619;
        foreach (var c in str)
        {
            result ^= (byte)c;
            result *= prime;
        }

        return result;

    }
    public MPHNodeInfo GetNodeByIndex(int index)
    {
        if (index >= 0 && index < Nodes.Length)
        {
            return Nodes[index];
        }

        return null;
    }
}

public class ClusterVolumeInfoMPH
{
    public string FileName { get; set; }
    public byte PlayGoChunkIndex { get; set; }
    public ushort Unk { get; set; }
    public ulong VolumeSize { get; set; }

    public void Read(ref BitStream bs)
    {
        FileName = bs.ReadStringRaw(0x10).TrimEnd('\0');
        PlayGoChunkIndex = bs.ReadByte();
        Unk = bs.ReadUInt16();
        VolumeSize = bs.ReadBits(40);
    }

    public override string ToString()
    {
        return $"{FileName} (Size: 0x{VolumeSize:X10})";
    }
}

public class IndexerMPH
{
    public int NodeCount { get; set; }
    public ulong ExistsCount { get; set; }
    public int[] Exists { get; set; }
    public ulong ExistsACM256Count { get; set; }
    public int[] ExistsACM256 { get; set; }
    public ulong ExistsACM32Count { get; set; }
    public byte[] ExistsACM32 { get; set; }
    public uint VertexCount { get; set; }
    public uint[] Seeds { get; set; } = new uint[3];
    public ulong GValueCount { get; set; }
    public byte[] GValues { get; set; }

    public void Read(ref BitStream bs)
    {
        NodeCount = bs.ReadInt32();

        ExistsCount = bs.ReadUInt64();
        Exists = new int[ExistsCount];
        for (var i = 0; i < Exists.Length; i++)
            Exists[i] = bs.ReadInt32();

        ExistsACM256Count = bs.ReadUInt64();
        ExistsACM256 = new int[ExistsACM256Count];
        for (var i = 0; i < ExistsACM256.Length; i++)
            ExistsACM256[i] = bs.ReadInt32();

        ExistsACM32Count = bs.ReadUInt64();
        ExistsACM32 = new byte[ExistsACM32Count];
        for (var i = 0; i < ExistsACM32.Length; i++)
            ExistsACM32[i] = bs.ReadByte();

        VertexCount = bs.ReadUInt32();

        for (var i = 0; i < Seeds.Length; i++)
            Seeds[i] = bs.ReadUInt32();

        GValueCount = bs.ReadUInt64();
        GValues = new byte[GValueCount];
        for (var i = 0; i < GValues.Length; i++)
            GValues[i] = bs.ReadByte();
    }

    /***************************************
     * Minimal Perfect Hash Implementation  *
     ***************************************/
    public uint GetHashFromString(uint seed, uint lim, string key)
    {
        uint h = 1111111111, s = seed;
        for (var i = 0; i < key.Length; ++i)
        {
            s = s * 1504569917 + 987987987;
            h = (uint)((h * 103UL % seed) + GetHash(s, lim, key[i]));
        }

        return h % seed % lim;
    }

    public int GetNodeIndex(string str)
    {
        var limit = VertexCount;
        var indices = new uint[]
        {
            GetHashFromString(Seeds[0], limit, str),
            GetHashFromString(Seeds[1], limit, str) + limit,
            GetHashFromString(Seeds[2], limit, str) + limit * 2,
        };

        var index = (GetG((int)indices[0]) + GetG((int)indices[1]) + GetG((int)indices[2])) % 3;
        index = (int)indices[index];

        return ExistsACM256[index / 256] + ExistsACM32[index / 32] + System.Numerics.BitOperations.PopCount(  (ulong)Exists[index / 32] & ((1u << (index % 32)) - 1) );
    }

    public uint GetHash(uint seed, uint lim, uint key)
    {
        return (uint)(((ulong)key * 1350490027 + 123456789012345UL) % seed % lim);
    }

    int GetG(int i)  
    {
        return 3 & (GValues[i / 4] >> ((i % 4) * 2));
    }
}
