using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Buffers;
using System.Security.Cryptography;
using System.Buffers.Binary;
using Syroot.BinaryData.Core;

using GTToolsSharp.BTree;
using GTToolsSharp.Utils;
using GTToolsSharp.Encryption;
using GTToolsSharp.Headers;

using PDTools.Utils;
using PDTools.Compression;
using PDTools.Crypto;

namespace GTToolsSharp.Volumes
{
    /// <summary>
    /// GT7 Volume based on Minimal Perfect Hash.
    /// </summary>
    public class GTVolumeMPH
    {
        public string InputPath { get; set; }
        public bool NoCompress { get; set; }

        public byte[] VolumeHeaderData { get; private set; }

        public ClusterVolume[] SplitVolumes { get; set; }
        public FileStream MainStream { get; }

        public MPHVolumeHeaderBase VolumeHeader { get; set; }

        public GTVolumeMPH(FileStream sourceStream)
        {
            MainStream = sourceStream;
        }

        /// <summary>
        /// Loads a volume file.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isPatchVolume"></param>
        /// <param name="endianness"></param>
        /// <returns></returns>
        public static GTVolumeMPH Load(string path)
        {
            FileStream fs = new FileStream(path, FileMode.Open);
            GTVolumeMPH vol = new GTVolumeMPH(fs);

            byte[] headerMagic = new byte[4];
            fs.Read(headerMagic);

            Span<byte> tmp = new byte[4];
            headerMagic.AsSpan().CopyTo(tmp);

            var headerType = MPHVolumeHeaderBase.Detect(tmp);
            if (headerType == MPHVolumeHeaderType.Unknown)
            {
                var tmpData = new byte[fs.Length];
                fs.Position = 0;
                fs.Read(tmpData);
                vol.DecryptHeader(tmpData);

                headerType = MPHVolumeHeaderBase.Detect(tmpData);
            }

            if (headerType == MPHVolumeHeaderType.Unknown)
            {
                fs?.Dispose();
                return null;
            }

            fs.Position = 0;
            vol.InputPath = path;
            vol.VolumeHeader = MPHVolumeHeaderBase.Load(fs, vol, headerType, out byte[] headerBytes);
            vol.VolumeHeaderData = headerBytes;

            if (Program.SaveHeader)
                File.WriteAllBytes("VolumeHeader.bin", vol.VolumeHeaderData);

            vol.VolumeHeader.PrintInfo();
            vol.LoadSplitVolumesIfNeeded();

            return vol;
        }

        public void Unpack(IEnumerable<string> files)
        {
            var gtsFiles = File.ReadAllLines(@"volume_entries.txt");
            var h = VolumeHeader as FileDeviceMPHSuperintendentHeader;

            SortedDictionary<string, MPHNodeInfo> gameFiles = BruteforceFind();


            int i = 0;
            for (int i1 = 0; i1 < h.Nodes.Length; i1++)
            {
                MPHNodeInfo file = h.Nodes[i1];
                var vol = SplitVolumes[file.VolumeIndex];
                if (vol is null)
                {
                    Program.Log($"Volume index {i1} is not loaded, skipping node");
                    continue;
                }

                if (gameFiles.ContainsValue(file))
                {
                    var name = gameFiles.FirstOrDefault(e => e.Value == file).Key;
                    vol.UnpackFile(file, $@"Unpack\{name}", -1);
                }
                else
                {
                    vol.UnpackFile(file, $@"Unpack\output\vol{file.VolumeIndex}\tmp", i1);
                }

                i++;

            }
        }


