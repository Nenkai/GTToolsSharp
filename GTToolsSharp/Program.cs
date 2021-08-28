using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using CommandLine;

using GTToolsSharp.Headers;
using GTToolsSharp.Encryption;
using GTToolsSharp.Utils;
using GTToolsSharp.PackedFileInstaller;

using PDTools.Compression;
using PDTools.Utils;

namespace GTToolsSharp
{
    class Program
    {
        private static StreamWriter sw;
        public static bool PrintToConsole = true;
        public static bool SaveHeader = false;
        public static bool SaveTOC = false;

        public const string Version = "3.0.1";

        static void Main(string[] args)
        {
            Console.WriteLine($"-- GTToolsSharp {Version} - (c) Nenkai#9075, ported from flatz's gttool --");
            Console.WriteLine();
            
            Parser.Default.ParseArguments<PackVerbs, UnpackVerbs, UnpackInstallerVerbs, CryptVerbs, ListVerbs, CompressVerbs>(args)
                .WithParsed<PackVerbs>(Pack)
                .WithParsed<UnpackVerbs>(Unpack)
                .WithParsed<UnpackInstallerVerbs>(UnpackInstaller)
                .WithParsed<CryptVerbs>(Crypt)
                .WithParsed<ListVerbs>(List)
                .WithParsed<CompressVerbs>(Compress)
                .WithNotParsed(HandleNotParsedArgs);

            Program.Log("Exiting.");
            sw?.Dispose();
        }

        public static void Pack(PackVerbs options)
        {
            bool isFile = File.Exists(options.InputPath);
            bool isDir = false;
            if (!isFile)
            {
                isDir = Directory.Exists(options.InputPath);
                if (!isDir)
                {
                    Console.WriteLine($"[X] Volume file or PDIPFS folder \"{options.InputPath}\" does not exist.");
                    return;
                }
            }

            if (File.Exists(options.FolderToRepack))
            {
                Program.Log("[X] No, don't put a specific file to repack. Use the whole folder containing files in their proper volume folder." +
                    "Example: Your input folder is GT5, inside is a file like textdata/gt5/somefile.xml, just use '-p GT5'. ");
                return;
            }
            else if (!Directory.Exists(options.FolderToRepack))
            {
                Program.Log("[X] Folder to pack directory does not exist, create it first and put the files to pack inside accordingly with their proper game path.");
                return;
            }

            Keyset[] keyset = CheckKeys();
            if (keyset is null)
                return;

            if (!string.IsNullOrEmpty(options.LogPath))
                sw = new StreamWriter(options.LogPath);

            if (!string.IsNullOrEmpty(options.CustomGameID) && options.CustomGameID.Length > 128)
            {
                Console.WriteLine($"[X] Custom Game ID must not be above 128 characters.");
                return;
            }

            bool found = false;
            GTVolume vol = null;
            foreach (var k in keyset)
            {
                vol = GTVolume.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Big);
                if (vol != null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.WriteLine($"[X] Could not decrypt volume to read information for packing. Make sure that you have a valid game key/seed in your key.json.");
                return;
            }

            PrintToConsole = true;

            if (options.Cache)
                Program.Log("[!] Using packing cache. (--cache)");
            vol.UsePackingCache = options.Cache;

            Program.Log("[-] Started packing process.");

            List<string> filesToRemove = new List<string>();
            if (options.PackRemoveFiles)
            {
                if (!File.Exists("files_to_remove.txt"))
                {
                    Console.WriteLine($"[X] --remove-files was provided but 'files_to_remove.txt' file does not exist.");
                    return;
                }
                filesToRemove = ReadEntriesFromFile("files_to_remove.txt");
                Program.Log($"[!] Files to remove: {filesToRemove.Count} entries (--remove-files)");
            }

            List<string> filesToIgnore = new List<string>();
            if (options.PackIgnoreFiles)
            {
                if (!File.Exists("files_to_ignore.txt"))
                {
                    Console.WriteLine($"[X] --ignore-files was provided but 'files_to_ignore.txt' file does not exist.");
                    return;
                }
                filesToIgnore = ReadEntriesFromFile("files_to_ignore.txt");
                Program.Log($"[!] Files to ignore: {filesToIgnore.Count} entries (--ignore-files)");
            }

            if (vol.UsePackingCache && File.Exists(".pack_cache"))
                vol.ReadPackingCache(".pack_cache");

            if (options.StartIndex != 0)
            {
                Program.Log($"[!] Will use {options.StartIndex} as starting File Index. (--start-index)");
                vol.VolumeHeader.ToCNodeIndex = options.StartIndex;
            }

            if (options.CreateBDMARK)
                Program.Log("[!] PDIPFS_bdmark will be created. (--create_bdmark)");
            vol.CreateBDMARK = options.CreateBDMARK;

            vol.NoCompress = options.NoCompress;
            vol.RegisterEntriesToRepack(options.FolderToRepack, filesToIgnore);

            if (options.Version != null)
                vol.VolumeHeader.SerialNumber = options.Version.Value;

            if (!string.IsNullOrEmpty(options.CustomGameID))
                Program.Log($"[!] Volume Game ID will be set to '{options.CustomGameID}'. (--custom-game-id)");

            vol.PackFiles(options.OutputPath, filesToRemove, !options.PackAsOverwrite, options.CustomGameID);
        }

