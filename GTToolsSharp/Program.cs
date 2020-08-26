using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using CommandLine.Text;

using System.Text.Json;

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

            Parser.Default.ParseArguments<PackVerbs, UnpackVerbs>(args)
                .WithParsed<PackVerbs>(Pack)
                .WithParsed<UnpackVerbs>(Unpack)
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

            Keyset[] keyset;
            if (!File.Exists("key.json"))
            {
                try
                {
                    CreateDefaultKeysFile();
                    Console.WriteLine($"[X] Error: Key file is missing, A default one was created with GT5 EU keys. (key.json)");
                    Console.WriteLine($"Change them accordingly to the keys of the game and/or different game region you are trying to unpack.");
                    Console.WriteLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[X] key.json was missing. Tried to create a default one, but unable to create it: {e.Message}");
                }

                return;
            }
            else
            {
                keyset = ReadKeysets();
                if (keyset is null)
                    return;

                if (keyset.Length == 0)
                {
                    Console.WriteLine("No keys found in key.json.");
                    return;
                }
            }

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

            if (!vol.IsPatchVolume)
            {
                Program.Log("[X] Cannot repack files in single volume files (GT.VOL).");
                return;
            }
            
            Program.Log("[-] Started packing process.");

            string[] filesToRemove = Array.Empty<string>();
            if (options.PackRemoveFiles && File.Exists("files_to_remove.txt"))
                filesToRemove = File.ReadAllLines("files_to_remove.txt");

            vol.RegisterEntriesToRepack(options.FolderToRepack);
            vol.PackFiles(options.OutputPath, filesToRemove, options.PackAllAsNew, options.CustomGameID);
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
            

            Keyset[] keyset;
            if (!File.Exists("key.json"))
            {
                try
                {
                    CreateDefaultKeysFile();
                    Console.WriteLine($"[X] Error: Key file is missing, A default one was created with GT5 EU keys. (key.json)");
                    Console.WriteLine($"Change them accordingly to the keys of the game and/or different game region you are trying to unpack.");
                    Console.WriteLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[X] key.json was missing. Tried to create a default one, but unable to create it: {e.Message}");
                }

                return;
            }
            else
            {
                keyset = ReadKeysets();
                if (keyset is null)
                    return;

                if (keyset.Length == 0)
                {
                    Console.WriteLine("No keys found in key.json.");
                    return;
                }
            }


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

        public static void Log(string message, bool forceConsolePrint = false)
        {
            if (PrintToConsole || forceConsolePrint)
                Console.WriteLine(message);

            sw?.WriteLine(message);
        }

        public static void CreateDefaultKeysFile()
        {
            string json = JsonSerializer.Serialize(new[] { GTVolume.DefaultKeyset }, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText("key.json", json);
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
