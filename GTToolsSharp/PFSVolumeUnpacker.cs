using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.IO;
using System.IO.Compression;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Encryption;
using GTToolsSharp.BinaryPatching;
using GTToolsSharp.Volumes;

using PDTools.Compression;
using PDTools.Utils;
using PDTools.Crypto;

namespace GTToolsSharp;

public class PFSVolumeUnpacker
{
    public GTVolumePFS Volume { get; }
    public UpdateNodeInfo TPPS { get; set; }

    public string OutputDirectory { get; private set; }
    public bool NoUnpack { get; set; }
    public string BasePFSFolder { get; set; }

    public void SetOutputDirectory(string dirPath)
        => OutputDirectory = dirPath;

    public PFSVolumeUnpacker(GTVolumePFS vol)
    {
        Volume = vol;
    }

    /// <summary>
    /// Unpacks all the files within the volume.
    /// </summary>
    /// <param name="fileIndexesToExtract">If non empty, specific file indexes to extract only</param>
    /// <param name="basePFSFolder">Base PDIPFS folder for binary patching</param>
    public void UnpackFiles(IEnumerable<int> fileIndexesToExtract, string basePFSFolder)
    {
        fileIndexesToExtract ??= [];

        // Lazy way
        var files = Volume.BTree.GetAllRegisteredFileMap();

        // Cache it
        Dictionary<uint, FileInfoKey> fileInfoKeys = [];
        foreach (var file in Volume.BTree.FileInfos.Entries)
            fileInfoKeys.Add(file.FileIndex, file);

        foreach (var file in files)
        {
            if (!fileIndexesToExtract.Any() || fileIndexesToExtract.Contains((int)file.Value.EntryIndex))
            {
                string volPath = file.Key;
                var fileInfo = fileInfoKeys[file.Value.EntryIndex];
                UnpackFile(fileInfo, volPath, Path.Combine(OutputDirectory, volPath));
            }
        }
    }

    /// <summary>
    /// Unpacks a file node.
    /// </summary>
    /// <param name="nodeKey">Info of the file.</param>
    /// <param name="entryPath">Entry path of the file.</param>
    /// <param name="filePath">Local file path of the file.</param>
    /// <returns></returns>
    public bool UnpackFile(FileInfoKey nodeKey, string entryPath, string filePath)
    {
        // Split Volumes
        if ((int)nodeKey.VolumeIndex != -1)
        {
            var volDevice = Volume.SplitVolumes[nodeKey.VolumeIndex];
            if (volDevice is null)
                return false;

            Program.Log($"[:] Unpacking '{entryPath}' from '{volDevice.Name}'..");

            return Volume.SplitVolumes[nodeKey.VolumeIndex].UnpackFile(nodeKey, Volume.Keyset, filePath);
        }
        else
        {
            if (!Volume.IsPatchVolume)
            {
                if (NoUnpack)
                    return false;

                ulong offset = Volume.DataOffset + (ulong)nodeKey.SectorOffset * PFSBTree.SECTOR_SIZE;
                return UnpackVolumeFile(nodeKey, filePath, offset);
            }
            else
                return UnpackPFSFile(nodeKey, entryPath, filePath);
        }
    }

    private bool UnpackPFSFile(FileInfoKey nodeKey, string entryPath, string filePath)
    {
        string patchFilePath = PDIPFSPathResolver.GetPathFromSeed(nodeKey.FileIndex, Volume.IsGT5PDemoStyle);
        string localPath = Volume.PatchVolumeFolder + "/" + patchFilePath;

        if (NoUnpack)
            return false;

        /* I'm really not sure if there's a better way to do this.
        * Volume files, at least nodes don't seem to even store any special flag whether
        * it is located within an actual volume file or a patch volume. The only thing that is different is the sector index.. Sometimes node index when it's updated
        * It's slow, but somewhat works I guess..
        * */
        if (!File.Exists(localPath))
            return false;

        Program.Log($"[:] Unpacking: {patchFilePath} -> {filePath}");
        using var fs = new FileStream(localPath, FileMode.Open);
        if (fs.Length >= 7)
        {
            Span<byte> magic = stackalloc byte[6];
            fs.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic).StartsWith("BSDIFF"))
            {
                fs.Dispose();
                return UnpackTPPSFile(nodeKey, entryPath, patchFilePath);
            }