        public static void Unpack(UnpackVerbs options)
        {
            bool isFile = File.Exists(options.InputPath);
            bool isDir = false;
            
            if (!isFile)
            {
                isDir = Directory.Exists(options.InputPath);
                if (!isDir)
                {
                    Console.WriteLine($"[X] Volume file or PDIPFS folder \"{options.InputPath}\" does not exist.");
                    return;
                }
            }


            Keyset[] keyset = CheckKeys();
            if (keyset is null)
                return;


            if (!string.IsNullOrEmpty(options.LogPath))
                sw = new StreamWriter(options.LogPath);

            SaveTOC = options.SaveTOC;
            SaveHeader = options.SaveVolumeHeader;
            if (isDir)
            {
                string indexFile = Path.Combine(options.InputPath, PDIPFSPathResolver.Default);
                string oldIndexFile = Path.Combine(options.InputPath, PDIPFSPathResolver.DefaultOld);
                if (!File.Exists(indexFile) && !File.Exists(oldIndexFile))
                {
                    Console.WriteLine($"[X] Provided input folder (assuming PDIPFS) does not contain an Index file (K/4D or K/4/D). Make sure this folder is actually a PDIPFS folder.");
                    return;
                }
            }

            bool found = false;
            GTVolume vol = null;
            foreach (var k in keyset)
            {
                vol = GTVolume.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Big);
                if (vol != null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.WriteLine($"[X] Could not unpack volume. Make sure that you have a valid game key/seed in your key.json.");
                return;
            }

            Program.Log("[-] Started unpacking process.");
            vol.SetOutputDirectory(options.OutputPath);
            if (options.OnlyLog)
                vol.NoUnpack = true;

            vol.UnpackFiles(options.FileIndexesToExtract);
        }