        public SortedDictionary<string, MPHNodeInfo> BruteforceFind()
        {
            var gtsFiles = File.ReadAllLines(@"volume_entries.txt");

            SortedDictionary<string, MPHNodeInfo> validFiles = new SortedDictionary<string, MPHNodeInfo>();

            foreach (var p in gtsFiles)
            {
                var lower = p.ToLower();

                bool found = CheckFile(validFiles, lower);

                if (lower.Contains("gt7sp"))
                    lower = lower.Replace("gt7sp", "gt7");

                CheckFile(validFiles, lower);

                if (lower.Contains(".adc"))
                   lower = lower.Replace(".adc", ".mpackage");

                 CheckFile(validFiles, lower);
            }

            // Check game parameters
            for (var i = 1000000; i < 1010000; i++)
                CheckFile(validFiles, $"game_parameter/gp/{i}.json");
            
            // Car Models
            for (var make = 0; make < 400; make++)
            {
                for (var id = 0; id < 400; id++)
                {
                    string carPath = $"car/{make.ToString().PadLeft(4, '0')}/{id.ToString().PadLeft(4, '0')}";
                    CheckFile(validFiles, carPath + "/meter");
                    CheckFile(validFiles, carPath + "/interior");
                    CheckFile(validFiles, carPath + "/meter.sepdat");
                    CheckFile(validFiles, carPath + "/interior.sepdat");
                    CheckFile(validFiles, carPath + "/race/body");
                    CheckFile(validFiles, carPath + "/hq/body");
                    CheckFile(validFiles, carPath + "/race/body.sepdat");
                    CheckFile(validFiles, carPath + "/hq/body.sepdat");
                    CheckFile(validFiles, carPath + "/race/wheel");
                    CheckFile(validFiles, carPath + "/hq/wheel");
                    CheckFile(validFiles, carPath + "/race/brakedisk");
                    CheckFile(validFiles, carPath + "/hq/brakedisk");
                    CheckFile(validFiles, carPath + "/race/caliper");
                    CheckFile(validFiles, carPath + "/hq/caliper");
                    CheckFile(validFiles, carPath + "/info");
                    CheckFile(validFiles, carPath + "/tirehouse_sdf_normal");
                    CheckFile(validFiles, carPath + "/tirehouse_sdf_wide");

                }
            }

            // Tracks
            CheckFile(validFiles, "crs/gadgets");

            for (var course_id = 0; course_id < 500; course_id++)
            {
                for (var level = 0; level < 10; level++)
                {
                    string crsPath = $"crs/c{course_id.ToString().PadLeft(3, '0')}";
                    CheckFile(validFiles, crsPath + "/pack");
                    CheckFile(validFiles, crsPath + "/pack_s");
                    CheckFile(validFiles, crsPath + "/maxsizes");
                    CheckFile(validFiles, crsPath + "/pack");
                    CheckFile(validFiles, crsPath + "/probe_gbuffers_common.img");

                    CheckFile(validFiles, $"crs/c{course_id.ToString().PadLeft(3, '0')}.shapestream");
                    CheckFile(validFiles, $"crs/c{course_id.ToString().PadLeft(3, '0')}.texstream");

                    //CheckFile(txt, validFiles, crsPath + "/probe_gbuffers_common.img");

                    string levelPath = crsPath += $"/l{level.ToString().PadLeft(2, '0')}";

                    CheckFile(validFiles, levelPath + "/course_map");
                    CheckFile(validFiles, levelPath + "/course_offset");
                    CheckFile(validFiles, levelPath + "/parking");
                    CheckFile(validFiles, levelPath + "/replay_offset");
                    CheckFile(validFiles, levelPath + "/spots.json");
                    CheckFile(validFiles, levelPath + "/road_condition_map");

                    CheckFile(validFiles, levelPath + "/auto_drive");
                    CheckFile(validFiles, levelPath + "/course_line");
                    CheckFile(validFiles, levelPath + "/driving_marker");
                    CheckFile(validFiles, levelPath + "/enemy_spline");
                    CheckFile(validFiles, levelPath + "/expert_line");
                    CheckFile(validFiles, levelPath + "/gadget_layout");
                    CheckFile(validFiles, levelPath + "/pack");
                    CheckFile(validFiles, levelPath + "/penalty_line");
                    CheckFile(validFiles, levelPath + "/replay");
                    CheckFile(validFiles, levelPath + "/runway");
                }

                for (var level = 0; level < 10; level++)
                {
                    string crsPath = $"crs/p{course_id.ToString().PadLeft(3, '0')}";
                    CheckFile(validFiles, crsPath + "/pack");
                    CheckFile(validFiles, crsPath + "/pack_s");
                    CheckFile(validFiles, crsPath + "/maxsizes");
                    CheckFile(validFiles, crsPath + "/pack");
                    CheckFile(validFiles, crsPath + "/probe_gbuffers_common.img");

                    CheckFile(validFiles, $"crs/c{course_id.ToString().PadLeft(3, '0')}.shapestream");
                    CheckFile(validFiles, $"crs/c{course_id.ToString().PadLeft(3, '0')}.texstream");

                    //CheckFile(txt, validFiles, crsPath + "/probe_gbuffers_common.img");

                    string levelPath = crsPath += $"/l{level.ToString().PadLeft(2, '0')}";

                    CheckFile(validFiles, levelPath + "/course_map");
                    CheckFile(validFiles, levelPath + "/course_offset");
                    CheckFile(validFiles, levelPath + "/parking");
                    CheckFile(validFiles, levelPath + "/replay_offset");
                    CheckFile(validFiles, levelPath + "/spots.json");
                    CheckFile(validFiles, levelPath + "/road_condition_map");
                    CheckFile(validFiles, levelPath + "/road_condition_map");

                    CheckFile(validFiles, levelPath + "/auto_drive");
                    CheckFile(validFiles, levelPath + "/course_line");
                    CheckFile(validFiles, levelPath + "/driving_marker");
                    CheckFile(validFiles, levelPath + "/enemy_spline");
                    CheckFile(validFiles, levelPath + "/expert_line");
                    CheckFile(validFiles, levelPath + "/gadget_layout");
                    CheckFile(validFiles, levelPath + "/pack");
                    CheckFile(validFiles, levelPath + "/penalty_line");
                    CheckFile(validFiles, levelPath + "/replay");
                    CheckFile(validFiles, levelPath + "/runway");
                }
            }

            // Decals
            for (var i = 0; i < 10000; i++)
            {
                string p = $"livery/decal";

                CheckFile(validFiles, p + $"/img/{i}.jpg");
                CheckFile(validFiles, p + $"/img/{i}.svg");

                CheckFile(validFiles, p + $"/txs/{i}.jpg");
            }

            // Scapes
            for (var i = 0; i < 500; i++)
            {
                string p = $"scapes/{i}";

                CheckFile(validFiles, p + $"/script_ext.txt");
                CheckFile(validFiles, p + $"/diffuse_env.txs");

                CheckFile(validFiles, p + $"/serialize.ssb");

                for (var j = 0; j < 100; j++)
                {
                    CheckFile(validFiles, p + $"/{j}.jpg");
                    CheckFile(validFiles, p + $"/menu_{j}.jpg");
                }
            }

            // Sndz
            for (var i = 40000; i < 80000; i++)
            {
                CheckFile(validFiles, $"carsound/aes/{i}");

            }

            for (var i = 40000; i < 80000; i++)
            {
                CheckFile(validFiles, $"carsound/aes/{i}");
                CheckFile(validFiles, $"carsound/gtes2/{i}/{i}.gtesd1");
            }

            // Rtext
            string[] countries = new[] { "BP", "CN", "CZ", "DK", "DE", "EL", "ES", "FI", "FR", "GB", "HU", "IT", "JP", "KR", "MS", "NO", "NL", "PL", "PT", "RU", "SE", "TR", "TW", "US" };
            foreach (var c in countries)
            {
                
            }

            CheckFile(validFiles, "sound_gt/etc/files.json");
            CheckFile(validFiles, "sound_gt/etc/gt7sys.json");
            CheckFile(validFiles, "sound_gt/se/gt7_carsfx.szd");
            CheckFile(validFiles, "sound_gt/se/gt7_race.szd");
            CheckFile(validFiles, "sound_gt/se/gt7_tire.szd");
            CheckFile(validFiles, "sound_gt/se/gt7_collision.szd");

            CheckFile(validFiles, "sound_gt/etc/gt7_buss.szd");
            CheckFile(validFiles, "sound_gt/library/music.dat");

            return validFiles;
        }

