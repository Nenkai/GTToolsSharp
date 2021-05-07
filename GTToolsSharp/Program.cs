using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using CommandLine;
using CommandLine.Text;

using GTToolsSharp.Encryption;
using GTToolsSharp.Utils;
using GTToolsSharp.PackedFileInstaller;

namespace GTToolsSharp
{
    class Program
    {
        private static StreamWriter sw;
        public static bool PrintToConsole = true;
        public static bool SaveHeader = false;
        public static bool SaveTOC = false;

        static void Main(string[] args)
        {
            Console.WriteLine("-- Gran Turismo 5/6 Volume Tools - (c) Nenkai#9075, ported from flatz --");
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
                Program.Log("[!] Using packing cache.");
            vol.UsePackingCache = options.Cache;

            Program.Log("[-] Started packing process.");

            string[] filesToRemove = Array.Empty<string>();
            if (options.PackRemoveFiles && File.Exists("files_to_remove.txt"))
                filesToRemove = File.ReadAllLines("files_to_remove.txt");

            if (options.PackAllAsNew)
                Program.Log("[!] Note: --pack-all-as-new provided - packing as new is now on by default. To use overwrite mode, use --pack-as-overwrite");

            if (vol.UsePackingCache && File.Exists(".pack_cache"))
                vol.ReadPackingCache(".pack_cache");

            if (options.StartIndex != 0)
            {
                Program.Log($"[!] Will use {options.StartIndex} as starting File Index.");
                vol.VolumeHeader.TOCEntryIndex = options.StartIndex;
            }

            vol.CreateBDMARK = options.CreateBDMARK;
            vol.NoCompress = options.NoCompress;
            vol.RegisterEntriesToRepack(options.FolderToRepack);
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
                if (!File.Exists(indexFile))
                {
                    Console.WriteLine($"[X] Provided input folder (assuming PDIPFS) does not contain an Index file. ({PDIPFSPathResolver.Default}) Make sure this folder is actually a PDIPFS folder.");
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

            SaveTOC = options.SaveTOC;
            SaveHeader = options.SaveHeader;

            bool found = false;
            InstallerUnpacker unp = null;
            foreach (var k in keyset)
            {
                unp = InstallerUnpacker.Load(k, options.InputPath);
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
                using SymmetricAlgorithm salsa20 = new Salsa20();
                byte[] dataKey = new byte[8];

                Console.WriteLine($"[:] Salsa Encrypting '{file}'..");
                using var decrypt = salsa20.CreateDecryptor(keyBytes, dataKey);
                decrypt.TransformBlock(input, 0, input.Length, input, 0);
            }
            else if (!string.IsNullOrEmpty(options.Salsa20KeyDecrypt))
            {
                byte[] keyBytes = MiscUtils.StringToByteArray(options.Salsa20KeyDecrypt);
                using SymmetricAlgorithm salsa20 = new Salsa20();
                byte[] dataKey = new byte[8];

                Console.WriteLine($"[:] Salsa Decrypting '{file}'..");
                using var encrypt = salsa20.CreateEncryptor(keyBytes, dataKey);
                encrypt.TransformBlock(input, 0, input.Length, input, 0);
            }
            else
            {
                Console.WriteLine($"[:] Crypting '{file}'..");
                keys.CryptData(input, 0);
            }

            Console.WriteLine($"[:] Saving file as {file}.dec..");
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
            foreach (var entry in entries)
            {
                if (vol.IsPatchVolume)
                    sw.WriteLine($"{entry.Key} ({entry.Value.EntryIndex}) - {PDIPFSPathResolver.GetPathFromSeed(entry.Value.EntryIndex)}");
                else
                    sw.WriteLine($"{entry.Key} ({entry.Value.EntryIndex})");
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
            var compressed = MiscUtils.ZlibCompress(file);

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
            string json = JsonSerializer.Serialize(new[] { GTVolume.Keyset_GT5_EU, GTVolume.Keyset_GT5_US, GTVolume.Keyset_GT6 }, new JsonSerializerOptions() { WriteIndented = true }); ;
            File.WriteAllText("key.json", json);
        }

        public static Keyset[] CheckKeys()
        {
            if (!File.Exists("key.json"))
            {
                try
                {
                    CreateDefaultKeysFile();
                    Console.WriteLine($"[X] Error: Key file is missing, A default one was created with GT5 EU, US and GT6 keys. (key.json)");
                    Console.WriteLine($"Change them accordingly to the keys of the game and/or different game region you are trying to unpack if needed.");
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

        public static void HandleNotParsedArgs(IEnumerable<Error> errors)
        {
            ;
        }
    }
}
