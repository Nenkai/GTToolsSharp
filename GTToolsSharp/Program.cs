using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;

using System.Text.Json;

namespace GTToolsSharp
{
    class Program
    {
        private static StreamWriter sw;
        public static bool PrintToConsole = true;
        public static bool SaveHeader = false;
        public static bool SaveTOC = false;
        public class Options
        {
            [Option('i', "input", Required = true, HelpText = "Input file volume or folder. Usually GT.VOL, or if game update, a PDIPFS folder.")]
            public string InputPath { get; set; }

            [Option('o', "output",  HelpText = "Output Folder for unpacked files. If you don't provide one, will display info about the volume.")]
            public string OutputPath { get; set; }

            [Option('u', "unpack", HelpText = "Extract all the files in the volume file or folder.")]
            public bool Unpack { get; set; }

            [Option('p', "pack", HelpText = "Pack all files provided in this dir. Needs pack output dir.")]
            public string PackDir { get; set; }

            [Option("packoutputdir", HelpText = "Output folder for the files to pack.")]
            public string PackOutputDir { get; set; }

            [Option("packremovefiles", HelpText = "Remove specific files from the volume. Requires a \"files_to_remove.txt\" file in the current folder, each line being an asset path (i.e advertise/gt5-jp/pdi_sd.img)")]
            public bool PackRemoveFiles { get; set; }

            [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
            public string LogPath { get; set; }

            [Option("unpacklogonly", HelpText = "Only log volume information while unpacking. (No unpacking will be done)")]
            public bool OnlyLog { get; set; }

            [Option("noprint", HelpText = "Whether to disable printing messages if unpacking. Disabling this can speed up the unpacking process")]
            public bool NoPrint { get; set; }

            [Option("savevolumeheader", HelpText = "Decrypts and saves the volume header (as VolumeHeader.bin)")]
            public bool SaveVolumeHeader { get; set; }

            [Option("savetocheader", HelpText = "Decrypts and saves the Table of Contents header. (as VolumeTOC.bin)")]
            public bool SaveTOC { get; set; }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("-- Gran Turismo 5/6 Volume Tools - (c) Nenkai#9075, ported from flatz --");
            Console.WriteLine();

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(ParseArgs)
                .WithNotParsed(HandleNotParsedArgs);
        }

        public static void ParseArgs(Options options)
        {
            bool isFile = File.Exists(options.InputPath);
            bool isDir = false;
            if (!isFile)
            {
                isDir = Directory.Exists(options.InputPath);
                if (!isDir)
                {
                    Console.WriteLine($"File or Directory \"{options.InputPath}\" does not exist.");
                    return;
                }
            }

            Keyset keyset;
            if (!File.Exists("key.json"))
            {
                try
                {
                    CreateDefaultKeysFile();
                    Console.WriteLine($"Error: Key file is missing, A default one was created with GT5 EU keys. (key.json)");
                    Console.WriteLine($"Change them accordingly to the keys of the game you are trying to unpack.");
                    Console.WriteLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"key.json was missing. Tried to create a default one, but unable to create it: {e.Message}");
                }

                return;
            }
            else
            {
                keyset = ReadKeyset();
                if (keyset is null)
                    return;

            }

            if (!string.IsNullOrEmpty(options.PackDir) && string.IsNullOrEmpty(options.PackOutputDir))
            {
                Console.WriteLine("Packing output directory is missing.");
                return;
            }

            if (!string.IsNullOrEmpty(options.LogPath))
                sw = new StreamWriter(options.LogPath);

            GTVolume vol = null;
            if (isFile)
            {
                vol = GTVolume.Load(keyset, options.InputPath, false, Syroot.BinaryData.Core.Endian.Big);
            }
            else if (isDir)
            {
                string indexFile = Path.Combine(options.InputPath, PDIPFSPathResolver.Default);
                if (!File.Exists(indexFile))
                {
                    Console.WriteLine($"[X] Provided folder (assuming PDIPFS) does not contain an Index file. ({PDIPFSPathResolver.Default}).");
                    return;
                }

                vol = GTVolume.Load(keyset, options.InputPath, true, Syroot.BinaryData.Core.Endian.Big);
            }


            PrintToConsole = !options.NoPrint;
            SaveTOC = options.SaveTOC;
            SaveHeader = options.SaveVolumeHeader;
            if (vol is null)
            {
                Console.WriteLine("Could not process volume file.");
                return;
            }

            
            if (options.Unpack)
            {
                Program.Log("[-] Started unpacking process.");
                vol.SetOutputDirectory(options.OutputPath);
                if (options.OnlyLog)
                    vol.NoUnpack = true;

                vol.UnpackAllFiles();
            }
            else if (!string.IsNullOrEmpty(options.PackDir))
            {
                if (!vol.IsPatchVolume)
                {
                    Program.Log("[X] Cannot repack files in single volume files (GT.VOL).");
                    return;
                }

                Program.Log("[-] Started packing process.");

                string[] filesToRemove = Array.Empty<string>();
                if (options.PackRemoveFiles && File.Exists("files_to_remove.txt"))
                    filesToRemove = File.ReadAllLines("files_to_remove.txt");

                vol.RegisterEntriesToRepack(options.PackDir);
                vol.PackFiles(options.PackOutputDir, filesToRemove);
            }

            Program.Log("Exiting.");
            sw.Dispose();
        }

        public static void Log(string message, bool forceConsolePrint = false)
        {
            if (PrintToConsole || forceConsolePrint)
                Console.WriteLine(message);

            sw?.WriteLine(message);
        }

        public static void CreateDefaultKeysFile()
        {
            string json = JsonSerializer.Serialize(GTVolume.DefaultKeyset, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText("key.json", json);
        }

        public static Keyset ReadKeyset()
        {
            try
            {
                return JsonSerializer.Deserialize<Keyset>(File.ReadAllText("key.json"));
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
