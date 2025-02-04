﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using CommandLine;

using GTToolsSharp.Headers;
using GTToolsSharp.Encryption;
using GTToolsSharp.Utils;
using GTToolsSharp.PackedFileInstaller;
using GTToolsSharp.Volumes;

using PDTools.Compression;
using PDTools.Utils;
using PDTools.Crypto;

namespace GTToolsSharp;

class Program
{
    private static StreamWriter sw = new StreamWriter("log.txt");

    public static bool PrintToConsole = true;
    public static bool SaveHeader = false;
    public static bool SaveTOC = false;

    public const string Version = "5.3.2";

    static async Task Main(string[] args)
    {
        Console.WriteLine($"-- GTToolsSharp {Version} - (c) Nenkai#9075, ported from flatz's gttool --");
        Console.WriteLine();

        var p = Parser.Default.ParseArguments<PackVerbs, UnpackVerbs, UnpackInstallerVerbs, CryptVerbs, CryptSalsaVerbs, CryptMovieVerbs, ListVerbs, CompressVerbs, DecompressVerbs, GT7UnpackVerbs>(args);
        p = await p.WithParsedAsync<GT7UnpackVerbs>(UnpackGT7);
         
        p.WithParsed<PackVerbs>(Pack)
         .WithParsed<UnpackVerbs>(Unpack)
         .WithParsed<UnpackInstallerVerbs>(UnpackInstaller)
         .WithParsed<CryptVerbs>(Crypt)
         .WithParsed<CryptSalsaVerbs>(CryptSalsa)
         .WithParsed<CryptMovieVerbs>(CryptMovie)
         .WithParsed<ListVerbs>(List)
         .WithParsed<CompressVerbs>(Compress)
         .WithParsed<DecompressVerbs>(Decompress)
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
        {
            sw?.Dispose();
            sw = new StreamWriter(options.LogPath);
        }

        if (!string.IsNullOrEmpty(options.CustomGameID) && options.CustomGameID.Length > 128 - 1)
        {
            Console.WriteLine($"[X] Custom Game ID must not be above 127 characters.");
            return;
        }

        bool found = false;
        GTVolumePFS vol = null;
        foreach (var k in keyset)
        {
            vol = GTVolumePFS.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Big);
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

        PatchFileSystemBuilder builder = new PatchFileSystemBuilder(vol);

        if (options.Cache)
            Program.Log("[!] Using packing cache. (--cache)");
        builder.UsePackingCache = options.Cache;

        Program.Log("[-] Started packing process.");

        options.FolderToRepack = Path.GetFullPath(options.FolderToRepack);
        string parentRepackFolder = Directory.GetParent(options.FolderToRepack).FullName;

        List<string> filesToRemove = [];
        if (!string.IsNullOrEmpty(options.RemoveFilesList))
        {
            if (!File.Exists(options.RemoveFilesList))
            {
                Console.WriteLine($"[X] --remove-files was provided but 'files_to_remove.txt' file does not exist.");
                return;
            }
            filesToRemove = ReadEntriesFromFile(options.RemoveFilesList);
            Program.Log($"[!] Files to remove: {filesToRemove.Count} entries (--remove-files)");
        }

        List<string> filesToIgnore = [];
        if (!string.IsNullOrEmpty(options.IgnoreFilesList))
        {
            if (!File.Exists(options.IgnoreFilesList))
            {
                Console.WriteLine($"[X] --ignore-files was provided but 'files_to_ignore.txt' file does not exist.");
                return;
            }
            filesToIgnore = ReadEntriesFromFile(options.IgnoreFilesList);
            Program.Log($"[!] Files to ignore: {filesToIgnore.Count} entries (--ignore-files)");
        }

        if (builder.UsePackingCache)
        {
            string packCacheFile = Path.Combine(parentRepackFolder, ".pack_cache");
            if (File.Exists(packCacheFile))
                builder.ReadPackingCache(packCacheFile);
        }

        if (options.StartIndex != 0)
        {
            Program.Log($"[!] Will use {options.StartIndex} as starting File Index. (--start-index)");
            vol.VolumeHeader.ToCNodeIndex = options.StartIndex;
        }

        if (options.CreateBDMARK)
            Program.Log("[!] PDIPFS_bdmark will be created. (--create_bdmark)");
        builder.CreateBDMark = options.CreateBDMARK;

        builder.NoCompress = options.NoCompress;
        builder.RegisterFilesToPackFromDirectory(options.FolderToRepack, filesToIgnore, options.UpdateNodeInfo);
        builder.GrimPatch = options.GrimPatch;
        builder.PackAllAsNewEntries = options.PackAllAsNew;
        builder.CreateUpdateNodeInfo = options.UpdateNodeInfo;
        builder.CreatePatchSequence = options.PatchSequence;

        if (options.Version != null)
            builder.NewSerial = options.Version.Value;

        if (!string.IsNullOrEmpty(options.CustomGameID))
            Program.Log($"[!] Volume Game ID will be set to '{options.CustomGameID}'. (--custom-game-id)");

        builder.Build(options.OutputPath, filesToRemove, options.CustomGameID);
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
        {
            sw?.Dispose();
            sw = new StreamWriter(options.LogPath);
        }

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
        GTVolumePFS vol = null;
        foreach (var k in keyset)
        {
            vol = GTVolumePFS.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Little);
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

        Program.Log("[-] Started unpacking process.", forceConsolePrint: true);

        PFSVolumeUnpacker unpacker = new PFSVolumeUnpacker(vol);
        unpacker.SetOutputDirectory(options.OutputPath);
        if (options.OnlyLog)
            unpacker.NoUnpack = true;

        unpacker.BasePFSFolder = options.BasePFSFolder;

        unpacker.UnpackFiles(options.FileIndicesToExtract, options.BasePFSFolder);
    }

