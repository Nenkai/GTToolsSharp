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

        [Option("base-folder", HelpText = "Used for extracting PDIPFS with Binary Patching files introduced with Gran Turismo 1.05+.")]
        public string BasePFSFolder { get; set; }

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

    [Verb("unpackinstaller", HelpText = "Unpacks an installation file (TV.DAT)")]
    public class UnpackInstallerVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input file volume or folder. Usually TV.DAT.")]
        public string InputPath { get; set; }

        [Option('o', "output", Required = true, HelpText = "Output Folder for unpacked install files.")]
        public string OutputPath { get; set; }

        [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
        public string LogPath { get; set; }

        [Option("save-header-toc", HelpText = "Decrypts and saves the header & TOC of the installer. (as InstallerTOC.bin)")]
        public bool SaveHeaderTOC { get; set; }
    }

    [Verb("crypt", HelpText = "Decrypts/Encrypts a file.")]
    public class CryptVerbs
    {
        [Option('g', "gamecode", Required = true, HelpText = "GameCode - Key to use within your key file.")]
        public string GameCode { get; set; }

        [Option('i', "input", Required = true, HelpText = "Input files or folder to encrypt or decrypt.")]
        public IEnumerable<string> InputPath { get; set; }

        [Option('s', "seed", Default = (uint)0, HelpText = "Encrypt Seed to use. Default is 0. Change only if you know what you are doing.")]
        public uint Seed { get; set; }

        [Option("keyset-seed-override", HelpText = "Override keyset seed with custom seed. Use only if you know what you are doing. Used by GT5P to " +
            "decrypt some files in the grim folder by overriding with the game code, i.e 'NPUA-80075'.")]
        public string KeysetSeedOverride { get; set; }

        [Option("alternative", HelpText = "Use alternative method. Use only if you know what you are doing. Used by GT5P (NPUA80075).")]
        public bool UseAlternative { get; set; }

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

        [Option("ignore-files", HelpText = "Files to ignore from the mod folder while packing. Requires a \"files_to_ignore.txt\" file in the current folder, each line being a file in the mod folder (i.e advertise/gt5-jp/pdi_sd.img)")]
        public bool PackIgnoreFiles { get; set; }

        [Option("cache", HelpText = "Enable packing cache.")]
        public bool Cache { get; set; }

        [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
        public string LogPath { get; set; }

        [Option("pack-all-as-new", HelpText = "On by default. This marks all the files provided to pack, including existing ones in the volume as new files. " +
            "This is useful when creating a mod that add or modifies file content from the game without actually modifying files, just adding new ones, so only a volume header (K/4D) backup is required to revert all of the changes.")]
        public bool PackAllAsNew { get; set; }

        [Option("pack-as-overwrite", HelpText = "Advanced users only. Instead of packing files as new files and leaving original entries untouched, they are overwritten.")]
        public bool PackAsOverwrite { get; set; }

        [Option("custom-game-id", HelpText = "Custom Game-ID/Description to assign to the volume header. Must not be above 128 characters.")]
        public string CustomGameID { get; set; }

        [Option("no-compress", HelpText = "Newly packed files won't be compressed, and will be marked as such in the TOC. Do not use with streamed files such as shapestream/texstream.")]
        public bool NoCompress { get; set; }

        [Option("start-index", HelpText = "Start index of which new files to pack will start from, useful when making files work across multiple updates by making it high.")]
        public uint StartIndex { get; set; }

        [Option("create_bdmark", HelpText = "Bdmark will be created. Advanced users only. Used for patching games from their first version (i.e GT5 1.00) as bdmark tells which files were read from the disc and installed into the HDD.")]
        public bool CreateBDMARK { get; set; }

        [Option("updatenodeinfo", HelpText = "Creates an update node info file with the summary of the patch. Advanced users only.")]
        public bool UpdateNodeInfo { get; set; }

        [Option("version", Hidden = true, HelpText = "Set version")]
        public ulong? Version { get; set; }

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

    [Verb("listfiles", HelpText = "List all volume entries into a file.")]
    public class ListVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input file volume or folder to use as base. Usually GT.VOL, or if game update, a PDIPFS folder.")]
        public string InputPath { get; set; }

        [Option('o', "output", HelpText = "Output folder for the files to pack.", Default = "volume_entries.txt")]
        public string OutputPath { get; set; }
    }

    [Verb("compress", HelpText = "Custom compresses a file (also known as PS2ZIP)")]
    public class CompressVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input file to compress")]
        public string InputPath { get; set; }

        [Option('o', "output", HelpText = "Output file, compressed", Default = "")]
        public string OutputPath { get; set; }

        [Option("b64", HelpText = "Encode the compressed file as base64.")]
        public bool Base64Encode { get; set; }
    }
}
