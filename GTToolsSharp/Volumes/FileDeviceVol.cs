using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Encryption;

using Syroot.BinaryData;

using PDTools.Compression;

namespace GTToolsSharp.Volumes;

/// <summary>
/// File device for a single volume handle used in GT7SP. (Disposable object)
/// </summary>
public class FileDeviceVol : IDisposable
{
    public const ulong Magic = 0x2B26958523AD;
    private readonly FileStream _fs;

    public string Name { get; set; }

    public uint SectorSize { get; set; }
    public uint ClusterSize { get; set; }
    public ulong VolumeSize { get; set; }
    public uint Flags { get; set; }

    private FileDeviceVol(FileStream fs)
        => _fs = fs;

    public static FileDeviceVol Read(string path)
    {
        FileStream fs = new FileStream(path, FileMode.Open);
        BinaryStream bs = new BinaryStream(fs, ByteConverter.Little);

        ulong magic = bs.ReadUInt64();
        if (magic != Magic)
            return null;

        var fileDevice = new FileDeviceVol(fs);
        fileDevice.SectorSize = bs.ReadUInt32();
        fileDevice.ClusterSize = bs.ReadUInt32();
        fileDevice.VolumeSize = bs.ReadUInt64();
        fileDevice.Flags = bs.ReadUInt32();
        return fileDevice;
    }

    public bool UnpackFile(FileInfoKey nodeKey, Keyset keyset, string filePath)
    {
        long offset = SectorSize * (long)nodeKey.SectorOffset;
        _fs.Position = offset;

        if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed) || nodeKey.Flags.HasFlag(FileInfoFlags.PDIZIPCompressed))
        {
            if (!CryptoUtils.DecryptCheckCompression(_fs, keyset, nodeKey.FileIndex, nodeKey.UncompressedSize))
            {
                Program.Log($"[X] Failed to decompress file ({filePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            _fs.Position -= 8;
            return CryptoUtils.DecryptAndInflateToFile(keyset, _fs, nodeKey.FileIndex, nodeKey.UncompressedSize, filePath, false);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            CryptoUtils.CryptToFile(keyset, _fs, nodeKey.FileIndex, nodeKey.UncompressedSize, filePath, false);
        }

        return true;
    }

    public void Dispose()
    {
        ((IDisposable)_fs).Dispose();
    }
}
