using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Buffers;
using System.Security.Cryptography;

using Syroot.BinaryData.Core;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Encryption;
using GTToolsSharp.Headers;

using PDTools.Utils;
using PDTools.Compression;

namespace GTToolsSharp
{
    /// <summary>
    /// Represents a container for all the files used within Gran Turismo games.
    /// </summary>
    public class GTVolume
    {
        public const int BASE_VOLUME_ENTRY_INDEX = 1;
        
        /// <summary>
        /// Keyset used to decrypt and encrypt volume files.
        /// </summary>
        public Keyset Keyset { get; private set; }

        public readonly Endian Endian;

        public string InputPath { get; set; }
        public bool IsPatchVolume { get; set; }
        public string PatchVolumeFolder { get; set; }
        public bool NoCompress { get; set; }
        public bool IsGT5PDemoStyle { get; set; }

        public VolumeHeaderBase VolumeHeader { get; set; }
        public byte[] VolumeHeaderData { get; private set; }

        public GTVolumeTOC TableOfContents { get; set; }
        public FileDeviceVol[] SplitVolumes { get; set; }

        public uint DataOffset;
        public FileStream MainStream { get; }

        public GTVolume(FileStream sourceStream, Endian endianness)
        {
            MainStream = sourceStream;
            Endian = endianness;
        }

        public GTVolume(string patchVolumeFolder, Endian endianness)
        {
            PatchVolumeFolder = patchVolumeFolder;
            IsPatchVolume = true;

            Endian = endianness;
        }

        /// <summary>
        /// Loads a volume file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isPatchVolume"></param>
        /// <param name="endianness"></param>
        /// <returns></returns>
        public static GTVolume Load(Keyset keyset, string path, bool isPatchVolume, Endian endianness)
        {
            FileStream fs;
            GTVolume vol;
            if (isPatchVolume)
            {
                if (File.Exists(Path.Combine(path, PDIPFSPathResolver.Default)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.Default), FileMode.Open);
                else if (File.Exists(Path.Combine(path, PDIPFSPathResolver.DefaultOld)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.DefaultOld), FileMode.Open);
                else
                    return null;

                vol = new GTVolume(path, endianness);
            }
            else
            {
                fs = new FileStream(path, FileMode.Open);
                vol = new GTVolume(fs, endianness);
            }

            vol.SetKeyset(keyset);

            byte[] headerMagic = new byte[4];
            fs.Read(headerMagic);

            Span<byte> tmp = new byte[4];
            headerMagic.AsSpan().CopyTo(tmp);

            if (!vol.DecryptHeader(tmp, BASE_VOLUME_ENTRY_INDEX))
            {
                fs?.Dispose();
                return null;
            }

            // Try old if failed
            var headerType = VolumeHeaderBase.Detect(tmp);
            if (headerType == VolumeHeaderType.Unknown)
            {
                tmp = new byte[0x14];
                fs.Position = 0;
                fs.Read(tmp);

                if (vol.DecryptHeaderOld(tmp))
                    headerType = VolumeHeaderBase.Detect(tmp);

                if (headerType == VolumeHeaderType.PFS)
                    vol.SetKeyset(KeysetStore.Keyset_GT5P_JP_DEMO);
            }

            if (headerType == VolumeHeaderType.Unknown)
            {
                fs?.Dispose();
                return null;
            }

            fs.Position = 0;
            vol.InputPath = path;
            vol.VolumeHeader = VolumeHeaderBase.Load(fs, vol, headerType, out byte[] headerBytes);
            vol.VolumeHeaderData = headerBytes;
            vol.IsGT5PDemoStyle = headerType == VolumeHeaderType.PFS;

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            Program.Log($"[>] PFS Version/Serial No: '{vol.VolumeHeader.SerialNumber}'");
            Program.Log($"[>] Table of Contents Entry Index: {vol.VolumeHeader.ToCNodeIndex}");
            Program.Log($"[>] TOC Size: 0x{vol.VolumeHeader.CompressedTOCSize:X8} bytes (0x{vol.VolumeHeader.ExpandedTOCSize:X8} expanded)");
            if (vol.VolumeHeader is FileDeviceGTFS2Header header2)
            {
                Program.Log($"[>] Total Volume Size: {MiscUtils.BytesToString((long)header2.TotalVolumeSize)}");
                Program.Log($"[>] Title ID: '{header2.TitleID}'");
            }

            Program.Log("[-] Reading table of contents.", true);

            if (!vol.DecryptTOC())
            {
                fs?.Dispose();
                return null;
            }

            if (!vol.TableOfContents.Load())
            {
                fs?.Dispose();
                return null;
            }

            Program.Log($"[>] File names tree offset: 0x{vol.TableOfContents.NameTreeOffset:X8}", true);
            Program.Log($"[>] File extensions tree offset: 0x{vol.TableOfContents.FileExtensionTreeOffset:X8}", true);
            Program.Log($"[>] Node tree offset: 0x{vol.TableOfContents.NodeTreeOffset:X8}", true);
            Program.Log($"[>] Entry count: {vol.TableOfContents.RootAndFolderOffsets.Count}.", true);

            if (Program.SaveTOC)
                File.WriteAllBytes("VolumeTOC.bin", vol.TableOfContents.Data);

            vol.LoadSplitVolumesIfNeeded();

            return vol;
        }