    public static async Task UnpackGT7(GT7UnpackVerbs options)
    {
        if (!File.Exists(options.InputPath))
        {
            Console.WriteLine($"[X] Input index file does not exist.");
            return;
        }

        var vol = GTVolumeMPH.Load(options.InputPath);
        if (vol == null)
        {
            Console.WriteLine($"[X] Could not unpack volume.");
            return;
        }

        Program.Log("[-] Started unpacking process.", forceConsolePrint: true);
        Program.SaveHeader = options.SaveVolumeHeader;

        if (string.IsNullOrEmpty(options.OutputPath))
            options.OutputPath = Path.Combine(Path.GetDirectoryName(options.InputPath), "Unpacked");

        if (!string.IsNullOrEmpty(options.FileToUnpack))
        {
            if (vol.UnpackFile(options.FileToUnpack, options.OutputPath))
            {
                Program.Log($"[/] Successfully unpacked '{options.FileToUnpack}'.");
            }
            else
            {
                Program.Log($"[/] Failed to unpack '{options.FileToUnpack}'. Error while unpacking or file was not found in the hashed volume.");
            }
        }
        else
        {
            await vol.UnpackAllFiles(options.OutputPath);
        }
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
        {
            sw?.Dispose();
            sw = new StreamWriter(options.LogPath);
        }

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
        
        Keyset[] keysets = CheckKeys();
        if (keysets is null)
            return;

        keys = keysets.Where(e => e.GameCode.Equals(options.GameCode, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (keys is null)
        {
            Console.WriteLine($"Keyset with GameCode '{options.GameCode}' does not exist in the keyset file.");
            return;
        }
        

        foreach (var file in options.InputPath)
        {
            if (!File.Exists(file) && !Directory.Exists(file))
            {
                Console.WriteLine($"[X] File or folder '{file}' to decrypt/encrypt does not exist.");
                continue;
            }

            // For some files, game code may be used, i.e "NPUA-80075"
            if (!string.IsNullOrEmpty(options.KeysetSeedOverride))
            {
                Console.WriteLine($"[!] Overriding {keys.Magic} with {options.KeysetSeedOverride}");
                keys.Magic = options.KeysetSeedOverride;
            }

            bool isDir = File.GetAttributes(file).HasFlag(FileAttributes.Directory);
            if (isDir)
            {
                foreach (var dirFile in Directory.GetFiles(file, "*", SearchOption.AllDirectories))
                    DecryptFile(options, keys, dirFile);
            }
            else
            {
                DecryptFile(options, keys, file);
            }
        }

        Console.WriteLine("[/] Done.");
    }

    public static void CryptSalsa(CryptSalsaVerbs options)
    {
        foreach (var file in options.InputPath)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"[X] File or folder '{file}' to decrypt/encrypt does not exist.");
                continue;
            }

            DecryptFile_Salsa(file, options.Key);
        }

