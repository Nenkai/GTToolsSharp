using GTToolsSharp.BinaryPatching;
using GTToolsSharp.BTree;
using GTToolsSharp.Encryption;
using GTToolsSharp.Utils;
using GTToolsSharp.Volumes;

using PDTools.Compression;
using PDTools.Crypto;
using PDTools.Utils;

using SharpCompress.Common;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GTToolsSharp;

public class PFSVolumeUnpacker
{
    public GTVolumePFS MainPFS { get; }

    /// <summary>
    /// For TPPS
    /// </summary>
    public GTVolumePFS BaseVolumePFS { get; }
    public UpdateNodeInfo TPPS { get; set; }

    public string OutputDirectory { get; private set; }
    public bool NoUnpack { get; set; }
    public string BasePFSFolder { get; set; }

    public void SetOutputDirectory(string dirPath)
        => OutputDirectory = dirPath;

    public PFSVolumeUnpacker(GTVolumePFS baseVolOrPfs, GTVolumePFS baseVolume = null)
    {
        MainPFS = baseVolOrPfs;
        BaseVolumePFS = baseVolume;
    }

    private bool _hasInit = false;

    /// <summary>
    /// Unpacks all the files within the volume.
    /// </summary>
    /// <param name="fileIndexesToExtract">If non empty, specific file indexes to extract only</param>
    /// <param name="basePFSFolder">Base PDIPFS folder for binary patching</param>
    public void UnpackFiles(IEnumerable<int> fileIndexesToExtract, string basePFSFolder)
    {
        Init();

        fileIndexesToExtract ??= [];

        // Lazy way
        var files = MainPFS.BTree.GetAllRegisteredFiles();

        // Cache it
        Dictionary<uint, FileInfoKey> fileInfoKeys = [];
        foreach (var file in MainPFS.BTree.FileInfos.Entries)
            fileInfoKeys.Add(file.FileIndex, file);

        int numExtracted = 0;
        foreach (var file in files)
        {
            if (!fileIndexesToExtract.Any() || fileIndexesToExtract.Contains((int)file.Value.EntryIndex))
            {
                string volPath = file.Key;
                var fileInfo = fileInfoKeys[file.Value.EntryIndex];
                if (UnpackFile(fileInfo, volPath, Path.Combine(OutputDirectory, volPath)))
                {
                    numExtracted++;
                }
            }
        }

        Program.Log($"[:] Extracted {numExtracted} files.");
    }

    private void Init()
    {
        if (_hasInit)
            return;

        InitTPPS();
        _hasInit = true;
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
        Init();

        // Split Volumes
        if ((int)nodeKey.VolumeIndex != -1)
        {
            var volDevice = MainPFS.SplitVolumes[nodeKey.VolumeIndex];
            if (volDevice is null)
                return false;

            Program.Log($"[:] Unpacking '{entryPath}' from '{volDevice.Name}'..");

            return MainPFS.SplitVolumes[nodeKey.VolumeIndex].UnpackFile(nodeKey, MainPFS.Keyset, filePath);
        }
        else
        {
            if (!MainPFS.IsPatchVolume)
            {
                if (NoUnpack)
                    return false;

                return UnpackVolumeFile(MainPFS, nodeKey, filePath);
            }
            else
                return UnpackPFSFile(nodeKey, entryPath, filePath);
        }
    }

    private bool UnpackPFSFile(FileInfoKey nodeKey, string entryPath, string filePath)
    {
        string patchFilePath = PDIPFSPathResolver.GetPathFromSeed(nodeKey.FileIndex, MainPFS.IsGT5PDemoStyle);
        string localPath = MainPFS.PatchVolumeFolder + "/" + patchFilePath;

        if (NoUnpack)
            return false;

        /* I'm really not sure if there's a better way to do this.
        * Volume files, at least nodes don't seem to even store any special flag whether
        * it is located within an actual volume file or a patch volume. The only thing that is different is the sector index.. Sometimes node index when it's updated
        * It's slow, but somewhat works I guess..
        * */
        if (!File.Exists(localPath))
            return false;

        Program.Log($"[:] Unpacking: {patchFilePath} -> {entryPath} (from file info #{nodeKey.FileIndex})");
        using var fs = new FileStream(localPath, FileMode.Open);
        if (fs.Length >= 7)
        {
            Span<byte> magic = stackalloc byte[6];
            fs.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic).StartsWith("BSDIFF"))
            {
                fs.Dispose();
                return UnpackTPPSFile(nodeKey, entryPath, patchFilePath, filePath);
            }