            fs.Position = 0;
        }

        if (Volume.IsGT5PDemoStyle)
        {
            bool customCrypt = nodeKey.Flags.HasFlag(FileInfoFlags.CustomSalsaCrypt)
                || entryPath == "piece/car_thumb_M/gtr_07_01.img" // Uncompressed files, but no special flag either...
                || entryPath == "piece/car_thumb_M/impreza_wrx_sti_07_03.img";

            Salsa20 salsa = default;

            if (customCrypt)
            {
                if (Volume.Keyset.DecryptManager is null || !Volume.Keyset.DecryptManager.Keys.TryGetValue(entryPath, out string b64Key) || b64Key.Length < 32)
                {
                    Program.Log($"[X] Could not find custom decryption key for {filePath}, skipping it.", forceConsolePrint: true);
                    return false;
                }

                byte[] key = Convert.FromBase64String(b64Key).AsSpan(0, 32).ToArray();
                salsa = new Salsa20(key, key.Length);
                Program.Log($"[/] Attempting to decrypt custom encrypted file {entryPath}..");
            }

            try
            {
                if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using (var outCompressedFile = new FileStream(filePath + ".in", FileMode.Create))
                        VolumeCrypto.DecryptOld(Volume.Keyset, fs, outCompressedFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);

                    using (var inCompressedFile = new FileStream(filePath + ".in", FileMode.Open))
                    {
                        using var outFile = new FileStream(filePath, FileMode.Create);

                        inCompressedFile.Position = 8;
                        using var ds = new DeflateStream(inCompressedFile, CompressionMode.Decompress);
                        ds.CopyTo(outFile);
                    }

                    File.Delete(filePath + ".in");
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using var outFile = new FileStream(filePath, FileMode.Create);
                    VolumeCrypto.DecryptOld(Volume.Keyset, fs, outFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);
                }

                if (customCrypt)
                    Program.Log($"[/] Successfully decrypted custom encrypted file {entryPath}.");
            }
            catch (Exception e)
            {
                Program.Log($"[X] Failed to decrypt {entryPath} ({e.Message})");
            }
        }
        else
        {
            if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
            {
                if (!CryptoUtils.DecryptCheckCompression(fs, Volume.Keyset, nodeKey.FileIndex, nodeKey.UncompressedSize))
                {
                    Program.Log($"[X] Failed to decompress file {filePath} ({patchFilePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                fs.Position = 0;

                return CryptoUtils.DecryptAndInflateToFile(Volume.Keyset, fs, nodeKey.FileIndex, nodeKey.UncompressedSize, filePath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                CryptoUtils.CryptToFile(Volume.Keyset, fs, nodeKey.FileIndex, filePath);
            }
        }

        return true;
    }

    private bool UnpackVolumeFile(FileInfoKey nodeKey, string filePath, ulong offset)
    {
        Volume.MainStream.Position = (long)offset;
        if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
        {
            if (!CryptoUtils.DecryptCheckCompression(Volume.MainStream, Volume.Keyset, nodeKey.FileIndex, nodeKey.UncompressedSize))
            {
                Program.Log($"[X] Failed to decompress file ({filePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            Volume.MainStream.Position -= 8;
            CryptoUtils.DecryptAndInflateToFile(Volume.Keyset, Volume.MainStream, nodeKey.FileIndex, nodeKey.UncompressedSize, filePath, false);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            CryptoUtils.CryptToFile(Volume.Keyset, Volume.MainStream, nodeKey.FileIndex, nodeKey.UncompressedSize, filePath, false);
        }

        return true;
    }

    private bool UnpackTPPSFile(FileInfoKey nodeKey, string filePath, string patchFilePath)
    {
        if (TPPS is null && !InitTPPS())
        {
            Program.Log($"[X] Detected BSDIFF file for {filePath} ({patchFilePath}) - could not initialize binary patcher.", forceConsolePrint: true);
            return false;
        }

        if (ApplyFromPatchAndExtract(nodeKey, filePath))
        {
            Program.Log($"[/] Extracted binary patched file: {filePath} ({patchFilePath})", forceConsolePrint: true);
            return true;
        }

        return false;
    }

    private bool InitTPPS()
    {
        if (TPPS?.Entries?.Count > 0)
            return true;

        string updateNodeInfopath = Path.Combine(Volume.InputPath, "UPDATENODEINFO");
        if (!File.Exists(updateNodeInfopath))
            return false;

        TPPS = new UpdateNodeInfo();
        TPPS.ParseNodeInfo(updateNodeInfopath);

        return true;
    }

    /// <summary>
    /// Applies a patch to an old PFS file and extracts it.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="outputFilePath"></param>
    /// <returns></returns>
    private bool ApplyFromPatchAndExtract(FileInfoKey key, string outputFilePath)
    {
        if (!TPPS.TryGetEntry(key.FileIndex, out NodeInfo info))
            return false;

        string oldFilePfsPath = PDIPFSPathResolver.GetPathFromSeed(info.CurrentEntryIndex);
        string oldFilePath = Path.Combine(this.BasePFSFolder, oldFilePfsPath);

        if (!File.Exists(oldFilePath))
            return false;

        byte[] oldFile = File.ReadAllBytes(oldFilePath);
        Volume.Keyset.CryptBytes(oldFile, oldFile, info.CurrentEntryIndex);

        if (info.NewFileInfoFlags == TPPSFileState.BinaryPatched)
            PS2ZIP.TryInflateInMemory(oldFile, key.UncompressedSize, out oldFile);

        string patchPfsPath = PDIPFSPathResolver.GetPathFromSeed(info.NewEntryIndex);
        string patchPath = Path.Combine(Volume.InputPath, patchPfsPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));
        using var outputFile = new FileStream(outputFilePath, FileMode.Create);

        using (var inflatedDataStream = new MemoryStream(oldFile))
            BsPatch.Patch(inflatedDataStream, outputFile, patchPath);

        outputFile.Position = 0;
        string hash = ComputeMD5OfFile(outputFile);
        if (hash == info.MD5Checksum)
            return true;
        else
            File.Delete(outputFilePath);

        return false;
    }

    private static string ComputeMD5OfFile(Stream input)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(input);

        StringBuilder result = new StringBuilder(hash.Length * 2);

        for (int i = 0; i < hash.Length; i++)
            result.Append(hash[i].ToString("x2"));

        return result.ToString();
    }
}
