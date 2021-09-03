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

using PDTools.Compression;

namespace GTToolsSharp
{
    public class VolumeUnpacker
    {
        public GTVolume Volume { get; }
        public PDIBinaryPatcher TPPS { get; set; }

        public string OutputDirectory { get; private set; }
        public bool NoUnpack { get; set; }
        public string BasePFSFolder { get; set; }

        public void SetOutputDirectory(string dirPath)
            => OutputDirectory = dirPath;

        public VolumeUnpacker(GTVolume vol)
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
            if (fileIndexesToExtract is null)
                fileIndexesToExtract = Enumerable.Empty<int>();

            // Lazy way
            var files = Volume.TableOfContents.GetAllRegisteredFileMap();

            // Cache it
            Dictionary<uint, FileInfoKey> fileInfoKeys = new Dictionary<uint, FileInfoKey>();
            foreach (var file in Volume.TableOfContents.FileInfos.Entries)
                fileInfoKeys.Add(file.FileIndex, file);

            foreach (var file in files)
            {
                if (!fileIndexesToExtract.Any() || fileIndexesToExtract.Contains((int)file.Value.EntryIndex))
                {
                    string volPath = file.Key;
                    var fileInfo = fileInfoKeys[file.Value.EntryIndex];
                    UnpackFile(fileInfo, volPath, Path.Combine(OutputDirectory, volPath), basePFSFolder);
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
        public bool UnpackFile(FileInfoKey nodeKey, string entryPath, string filePath, string basePFSFolder)
        {
            // Split Volumes
            if ((int)nodeKey.VolumeIndex != -1)
            {
                var volDevice = Volume.SplitVolumes[nodeKey.VolumeIndex];
                if (volDevice is null)
                    return false;

                Program.Log($"[:] Unpacking '{entryPath}' from '{volDevice.Name}'..");

                return Volume.SplitVolumes[nodeKey.VolumeIndex].UnpackFile(nodeKey, Volume.Keyset, entryPath);
            }
            else
            {
                ulong offset = Volume.DataOffset + (ulong)nodeKey.SectorOffset * GTVolumeTOC.SECTOR_SIZE;
                if (!Volume.IsPatchVolume)
                {
                    if (NoUnpack)
                        return false;

                    return UnpackVolumeFile(nodeKey, filePath, offset);
                }
                else
                    return UnpackPFSFile(nodeKey, entryPath, filePath, basePFSFolder);
            }
        }

        private bool UnpackPFSFile(FileInfoKey nodeKey, string entryPath, string filePath, string basePFSFolder)
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
                fs.Read(magic);
                if (Encoding.ASCII.GetString(magic).StartsWith("BSDIFF"))
                {
                    if (TPPS is null && !InitTPPS())
                    {
                        Program.Log($"[X] Detected BSDIFF file for {filePath} ({patchFilePath}), can not unpack yet. (fileID {nodeKey.FileIndex})", forceConsolePrint: true);
                        return false;
                    }

                    fs.Dispose();
                    ApplyPatch(nodeKey);

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
                        using (var outFile = new FileStream(filePath, FileMode.Create))
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

        private bool InitTPPS()
        {
            if (TPPS?.Entries?.Count > 0)
                return true;

            string updateNodeInfopath = Path.Combine(Volume.InputPath, "UPDATENODEINFO");
            if (!File.Exists(updateNodeInfopath))
                return false;

            TPPS = new PDIBinaryPatcher();
            TPPS.ParseNodeInfo(updateNodeInfopath);

            return true;
        }

        private void ApplyPatch(FileInfoKey key)
        {
            if (!TPPS.TryGetEntry(key.FileIndex, out NodeInfo info))
                return;

            string oldFilePfsPath = PDIPFSPathResolver.GetPathFromSeed(info.CurrentEntryIndex);
            string oldFilePath = Path.Combine(this.BasePFSFolder, oldFilePfsPath);
            byte[] oldFile = File.ReadAllBytes(oldFilePath);
            Volume.Keyset.CryptBytes(oldFile, oldFile, info.CurrentEntryIndex);
            PS2ZIP.InflateInMemory(oldFile, out byte[] inflatedData);

            string patchPfsPath = PDIPFSPathResolver.GetPathFromSeed(info.NewEntryIndex);
            string patchPath = Path.Combine(Volume.InputPath, patchPfsPath);

            using var outputFile = new FileStream("test", FileMode.Create);
            BsPatch.Patch(new MemoryStream(inflatedData), outputFile, patchPath);

            /*
            using var md5 = MD5.Create();
            md5.ComputeHash(outputFile);
            */
        }
    }
}
