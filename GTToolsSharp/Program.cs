﻿using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;

namespace GTToolsSharp
{
    class Program
    {
        private static StreamWriter sw;
        public static bool PrintToConsole = true;

        public class Options
        {
            [Option('i', "input", Required = true, HelpText = "Input file or folder. Usually GT.VOL, or if game update, a PDIPFS folder.")]
            public string InputPath { get; set; }

            [Option('o', "output",  HelpText = "Output Folder for unpacked files.")]
            public string OutputPath { get; set; }

            [Option('u', "unpack", HelpText = "Extract all the files in the volume file or folder.")]
            public bool Unpack { get; set; }

            [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
            public string LogPath { get; set; }

            [Option("noprint", HelpText = "Whether to disable printing messages if unpacking. Disabling this can speed up the unpacking process")]
            public bool NoPrint { get; set; }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Gran Turismo 5/6 Volume Tools - (c) Nenkai#9075, ported from flatz");

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

            GTVolume vol = null;
            if (isFile)
            {
                vol = GTVolume.Load(options.InputPath, false, Syroot.BinaryData.Core.Endian.Big);
            }
            else if (isDir)
            {
                string indexFile = Path.Combine(options.InputPath, "K", "4D");
                if (!File.Exists(indexFile))
                {
                    Console.WriteLine("Provided folder (assuming PDIPFS) does not contain an Index file. (PDIPFS\\K\\4D).");
                    return;
                }

                vol = GTVolume.Load(options.InputPath, true, Syroot.BinaryData.Core.Endian.Big);
            }

            if (!string.IsNullOrEmpty(options.LogPath))
                sw = new StreamWriter(options.LogPath);

            PrintToConsole = !options.NoPrint;

            if (vol is null)
            {
                Console.WriteLine("Could not process volume file.");
                return;
            }

            if (options.Unpack)
            {
                vol.SetOutputDirectory(options.OutputPath);
                vol.UnpackAllFiles();
            }
        }

        public static void Log(string message)
        {
            if (PrintToConsole)
                Console.WriteLine(message);

            sw?.WriteLine(message);
        }
        public static void HandleNotParsedArgs(IEnumerable<Error> errors)
        {
            foreach (var error in errors)
            {
                Console.WriteLine(error.ToString());
            }
        }
    }
}
