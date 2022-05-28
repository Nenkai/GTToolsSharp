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

namespace GTToolsSharp.Volumes
{
    /// <summary>
    /// Represents a container for all the files used within Gran Turismo games. GT5, 6, Sport.
    /// </summary>
    public class GTVolumePFS
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

        public PFSVolumeHeaderBase VolumeHeader { get; set; }
        public byte[] VolumeHeaderData { get; private set; }

        public PFSBTree BTree { get; set; }
        public FileDeviceVol[] SplitVolumes { get; set; }

        public uint DataOffset;
        public FileStream MainStream { get; }

        public GTVolumePFS(FileStream sourceStream, Endian endianness)
        {
            MainStream = sourceStream;
            Endian = endianness;
        }

        public GTVolumePFS(string patchVolumeFolder, Endian endianness)
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
        public static GTVolumePFS Load(Keyset keyset, string path, bool isPatchVolume, Endian endianness)
        {
            FileStream fs;
            GTVolumePFS vol;
            if (isPatchVolume)
            {
                if (File.Exists(Path.Combine(path, PDIPFSPathResolver.Default)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.Default), FileMode.Open);
                else if (File.Exists(Path.Combine(path, PDIPFSPathResolver.DefaultOld)))
                    fs = new FileStream(Path.Combine(path, PDIPFSPathResolver.DefaultOld), FileMode.Open);
                else
                    return null;

                vol = new GTVolumePFS(path, endianness);
            }
            else
            {
                fs = new FileStream(path, FileMode.Open);
                vol = new GTVolumePFS(fs, endianness);
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


            // Try GT5P if failed
            var headerType = PFSVolumeHeaderBase.Detect(tmp);
            if (headerType == PFSVolumeHeaderType.Unknown)
            {
                tmp = new byte[0x14];
                fs.Position = 0;
                fs.Read(tmp);

                vol.DecryptHeaderGT5PDemo(tmp);
                headerType = PFSVolumeHeaderBase.Detect(tmp);

                if (headerType == PFSVolumeHeaderType.PFS)
                    vol.SetKeyset(KeysetStore.Keyset_GT5P_JP_DEMO);
            }

            if (headerType == PFSVolumeHeaderType.Unknown)
            {
                fs?.Dispose();
                return null;
            }

            fs.Position = 0;
            vol.InputPath = path;
            vol.VolumeHeader = PFSVolumeHeaderBase.Load(fs, vol, headerType, out byte[] headerBytes);
            vol.VolumeHeaderData = headerBytes;
            vol.IsGT5PDemoStyle = headerType == PFSVolumeHeaderType.PFS;

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            vol.VolumeHeader.PrintInfo();

            Program.Log("[-] Reading table of contents.", true);

            if (!vol.DecryptTOC())
            {
                fs?.Dispose();
                return null;
            }

            if (!vol.BTree.Load())
            {
                fs?.Dispose();
                return null;
            }

            vol.BTree.PrintOffsetInfo();

            if (Program.SaveTOC)
                File.WriteAllBytes("VolumeTOC.bin", vol.BTree.Data);
            

            vol.LoadSplitVolumesIfNeeded();

            return vol;
        }

        public void LoadSplitVolumesIfNeeded()
        {
            if (VolumeHeader is FileDeviceGTFS3Header header3)
            {
                string inputDir = Path.GetDirectoryName(InputPath);
                SplitVolumes = new FileDeviceVol[header3.VolList.Length];

                for (int i = 0; i < header3.VolList.Length; i++)
                {
                    FileDeviceGTFS3Header.VolEntryGTFS3 volEntry = header3.VolList[i];
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
        public bool DecryptHeaderGT5PDemo(Span<byte> headerData)
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

                BTree = new PFSBTree(VolumeHeader, this);
                BTree.Location = path;
                BTree.Data = deflatedData;
            }
            else
            {
                MainStream.Seek(PFSBTree.SECTOR_SIZE, SeekOrigin.Begin);

                var br = new BinaryReader(MainStream);
                byte[] data = br.ReadBytes((int)VolumeHeader.CompressedTOCSize);

                Program.Log($"[-] TOC Entry Index is {VolumeHeader.ToCNodeIndex} (Offset: 0x{PFSBTree.SECTOR_SIZE:X8})", true);

                // Decrypt it with the seed that the main header gave us
                CryptoUtils.CryptBuffer(Keyset, data, data, VolumeHeader.ToCNodeIndex);

                Program.Log($"[-] Decompressing TOC within volume.. (Offset: 0x{PFSBTree.SECTOR_SIZE:X8})", true);
                if (!PS2ZIP.TryInflateInMemory(data, VolumeHeader.ExpandedTOCSize, out byte[] deflatedData))
                    return false;

                BTree = new PFSBTree(VolumeHeader, this);
                BTree.Data = deflatedData;

                DataOffset = MiscUtils.AlignValue(PFSBTree.SECTOR_SIZE + VolumeHeader.CompressedTOCSize, PFSBTree.SECTOR_SIZE);
            }

            return true;
        }
    }
}