        private bool CheckFile(SortedDictionary<string, MPHNodeInfo> validFiles, string path)
        {
            var node = (VolumeHeader as FileDeviceMPHSuperintendentHeader).GetNodeByPath(path);
            if (node != null)
            {
                Program.Log($"OK: {path} (0x{node.EntryHash:X8})");
                validFiles.TryAdd(path, node);
                return true;
            }

            return false;
        }

        public void LoadSplitVolumesIfNeeded()
        {
            string inputDir = Path.GetDirectoryName(InputPath);
            SplitVolumes = new ClusterVolume[VolumeHeader.VolumeInfo.Length];

            for (int i = 0; i < VolumeHeader.VolumeInfo.Length; i++)
            {
                ClusterVolumeInfoMPH volEntry = VolumeHeader.VolumeInfo[i];
                string localPath = Path.Combine(inputDir, volEntry.FileName);
                if (!File.Exists(localPath))
                {
                    Console.WriteLine($"[!] Linked volume file '{volEntry.FileName}' not found, will be skipped");
                    continue;
                }

                var vol = ClusterVolume.Read(localPath);
                if (vol is null)
                {
                    Console.WriteLine($"[!] Unable to read vol file '{localPath}'.");
                    continue;
                }

                vol.Name = volEntry.FileName;
                SplitVolumes[i] = vol;
            }
        }

        public bool DecryptHeader(Span<byte> data)
        {
            // Decrypt whole file
            ChaCha20 chacha20 = new ChaCha20(KeysetStore.GT7_Index_Key, KeysetStore.GT7_Index_IV, 0);

            // Love when libs don't support span
            chacha20.DecryptBytes(data, data.Length, 0);

            const uint BASE_CRC = 0xA7E4A1E8;
            uint crc = BASE_CRC;

            var ints = MemoryMarshal.Cast<byte, uint>(data);

            // Main header has another layer
            for (var i = 0; i < 0x800 / 4; i++)
            {
                uint value = ints[i];

                uint a = (byte)(crc >> 24);
                uint b = (byte)(crc >> 16);
                uint c = (byte)(crc >> 8);
                uint d = (byte)(crc >> 0);


                crc = value;

                value = PDTools.Crypto.CRC32.checksum_0x04C11DB7[a];
                value = value << 8 ^ PDTools.Crypto.CRC32.checksum_0x04C11DB7[b ^ (value >> 24)];
                value = value << 8 ^ PDTools.Crypto.CRC32.checksum_0x04C11DB7[c ^ (value >> 24)];
                value = value << 8 ^ PDTools.Crypto.CRC32.checksum_0x04C11DB7[d ^ (value >> 24)];
                value = ~(value ^ crc);

                ints[i] = value;
            }

            return true;
        }
    }
}