        public static void UnpackInstaller(UnpackInstallerVerbs options)
        {
            if (!File.Exists(options.InputPath))
            {
                Console.WriteLine($"[X] File \"{options.InputPath}\" does not exist.");
                return;
            }

            Keyset[] keyset = CheckKeys();
            if (keyset is null)
                return;

            if (!string.IsNullOrEmpty(options.LogPath))
                sw = new StreamWriter(options.LogPath);

            bool found = false;
            InstallerUnpacker unp = null;
            foreach (var k in keyset)
            {
                unp = InstallerUnpacker.Load(k, options.InputPath, options.SaveHeaderTOC);
                if (unp != null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.WriteLine($"[X] Could not unpack installer. Make sure that you have a valid game key/seed in your key.json.");
                return;
            }

            Program.Log("[-] Started unpacking process.");
            unp.Unpack(options.OutputPath);
        }

        public static void Crypt(CryptVerbs options)
        {
            Keyset keys = null;
            if (string.IsNullOrEmpty(options.Salsa20KeyEncrypt) && string.IsNullOrEmpty(options.Salsa20KeyDecrypt))
            {
                Keyset[] keysets = CheckKeys();
                if (keysets is null)
                    return;

                keys = keysets.Where(e => e.GameCode.Equals(options.GameCode, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (keys is null)
                {
                    Console.WriteLine($"Keyset with GameCode '{options.GameCode}' does not exist in the keyset file.");
                    return;
                }
            }

            foreach (var file in options.InputPath)
            {
                if (!File.Exists(file) && !Directory.Exists(file))
                {
                    Console.WriteLine($"[X] File or folder '{file}' to decrypt/encrypt does not exist.");
                    continue;
                }

                bool isDir = File.GetAttributes(file).HasFlag(FileAttributes.Directory);
                if (isDir)
                {
                    foreach (var dirFile in Directory.GetFiles(file))
                        DecryptFile(options, keys, dirFile);
                }
                else
                {
                    DecryptFile(options, keys, file);
                }
            }
            Console.WriteLine("[/] Done.");
        }

        private static void DecryptFile(CryptVerbs options, Keyset keys, string file)
        {
            byte[] input = File.ReadAllBytes(file);
            if (!string.IsNullOrEmpty(options.Salsa20KeyEncrypt))
            {
                byte[] keyBytes = MiscUtils.StringToByteArray(options.Salsa20KeyEncrypt);
                using SymmetricAlgorithm salsa20 = new Salsa20SymmetricAlgorithm();
                byte[] dataKey = new byte[8];

                Console.WriteLine($"[:] Salsa Encrypting '{file}'..");
                using var decrypt = salsa20.CreateDecryptor(keyBytes, dataKey);
                decrypt.TransformBlock(input, 0, input.Length, input, 0);
            }
            else if (!string.IsNullOrEmpty(options.Salsa20KeyDecrypt))
            {
                byte[] keyBytes = MiscUtils.StringToByteArray(options.Salsa20KeyDecrypt);
                using SymmetricAlgorithm salsa20 = new Salsa20SymmetricAlgorithm();
                byte[] dataKey = new byte[8];

                Console.WriteLine($"[:] Salsa Decrypting '{file}'..");
                using var encrypt = salsa20.CreateEncryptor(keyBytes, dataKey);
                encrypt.TransformBlock(input, 0, input.Length, input, 0);
            }
            else
            {
                Console.WriteLine($"[:] Crypting '{file}'..");
                CryptoUtils.CryptBuffer(keys, input, input, 0);
            }

            File.WriteAllBytes(file, input);
        }

        public static void List(ListVerbs options)
        {
            bool isFile = File.Exists(options.InputPath);
            bool isDir = false;

            if (!isFile)
            {
                isDir = Directory.Exists(options.InputPath);
                if (!isDir)
                {
                    Console.WriteLine($"[X] Volume file or PDIPFS folder \"{options.InputPath}\" does not exist.");
                    return;
                }
            }

            Keyset[] keyset = CheckKeys();
            if (keyset is null)
                return;

            bool found = false;
            GTVolume vol = null;
            foreach (var k in keyset)
            {
                vol = GTVolume.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Big);
                if (vol != null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Console.WriteLine($"[X] Could not unpack volume. Make sure that you have a valid game key/seed in your key.json.");
                return;
            }

            using var sw = new StreamWriter(options.OutputPath);
            var entries = vol.TableOfContents.GetAllRegisteredFileMap();

            if (vol.VolumeHeader is FileDeviceGTFS3Header header3)
            {
                sw.WriteLine("[Volumes]");
                for (int i = 0; i < header3.VolList.Length; i++)
                    sw.WriteLine($"Vol: {header3.VolList[i].Name} (Size: {header3.VolList[i].Size:X8})");
                sw.WriteLine();
            }

            foreach (var entry in entries)
            {
                if (vol.VolumeHeader is FileDeviceGTFS3Header header33)
                {
                    var entryInfo = vol.TableOfContents.FileInfos.GetByFileIndex(entry.Value.EntryIndex);
                    sw.WriteLine($"{entry.Key} - {entryInfo} - {header33.VolList[entryInfo.VolumeIndex].Name}");

                }

            }

            sw.WriteLine($"[!] Wrote {entries.Count} at {options.OutputPath}.");
        }

        public static void Compress(CompressVerbs options)
        {
            if (!File.Exists(options.InputPath))
            {
                Console.WriteLine($"[X] File does not exist.");
                return;
            }

            Console.WriteLine("[:] Compressing..");
            var file = File.ReadAllBytes(options.InputPath);
            var compressed = PS2ZIP.Deflate(file);

            if (string.IsNullOrEmpty(options.OutputPath))
                options.OutputPath = options.InputPath;

            if (options.Base64Encode)
            {
                var b64 = Convert.ToBase64String(compressed);
                var b64Bytes = Encoding.ASCII.GetBytes(b64);

                string outpath = Path.ChangeExtension(options.OutputPath, ".b64");
                File.WriteAllBytes(outpath, b64Bytes);
                Console.WriteLine($"[/] Done compressing & encoding as Base64 to {options.OutputPath}.");
            }
            else
            {
                string outpath = Path.ChangeExtension(options.OutputPath, ".cmp");
                File.WriteAllBytes(outpath, compressed);
                Console.WriteLine($"[/] Done compressing to {options.OutputPath}.");
            }
           
        }

        public static void Log(string message, bool forceConsolePrint = false)
        {
            if (PrintToConsole || forceConsolePrint)
                Console.WriteLine(message);

            sw?.WriteLine(message);
        }

        public static void CreateDefaultKeysFile()
        {
            string json = JsonSerializer.Serialize(new[] 
            { 
                KeysetStore.Keyset_GT5P_JP_DEMO,
                KeysetStore.Keyset_GT5P_EU_SPEC3,
                KeysetStore.Keyset_GT5P_US_SPEC3,
                KeysetStore.Keyset_GT5_EU,
                KeysetStore.Keyset_GT5_US,
                KeysetStore.Keyset_GT6 
            }, new JsonSerializerOptions() { WriteIndented = true });

            File.WriteAllText("key.json", json);
        }

        public static Keyset[] CheckKeys()
        {
            if (!File.Exists("key.json"))
            {
                try
                {
                    CreateDefaultKeysFile();
                    Console.WriteLine("[X] Error: Volume Encryption Key file is missing (key.json).");
                    Console.WriteLine(" A default one was created with the keys for the following games:");
                    Console.WriteLine("  - GT5P Demo (Japan)");
                    Console.WriteLine("  - GT5P (US, Spec III)");
                    Console.WriteLine("  - GT5P (Europe, Spec III)");
                    Console.WriteLine("  - GT5 (Europe)");
                    Console.WriteLine("  - GT5 (US)");
                    Console.WriteLine("  - GT6 (Universal/All Regions)");
                    Console.WriteLine(" Just run the program again if the game you are trying to extract/pack matches one of the above.");
                    Console.WriteLine(" If not, change the file accordingly and provide keys for the game build/region you are trying to unpack.");
                    
                    Console.WriteLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[X] key.json was missing. Tried to create a default one, but unable to create it: {e.Message}");
                }

                return null;
            }
            else
            {
                Keyset[] keyset = ReadKeysets();
                if (keyset is null)
                    return null;

                if (keyset.Length == 0)
                {
                    Console.WriteLine("No keys found in key.json.");
                    return null;
                }

                return keyset;
            }
        }

        public static Keyset[] ReadKeysets()
        {
            try
            {
                return JsonSerializer.Deserialize<Keyset[]>(File.ReadAllText("key.json"));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: Unable to parse key.json. {e.Message}");
                return null;
            }
        }
        
        public static List<string> ReadEntriesFromFile(string file)
        {
            var list = new List<string>();
            string[] array = File.ReadAllLines(file);
            for (int i = 0; i < array.Length; i++)
            {
                string line = array[i];
                line = line.Trim().Replace('\\', '/');
                if (line.StartsWith("//") || string.IsNullOrEmpty(file))
                    continue;

                list.Add(line);
            }

            return list;
        }

        public static void HandleNotParsedArgs(IEnumerable<Error> errors)
        {
            ;
        }
    }
}
