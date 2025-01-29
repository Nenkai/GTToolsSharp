using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommandLine;
using CommandLine.Text;

namespace GTToolsSharp;

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

    [Option("indices", HelpText = "List of file specific entry indices to extract")]
    public IEnumerable<int> FileIndicesToExtract { get; set; }

    [Usage(ApplicationAlias = "GTToolsSharp")]
    public static IEnumerable<Example> Examples
    {
        get
        {
            return
            [
                new Example("Unpack a GT.VOL", new UnpackVerbs { InputPath = @"C:\XXXXXX\GT.VOL", OutputPath = "GTVOL_EXTRACTED" } ),
                new Example("Unpack a PDIPFS", new UnpackVerbs { InputPath = @"C:\XXXXXX\USRDIR\PDIPFS", OutputPath = "PDIPFS_EXTRACTED" } ),
                new Example("Unpack a GT Sport Volume", new UnpackVerbs { InputPath = @"C:\XXXXXX\USRDIR\gt.idx", OutputPath = "GTVOL_EXTRACTED" } )
            ];
        }
    }
}

[Verb("gt7unpack", HelpText = "Unpacks a GT7 volume file.")]
public class GT7UnpackVerbs
{
    [Option('i', "input", Required = true, HelpText = "Input file volume or folder. Usually gt.idx.")]
    public string InputPath { get; set; }

    [Option('f', "file", HelpText = "Specific file to unpack. If it doesn't exist, the whole volume will be unpacked. Note: Path/file must match exact volume game paths.")]
    public string FileToUnpack { get; set; }

    [Option('o', "output", HelpText = "Output Folder for unpacked files. If you don't provide one, will display info about the volume. Defaults to 'Unpacked' next to index file.")]
    public string OutputPath { get; set; }

    [Option("save-volume-header", HelpText = "Decrypts and saves the volume header (as VolumeHeader.bin)")]
    public bool SaveVolumeHeader { get; set; }