        public void LoadSplitVolumesIfNeeded()
        {
            if (VolumeHeader is not FileDeviceGTFS3Header header3)
                return;

            string inputDir = Path.GetDirectoryName(InputPath);
            SplitVolumes = new FileDeviceVol[header3.VolList.Length];

            for (int i = 0; i < header3.VolList.Length; i++)
            {
                FileDeviceGTFS3Header.VolEntry volEntry = header3.VolList[i];
                string localPath = Path.Combine(inputDir, volEntry.Name);
                if (!File.Exists(localPath))
                {
                    Console.WriteLine($"[!] Linked volume file '{volEntry.Name}' not found, will be skipped");
                    continue;
                }

                var vol = FileDeviceVol.Read(localPath);
                if (vol is null)
                {
                    Console.WriteLine($"[!] Unable to read vol file '{localPath}'.");
                    continue;
                }

                vol.Name = volEntry.Name;
                SplitVolumes[i] = vol;
            }
        }

        private void SetKeyset(Keyset keyset)
            => Keyset = keyset;

        /// <summary>
        /// Decrypts the header of the main volume file, using a provided seed.
        /// </summary>
        /// <param name="headerData"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public bool DecryptHeader(Span<byte> headerData, uint seed)
        {
            if (Keyset.Key.Data is null || Keyset.Key.Data.Length < 4)
                return false;

            CryptoUtils.CryptBuffer(Keyset, headerData, headerData, seed);

            Span<uint> blocks = MemoryMarshal.Cast<byte, uint>(headerData);
            Keyset.DecryptBlocks(blocks, blocks);
            return true;
        }

        /// <summary>
        /// Decrypts the header of the main volume file, using a provided seed. GT5P JP Demo version.
        /// </summary>
        /// <param name="headerData"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public bool DecryptHeaderOld(Span<byte> headerData)
        {
            if (headerData.Length != 0x14)
                return false;

            GT5POldCrypto.DecryptPass(1, headerData, headerData, 0x14);

            byte[] outdata = new byte[0x14];
            GT5POldCrypto.DecryptHeaderSpecific(outdata, headerData);
            outdata.CopyTo(headerData);
            return true;
        }

        private bool DecryptTOC()
        {
            if (VolumeHeader is null)
                throw new InvalidOperationException("Header was not yet loaded");

            if (IsPatchVolume)
            {
                string path = PDIPFSPathResolver.GetPathFromSeed(VolumeHeader.ToCNodeIndex, IsGT5PDemoStyle);

                string localPath = Path.Combine(this.PatchVolumeFolder, path);
                Program.Log($"[!] Volume Patch Path Table of contents located at: {localPath}", true);
                if (!File.Exists(localPath))
                {
                    Program.Log($"[X] Error: Unable to locate PDIPFS main TOC file on local filesystem. ({path})", true);
                    return false;
                }

                using var fs = new FileStream(localPath, FileMode.Open);
                byte[] data = new byte[VolumeHeader.CompressedTOCSize];
                fs.Read(data);

                // Accessing a new file, we need to decrypt the header again
                Program.Log($"[-] TOC Entry is {VolumeHeader.ToCNodeIndex} which is at {path} - decrypting it", true);
                if (!IsGT5PDemoStyle)
                    CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.ToCNodeIndex);
                else
                    GT5POldCrypto.DecryptPass(VolumeHeader.ToCNodeIndex, data, data, (int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] Decompressing TOC file..", true);
                if (!PS2ZIP.TryInflateInMemory(data, VolumeHeader.ExpandedTOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Location = path;
                TableOfContents.Data = deflatedData;
            }
            else
            {
                MainStream.Seek(GTVolumeTOC.SECTOR_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(MainStream);
                byte[] data = br.ReadBytes((int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] TOC Entry Index is {VolumeHeader.ToCNodeIndex} (Offset: 0x{GTVolumeTOC.SECTOR_SIZE:X8})", true);

                // Decrypt it with the seed that the main header gave us
                CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.ToCNodeIndex);

                Program.Log($"[-] Decompressing TOC within volume.. (Offset: 0x{GTVolumeTOC.SECTOR_SIZE:X8})", true);
                if (!PS2ZIP.TryInflateInMemory(data, VolumeHeader.ExpandedTOCSize, out byte[] deflatedData))
                    return false;

                TableOfContents = new GTVolumeTOC(VolumeHeader, this);
                TableOfContents.Data = deflatedData;

                DataOffset = MiscUtils.AlignValue(GTVolumeTOC.SECTOR_SIZE + VolumeHeader.CompressedTOCSize, GTVolumeTOC.SECTOR_SIZE);
            }

            return true;
        }


        public static string lastEntryPath = "";
        public string GetEntryPath(FileEntryKey key, string prefix)
        {
            string entryPath = prefix;
            StringBTree nameBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.NameTreeOffset), TableOfContents);

            if (nameBTree.TryFindIndex(key.NameIndex, out StringKey nameKey))
                entryPath += nameKey.Value;

            if (key.Flags.HasFlag(EntryKeyFlags.File))
            {
                // If it's a file, find the extension aswell
                StringBTree extBTree = new StringBTree(TableOfContents.Data.AsMemory((int)TableOfContents.FileExtensionTreeOffset), TableOfContents);

                if (extBTree.TryFindIndex(key.FileExtensionIndex, out StringKey extKey) && !string.IsNullOrEmpty(extKey.Value))
                    entryPath += extKey.Value;

            }
            else if (key.Flags.HasFlag(EntryKeyFlags.Directory))
                entryPath += '/';

            lastEntryPath = entryPath;

            return entryPath;
        }

    }
}
