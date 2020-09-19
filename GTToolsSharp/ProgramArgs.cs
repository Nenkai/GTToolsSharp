using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommandLine;
using CommandLine.Text;

namespace GTToolsSharp
{
    [Verb("unpack", HelpText = "Unpacks a volume file.")]
    public class UnpackVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input file volume or folder. Usually GT.VOL, or if game update, a PDIPFS folder.")]
        public string InputPath { get; set; }

        [Option('o', "output", Required = true, HelpText = "Output Folder for unpacked files. If you don't provide one, will display info about the volume.")]
        public string OutputPath { get; set; }

        [Option("unpack-log-only", HelpText = "Only log volume information while unpacking. (No unpacking will be done)")]
        public bool OnlyLog { get; set; }

        [Option("no-print", HelpText = "Whether to disable printing messages if unpacking. Disabling this can speed up the unpacking process")]
        public bool NoPrint { get; set; }

        [Option("save-volume-header", HelpText = "Decrypts and saves the volume header (as VolumeHeader.bin)")]
        public bool SaveVolumeHeader { get; set; }

        [Option("save-toc-header", HelpText = "Decrypts and saves the Table of Contents header. (as VolumeTOC.bin)")]
        public bool SaveTOC { get; set; }

        [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
        public string LogPath { get; set; }

        [Option("indexes", HelpText = "List of file specific entry indexes to extract")]
        public IEnumerable<int> FileIndexesToExtract { get; set; }

        [Usage(ApplicationAlias = "GTToolsSharp")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                return new List<Example>()
                    {
                        new Example("Unpack a GT.VOL", new UnpackVerbs { InputPath = @"C:\XXXXXX\GT.VOL", OutputPath = "GTVOL_EXTRACTED" } ),
                        new Example("Unpack a PDIPFS", new UnpackVerbs { InputPath = @"C:\XXXXXX\USRDIR\PDIPFS", OutputPath = "PDIPFS_EXTRACTED" } )
                    };
            }
        }
    }

    [Verb("crypt", HelpText = "Decrypts/Encrypts a file.")]
    public class CryptVerbs
    {
        [Option('g', "gamecode", Required = true, HelpText = "GameCode - Key to use within your key file.")]
        public string GameCode { get; set; }

        [Option('i', "input", Required = true, HelpText = "Input file to encrypt or decrypt.")]
        public string InputPath { get; set; }
        
        [Option('o', "output", HelpText = "Output file.", Default = "out.bin")]
        public string OutputPath { get; set; }

        [Option("salsaencrypt", HelpText = "Advanced users. Salsa key (hex string) to use to encrypt. Do not provide to use default file volume encryption.")]
        public string Salsa20KeyEncrypt { get; set; }

        [Option("salsadecrypt", HelpText = "Advanced users. Salsa key (hex string) to use to decrypt. Do not provide to use default file volume encryption.")]
        public string Salsa20KeyDecrypt { get; set; }
    }

    [Verb("pack", HelpText = "Packs a volume file.")]
    public class PackVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input file volume or folder to use as base. Usually GT.VOL, or if game update, a PDIPFS folder.")]
        public string InputPath { get; set; }

        [Option('o', "output", HelpText = "Output folder for the files to pack.", Default = "RepackedPDIPFS")]
        public string OutputPath { get; set; }

        [Option('p', "folder-to-pack", Required = true, HelpText = "Folder which contains all the files to be repacked.")]
        public string FolderToRepack { get; set; }

        [Option("remove-files", HelpText = "Remove specific files from the volume. Requires a \"files_to_remove.txt\" file in the current folder, each line being an asset path (i.e advertise/gt5-jp/pdi_sd.img)")]
        public bool PackRemoveFiles { get; set; }

        [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
        public string LogPath { get; set; }

        [Option("pack-all-as-new", HelpText = "Advanced users only. This marks all the files provided to pack, including existing ones in the volume as new files. " +
            "This is useful when creating a mod that add or modifies file content from the game without actually modifying files, just adding new ones, so only a volume header (K/4D) backup is required to revert all of the changes.")]
        public bool PackAllAsNew { get; set; }

        [Option("custom-game-id", HelpText = "Custom Game-ID/Description to assign to the volume header. Must not be above 128 characters.")]
        public string CustomGameID { get; set; }

        [Usage(ApplicationAlias = "GTToolsSharp")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                return new List<Example>()
                    {
                        new Example("Pack a PDIPFS", new PackVerbs { InputPath =  @"C:\XXXXXX\USRDIR\PDIPFS", OutputPath = "PDIPFS_REPACKED", FolderToRepack = @"C:\Users\XXXX\Desktop\MyFilesToRepackFolder" } )
                    };
            }
        }
    }
}
