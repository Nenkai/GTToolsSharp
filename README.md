# GTToolsSharp
A port of gttool from flatz for Gran Turismo 5/6 to C# which allows for endless modding possibilities.

The main motivation for writing this tool was to allow modding GT5/6 through repacking volumes.

### Features
* Unpacking from any PS3-era Gran Turismo. (You'll need the keys to decrypt the volumes).
* **Packing files for modding** (PDIPFS only) (Read packing sections)
* Unpacking **Patch File Systems** (PDIPFS) from game updates.
* Unpacking GT Sport Volumes (gt01.vol->gt99.vol & others).
* Unpacking files for older version of volumes such as GT5 Prologue JP Demo
* Creating online update patches for GT6

Files can be extracted for GT.VOL (main build volume) and PDIPFS (update patches).
To unpack/pack certain builds you will need the keys for each one of them. Only the keys for GT5P, GT5 (EU/US) and GT6 are provided.

# [DOWNLOAD LINK](https://github.com/Nenkai/GTToolsSharp/releases)

## Usage
### Unpacking
Input: `GTToolsSharp unpack -i <input GT.VOL or PDIPFS path> -o <Folder to extract to>`

Examples:
  * Normal Unpack: `GTToolsSharp unpack -i PDIPFS -o PDIPFS_EXTRACTED`

### (Re)Packing (PDIPFS only)
Input: `GTToolsSharp pack -i <PDIPFS> -p <Folder with source files to pack> -o <output of repacked files>`

Examples:
  * Normal Pack: `GTToolsSharp pack -i PDIPFS -p RepackInput -o RepackedFiles`
  * To Delete files: `GTToolsSharp pack -i PDIPFS  -p RepackInput -o RepackedFiles --remove-files` (Needs files_to_remove.txt in current folder)
  Where `RepackInput` must contain the same game paths for files, i.e `RepackInput/piece/gt5/course_logo_LL/akasaka.img`
Recommended usage is to **not** to pack to the same input folder. If your input folder is PDIPFS (original), your output folder should not be PDIPFS.

**Make sure to make backups of the files you are reverting. If you get a black screen upon starting the game, revert your files.**

## Advanced Packing Notes (Modders Read)
The Gran Turismo 5 and above uses a file system that allows editing existing files while keeping the actual original files intact. This allows for extremely easy modding and easy revert method.

Important things to know:
1. *Main Volume Header File* - **Always** `PDIPFS/K/4D`
2. Table of Contents file (*TOC*) which contains all of file entries (each having an *entry number*) that indicates where an original file is located in the scrambled PDIPFS - location of TOC determined by the master file.
3. The *entry number* is used to determine the PDIPFS scrambled path.

Files packed as new with means that new entries numbers are appended to last one in *TOC* which are the same original paths, but with new entry numbers thus new files are generated, and the older file entries used for these paths are unused. That means that upon packing, new scrambled file names are generated, and do not interfere with any of the other original game files. The only file that is edited is the *Main Volume Header File* which is always located at `PDIPFS/K/4D`.

The advantage of doing this is that players of your mods only have to backup this one file when applying your mods instead of all the files which would overwrite. To revert, players can simply revert their original `PDIPFS/K/4D` file. You can provide the original `PDIPFS/K/4D` file inside your mod as a way for them to revert if they did not back it up.

If you want to use an existing mod as a base mod use the base mod's `PDIPFS` since it will contain the newer files, TOC, and header.
* First pack: `GTToolsSharp pack -i PDIPFS -p MyModdedFiles -o PDIPFS_MOD`
* Next packs: `GTToolsSharp pack -i PDIPFS_MOD -p MyModdedFiles -o PDIPFS_MOD2 (for a new folder again)`

Unless you are using a mod as base (and don't have the source files of the mod), keep it one packing only, from your original PDIPFS to your final pack folder.

If you would like your mod to actually overwrite *TOC* entries and original files, use `--pack-as-overwrite`.

## Compiling
If you anyhow want to compile this, Visual Studio 2019 Preview & .NET Core 5.0 is required.

## Credits

flatz for all his invaluable work on volumes, from GT5 to 7.


