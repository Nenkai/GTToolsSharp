# GTToolsSharp
A port of gttool from flatz for Gran Turismo 5/6 to C#.

The motivation for this tool is to port over the near unreadability of the other tool, along with extra features.

### Features
* Unpacking from any PS3-era Gran Turismo. (You'll need the keys to decrypt the volumes).
* Unpacking **Patch File Systems** (PDIPFS) from game updates (except 1.06 Bsdiff GT6 files, planned)
* **Packing files for modding** (PDIPFS only, packing an entire 10+gigs GT.VOL is kind of pointless).
* Packing modified files and marking them as new file entries, so you can have a mod that edits current game files while having them as new files to easily revert by backing up just the volume header file. (More in the packing section)

Files can be extracted for GT.VOL (main build volume) and PDIPFS (update patches).

To unpack/pack certain builds you will need the keys for each one of them. Only the keys for GT5 (EU) are provided.

## Usage
### Unpacking
Input: `GTToolsSharp unpack -i <input GT.VOL or PDIPFS path> -o <Folder to extract to> (--noprint)`

Examples:
  * Normal Unpack: `GTToolsSharp unpack -i PDIPFS -o PDIPFS_EXTRACTED`

### (Re)Packing (PDIPFS only)
Input: `GTToolsSharp pack -i <PDIPFS> --folder-to-pack <Folder with source files to pack i.e car/decken/00> -o <output of repacked files>`

Examples:
  * Normal Pack: `GTToolsSharp pack -i PDIPFS --folder-to-pack RepackInput -o RepackedFiles`
  * To Delete files: `GTToolsSharp pack -i PDIPFS  --folder-to-pack RepackInput -o RepackedFiles --packremovefiles` (Needs files_to_remove.txt in current folder)
  
Recommended usage is to **not** to pack to the same input folder. If your input folder is PDIPFS (original), your output folder should not also be PDIPFS.

**Make sure to make backups of the files you are reverting. If you get a black screen upon starting the game, revert your files.**

#### Packing files as new (Advanced users/modders)
Packing files as new means that new file entries are added to the volume table of contents pointing to the same old game files, rather than edited. That means that upon packing, new scrambled file names are generated, and do not interfere with any of the other original game files. The only file that is edited is the volume header file which is always located at `PDIPFS/K/4D`. 


The advantage of doing this is that players of your mods only have to backup this one file when applying your mods instead of all the files which would overwrite. The only inconvinience is that since the file names are scrambled, it is easy to forget which files are actually used or not.
This method is overall prefered over basic packing to avoid potentially accidental original game file overwrites (as long as your mod is not very sized (>100mb)).

## Compiling
If you anyhow want to compile this, Visual Studio 2019 Preview & .NET Core 5.0 is required.