    [Usage(ApplicationAlias = "GTToolsSharp")]
    public static IEnumerable<Example> Examples
    {
        get
        {
            return
            [
                new Example("Unpack all volumes", new GT7UnpackVerbs { InputPath = @"C:\XXXXXX\GT.IDX", OutputPath = "GTVOL_EXTRACTED" } ),
                new Example("Unpack a file", new GT7UnpackVerbs { InputPath = @"C:\XXXXXX\GT.IDX", FileToUnpack = "sound_gt/se/gt7sys.szd", OutputPath = "PDIPFS_EXTRACTED" } ),
            ];
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

[Verb("crypt", HelpText = "Decrypts/Encrypts a general game file (mostly from the user data folder on the console).")]
public class CryptVerbs
{
    [Option('g', "gamecode", Required = true, HelpText = "GameCode - Key to use within your key file.")]
    public string GameCode { get; set; }

    [Option('i', "input", Required = true, HelpText = "Input files or folder to encrypt or decrypt.")]
    public IEnumerable<string> InputPath { get; set; }

    [Option('s', "seed", Default = (uint)0, HelpText = "Encrypt Seed to use. Default is 0. Change only if you know what you are doing.")]
    public uint Seed { get; set; }

    [Option("keyset-seed-override", HelpText = "Override keyset seed with custom seed. Use only if you know what you are doing. Used by GT5P to " +
        "decrypt some files in the grim folder by overriding with the DISC game code (even on PSN versions!), " +
        "i.e for NPUA-80075 which is the GT5P US PSN game code, you would have to use BCUS-98158 which is the DISC game code.")]
    public string KeysetSeedOverride { get; set; }

    [Option("alternative", HelpText = "Use alternative method. Only use if you absolutely know what you are doing.")]
    public bool UseAlternative { get; set; }
}

[Verb("cryptsalsa", HelpText = "Decrypts/Encrypts a file using a specified Salsa20 key. Normally used for the database files (specdb, menudb, caption.dat)")]
public class CryptSalsaVerbs
{
    [Option('i', "input", Required = true, HelpText = "Input files or folder to encrypt or decrypt.")]
    public IEnumerable<string> InputPath { get; set; }

    [Option('k', "key", Required = true, HelpText = "Key as hex string, or base64 string.")]
    public string Key { get; set; }
}

[Verb("cryptmovie", HelpText = "Decrypt the specified Salsa20 key, then decrypts the provided movie file. Keys for each movie file are within menudb.")]
public class CryptMovieVerbs
{
    [Option('i', "input", Required = true, HelpText = "Input files or folder to encrypt or decrypt.")]
    public IEnumerable<string> InputPath { get; set; }

    [Option('k', "key", Required = true, HelpText = "Key as base64 string, or hex string.")]
    public string Key { get; set; }
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

    [Option("remove-files", HelpText = "Remove specific files from the volume. Requires a text file to be specified, each line being an asset path (i.e advertise/gt5-jp/pdi_sd.img)")]
    public string RemoveFilesList { get; set; }

    [Option("ignore-files", HelpText = "Files to ignore from the mod folder while packing. Requires a text file to be specified, each line being a file in the mod folder (i.e advertise/gt5-jp/pdi_sd.img)")]
    public string IgnoreFilesList { get; set; }

    [Option("cache", HelpText = "Enable packing cache.")]
    public bool Cache { get; set; }

    [Option('l', "log", HelpText = "Log file path. Default is log.txt.", Default = "log.txt")]
    public string LogPath { get; set; }

    [Option("pack-all-as-new", Default = true, HelpText = "On by default. This marks all the files provided to pack, including existing ones in the volume as new files.\n" +
        "This is useful when creating a mod that add or modifies file content from the game without actually modifying files, just adding new ones, so only a volume header (K/4D) backup is required to revert all of the changes.\n" +
        "Similar behavior can be achieved by setting start-index to a number higher than the last file index in the PFS.")]
    public bool PackAllAsNew { get; set; } = true;

    [Option("custom-game-id", HelpText = "Custom Game-ID/Description to assign to the volume header. Must not be above 127 characters.")]
    public string CustomGameID { get; set; }

    [Option("no-compress", HelpText = "Newly packed files won't be compressed, and will be marked as such in the TOC. Do not use with streamed files such as shapestream/texstream.")]
    public bool NoCompress { get; set; }

    [Option("start-index", HelpText = "Start index of which new files to pack will start from, useful when making files work across multiple updates by making it high.")]
    public uint StartIndex { get; set; }

    [Option("create_bdmark", HelpText = "Bdmark will be created. Advanced users only. Used for patching games from their first version (i.e GT5 1.00) as bdmark tells which files were read from the disc and installed into the HDD.")]
    public bool CreateBDMARK { get; set; }

    [Option("updatenodeinfo", HelpText = "Advanced users only. Creates an update node info file with the summary of the patch.")]
    public bool UpdateNodeInfo { get; set; }

    [Option("patchsequence", HelpText = "Advanced users only. Creates a patch sequence file.")]
    public bool PatchSequence { get; set; }

    [Option("grim-patch", HelpText = "Advanced users only. Creates a grim patch summary file.")]
    public bool GrimPatch { get; set; }

    [Option("version", Hidden = true, HelpText = "Set version")]
    public ulong? Version { get; set; }

    [Usage(ApplicationAlias = "GTToolsSharp")]
    public static IEnumerable<Example> Examples
    {
        get
        {
            return
            [
                new Example("Pack a PDIPFS", new PackVerbs { InputPath =  @"C:\XXXXXX\USRDIR\PDIPFS", OutputPath = "PDIPFS_REPACKED", FolderToRepack = @"C:\Users\XXXX\Desktop\MyFilesToRepackFolder" } )
            ];
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

    [Option("order-by-file-index", HelpText = "Order the file list by their file index.", Default = false)]
    public bool OrderByFileIndex { get; set; }
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

[Verb("decompress", HelpText = "Decompresses a file (also known as PS2ZIP)")]
public class DecompressVerbs
{
    [Option('i', "input", Required = true, HelpText = "Input file to compress")]
    public string InputPath { get; set; }

    [Option('o', "output", HelpText = "Output file, compressed", Default = "")]
    public string OutputPath { get; set; }
}