        Console.WriteLine("[/] Done.");
    }

    public static void CryptMovie(CryptMovieVerbs options)
    {
        foreach (var file in options.InputPath)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"[X] File or folder '{file}' to decrypt/encrypt does not exist.");
                continue;
            }

            using (var fs = new FileStream(file, FileMode.Open))
            using (var br = new BinaryReader(fs))
            {
                if (br.ReadUInt32() == 0x464D4150)
                {
                    Console.WriteLine($"[!] Movie file '{file}' is decrypted. Encrypt? [y/n]");
                    if (Console.ReadKey().Key != ConsoleKey.Y)
                    {
                        Console.WriteLine($"[!] Skipped {file}");
                        continue;
                    }
                }
            }

            DecryptFile_Salsa(file, options.Key, decryptKeyWithBaseKey: true);
        }

        Console.WriteLine("[/] Done.");
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
        GTVolumePFS vol = null;
        foreach (var k in keyset)
        {
            vol = GTVolumePFS.Load(k, options.InputPath, isDir, Syroot.BinaryData.Core.Endian.Big);
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
        var entries = vol.BTree.GetAllRegisteredFileMap();
        if (options.OrderByFileIndex)
            entries = entries.OrderBy(e => e.Value.EntryIndex).ToDictionary(x => x.Key, x => x.Value);

        var header3 = vol.VolumeHeader as FileDeviceGTFS3Header;
        if (header3 is not null)
        {
            sw.WriteLine("[Volumes]");
            for (int i = 0; i < header3.VolList.Length; i++)
                sw.WriteLine($"Vol: {header3.VolList[i].Name} (Size: {header3.VolList[i].Size:X8})");
            sw.WriteLine();
        }

        foreach (var entry in entries)
        {
            var entryInfo = vol.BTree.FileInfos.GetByFileIndex(entry.Value.EntryIndex);
            if (header3 is not null)
                sw.WriteLine($"{entry.Key} - {entryInfo} - {header3.VolList[entryInfo.VolumeIndex].Name}");
            else
            {
                sw.WriteLine($"{entry.Key} - {entryInfo}");
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

    public static void Decompress(DecompressVerbs options)
    {
        if (Directory.Exists(options.InputPath))
        {
            var files = Directory.GetFiles(options.InputPath);
            foreach (var compFile in files)
            {
                using (var fs = new FileStream(compFile, FileMode.Open))
                using (var br = new BinaryReader(fs))
                using (var outputStream = new FileStream(compFile + ".out", FileMode.Create))
                {
                    if (fs.Length < 4)
                    {
                        Console.WriteLine($"[!] Skipping {compFile} - too small length");
                        continue;
                    }

                    if (br.ReadUInt32() != 0xFFF7EEC5)
                    {
                        Console.WriteLine($"[!] Skipping {compFile} - Not a PS2ZIP compressed file");
                        continue;
                    }
                    fs.Position = 0;

                    if (!PS2ZIP.TryInflate(fs, outputStream))
                    {
                        Console.WriteLine($"[/] Failed to decompress {compFile}.");
                        continue;
                    }
                }

                File.Move(compFile + ".out", compFile, overwrite: true);
                Console.WriteLine($"[/] Decompressed {compFile}.");
            }
        }
        else if (File.Exists(options.InputPath))
        {
            if (string.IsNullOrEmpty(options.OutputPath))
                options.OutputPath = options.InputPath;

            Console.WriteLine("[:] Compressing..");
            using (var fs = new FileStream(options.InputPath, FileMode.Create))
            using (var br = new BinaryReader(fs))
            using (var outputStream = new FileStream(options.OutputPath + ".out", FileMode.Open))
            {
                if (br.ReadUInt32() != 0xFFF7EEC5)
                {
                    Console.WriteLine($"[!] Skipping {options.InputPath} - Not a PS2ZIP compressed file");
                    return;
                }
                fs.Position = 0;

                if (!PS2ZIP.TryInflate(fs, outputStream))
                {
                    Console.WriteLine($"[/] Failed to decompress {options.InputPath}.");
                    return;
                }
            }

            File.Move(options.OutputPath + ".out", options.OutputPath, overwrite: true);
            Console.WriteLine($"[/] Decompressed {options.InputPath}.");
        }
    }

    private static void DecryptFile(CryptVerbs options, Keyset keys, string file)
    {
        Console.WriteLine($"[:] Crypting '{file}'..");

        byte[] input = File.ReadAllBytes(file);
        if (!options.UseAlternative)
            CryptoUtils.CryptBuffer(keys, input, input, options.Seed);
        else
            CryptoUtils.CryptBufferAlternative(keys, input, input);

        try
        {
            if (input[0] == 0xC5 && input[1] == 0xEE)
            {
                input = PS2ZIP.Inflate(input);
            }

            File.WriteAllBytes(file, input);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Fail: {e.Message}");
        }
    }

    private static void DecryptFile_Salsa(string file, string salsaKey, bool decryptKeyWithBaseKey = false)
    {
        byte[] baseKey = Convert.FromBase64String(KeysetStore.GT5P_TVBASEKEY);
        byte[] keyBytes = new byte[0x20];

        if (Convert.TryFromBase64String(salsaKey, keyBytes, out int bytesWritten))
        {
            Console.WriteLine("[/] Detected key input as Base64.");
        }
        else
        {
            keyBytes = MiscUtils.StringToByteArray(salsaKey);
        }

        if (decryptKeyWithBaseKey)
        {
            Console.WriteLine("[:] Decrypting input key using GT5P_TVBASEKEY...");

            // Decrypt our movie key
            var salsa = new PDTools.Crypto.Salsa20(baseKey, 0x20);
            salsa.SetIV(new byte[8]);
            salsa.Decrypt(keyBytes, keyBytes.Length);
        }
        
        Console.WriteLine($"[:] Salsa crypting '{file}'..");

        using (FileStream fs = new FileStream(file, FileMode.Open))
        using (FileStream fsOut = new FileStream(file + ".out", FileMode.Create))
        {
            byte[] dataKey = new byte[8];
            using SymmetricAlgorithm salsa20 = new Salsa20SymmetricAlgorithm();
            using var decrypt = salsa20.CreateEncryptor(keyBytes, dataKey);

            byte[] buffer = new byte[0x8000];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                decrypt.TransformBlock(buffer, 0, read, buffer, 0);
                fsOut.Write(buffer, 0, read);
            }
        }

        // Make backup while replacing
        if (File.Exists(file))
            File.Move(file, file + ".bak");

        File.Move(file + ".out", file);

        // If replace was done correctly and backup exists, delete it
        if (File.Exists(file) && File.Exists(file + ".bak"))
            File.Delete(file + ".bak");
    }

    public static void Log(string message, bool forceConsolePrint = false)
    {
        if (PrintToConsole || forceConsolePrint)
            Console.WriteLine(message);

        sw?.WriteLine(message);
    }

    public static void CreateDefaultKeysFile()
    {
        string json = JsonSerializer.Serialize(KeysetStore.Default_Keysets, new JsonSerializerOptions() { WriteIndented = true });

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
                Console.WriteLine(" A default one was created with the keys for the following game codes:");
                foreach (var k in KeysetStore.Default_Keysets)
                    Console.WriteLine($"  - {k.GameCode}");

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