            fs.Position = 0;
        }

        if (MainPFS.IsGT5PDemoStyle)
        {
            bool customCrypt = nodeKey.Flags.HasFlag(FileInfoFlags.CustomSalsaCrypt)
                || entryPath == "piece/car_thumb_M/gtr_07_01.img" // Uncompressed files, but no special flag either...
                || entryPath == "piece/car_thumb_M/impreza_wrx_sti_07_03.img";

            Salsa20 salsa = default;

            if (customCrypt)
            {
                if (MainPFS.Keyset.DecryptManager is null || !MainPFS.Keyset.DecryptManager.Keys.TryGetValue(entryPath, out string b64Key) || b64Key.Length < 32)
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
                        VolumeCrypto.DecryptOld(MainPFS.Keyset, fs, outCompressedFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);

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
                    VolumeCrypto.DecryptOld(MainPFS.Keyset, fs, outFile, nodeKey.FileIndex, nodeKey.CompressedSize, 0, salsa);
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
                if (!CryptoUtils.DecryptCheckCompression(fs, MainPFS.Keyset, nodeKey.FileIndex, nodeKey.UncompressedSize))
                {
                    Program.Log($"[X] Failed to decompress file {filePath} ({patchFilePath}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                fs.Position = 0;

                using var outputFile = File.Create(filePath);
                return CryptoUtils.DecryptAndInflateToFile(MainPFS.Keyset, fs, outputFile, nodeKey.UncompressedSize, nodeKey.FileIndex);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                CryptoUtils.CryptToFile(MainPFS.Keyset, fs, filePath, nodeKey.UncompressedSize, nodeKey.FileIndex);
            }
        }

        return true;
    }

    private bool UnpackVolumeFile(GTVolumePFS pfs, FileInfoKey nodeKey, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        using var fs = File.Create(filePath);

        return UnpackVolumeFile(pfs, nodeKey, fs, filePath);
    }

    private bool UnpackVolumeFile(GTVolumePFS pfs, FileInfoKey nodeKey, Stream outStream, string filePathForLog)
    {
        ulong offset = pfs.DataOffset + (ulong)nodeKey.SectorOffset * PFSBTree.SECTOR_SIZE;
        pfs.MainStream.Position = (long)offset;

        if (nodeKey.Flags.HasFlag(FileInfoFlags.Compressed))
        {
            if (!CryptoUtils.DecryptCheckCompression(pfs.MainStream, pfs.Keyset, nodeKey.FileIndex, nodeKey.UncompressedSize))
            {
                Program.Log($"[X] Failed to decompress file ({filePathForLog}) while unpacking file info key {nodeKey.FileIndex}", forceConsolePrint: true);
                return false;
            }


            pfs.MainStream.Position -= 8;
            CryptoUtils.DecryptAndInflateToFile(pfs.Keyset, pfs.MainStream, outStream, nodeKey.UncompressedSize,nodeKey.FileIndex, false);
        }
        else
        {
            CryptoUtils.CryptToFile(pfs.Keyset, pfs.MainStream, outStream, nodeKey.UncompressedSize, nodeKey.FileIndex, false);
        }

        return true;
    }


    private bool UnpackTPPSFile(FileInfoKey nodeKey, string filePath, string patchFilePath, string outputPath)
    {
        if (TPPS is null)
        {
            Program.Log($"[X] Detected BSDIFF file for {filePath} ({patchFilePath}) - could not initialize binary patcher.", forceConsolePrint: true);
            return false;
        }

        if (ApplyFromPatchAndExtract(nodeKey, filePath, outputPath))
        {
            Program.Log($"[/] Extracted binary patched file: {filePath} ({patchFilePath})", forceConsolePrint: true);
            return true;
        }

        return false;
    }

    private bool InitTPPS()
    {
        string updateNodeInfopath = Path.Combine(MainPFS.InputPath, "UPDATENODEINFO");
        if (!File.Exists(updateNodeInfopath))
            return false;

        Program.Log("[:] Loading node information (UPDATENODEINFO)...");
        TPPS = new UpdateNodeInfo();
        TPPS.ParseNodeInfo(updateNodeInfopath);
        Program.Log($"[/] Loaded UPDATENODEINFO with {TPPS.Entries.Count} nodes ({TPPS.Entries.Count(e => e.Value.NewFileInfoFlags == TPPSFileState.BinaryPatched)} are to be binary patched)");

        return true;
    }

    /// <summary>
    /// Applies a patch to an old PFS file and extracts it.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="outputFilePath"></param>
    /// <returns></returns>
    private bool ApplyFromPatchAndExtract(FileInfoKey key, string pfsFilePath, string outputFilePath)
    {
        if (!TPPS.TryGetEntry(key.FileIndex, out NodeInfo info))
            return false;

        if (string.IsNullOrWhiteSpace(this.BasePFSFolder))
        {
            Program.Log($"[X] Unable to binary patch file '{pfsFilePath}', no base pfs was provided. Provide --base-pfs with the path to the old PDIPFS (basically point to the PDIPFS of the game of the previous version).");
            return false;
        }

        string oldNodePfsPath = PDIPFSPathResolver.GetPathFromSeed(info.CurrentEntryIndex);
        string oldFilePath = Path.Combine(this.BasePFSFolder, oldNodePfsPath);

        byte[] oldFile;
        if (File.Exists(oldFilePath))
        {
            oldFile = File.ReadAllBytes(oldFilePath);
            MainPFS.Keyset.CryptBytes(oldFile, oldFile, info.CurrentEntryIndex);

            if (info.NewFileInfoFlags == TPPSFileState.BinaryPatched)
            {
                oldFile = PS2ZIP.Inflate(oldFile);
            }
        }
        else
        {
            if (BaseVolumePFS is not null)
            {
                var oldFileStream = new MemoryStream();

                Program.Log($"[:] Binary patched file '{pfsFilePath}' not found in patch file system, trying base volume...");
                FileInfoKey baseVolFileEntry = BaseVolumePFS.BTree.FileInfos.GetByFileIndex(info.CurrentEntryIndex);

                if (baseVolFileEntry is null)
                {
                    Program.Log($"[X] Unable to binary patch file '{pfsFilePath}' (oldIndex: {info.CurrentEntryIndex} [{oldNodePfsPath}], newIndex: {info.NewEntryIndex}) - not found in patch file system, but also not found in volume?");
                    return false;
                }

                if (!UnpackVolumeFile(BaseVolumePFS, baseVolFileEntry, oldFileStream, outputFilePath))
                {
                    Program.Log($"[X] Unable to binary patch file '{pfsFilePath}' (oldIndex: {info.CurrentEntryIndex} [{oldNodePfsPath}], newIndex: {info.NewEntryIndex}) - failed to extract from base volume for binary patching.");
                    return false;
                }

                oldFile = oldFileStream.ToArray();
            }
            else
            {
                Program.Log($"[X] Unable to binary patch file '{pfsFilePath}' (oldIndex: {info.CurrentEntryIndex} [{oldNodePfsPath}], newIndex: {info.NewEntryIndex}) - not found in patch file system. Provide the volume aswell (GT.VOL) with --base-vol.");
                return false;
            }
        }

        string oldPatchedNodePfsPath = PDIPFSPathResolver.GetPathFromSeed(info.NewEntryIndex);
        string patchPath = Path.Combine(MainPFS.InputPath, oldPatchedNodePfsPath);

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
