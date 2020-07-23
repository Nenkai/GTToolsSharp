# GTToolsSharp
A port of gttool from flatz for Gran Turismo 5/6 to C#.


The motivation for this tool is to port over the near unreadability of the other tool, along with extra features. Packing support is implemented, you can add files and edit from the game. There is no support for GT Sport.

Files can be extracted for GT.VOL (main build volume) and PDIPFS (update patches).


To unpack/pack certain builds you will need the keys for each one of them. Only the keys for GT5 (EU) are provided.

## Usage
* To Unpack: `GTToolsSharp -i <input GT.VOL or PDIPFS path> -o <Folder to extract to> --unpack (--noprint)`
  * Example: `GTToolsSharp -i PDIPFS -o PDIPFS_EXTRACTED --unpack`


* To Repack: `GTToolsSharp -i <PDIPFS path only> -p <Folder with source files to pack i.e car/decken/00> --packoutputdir <output of repacked files>`
  * Example: `GTToolsSharp -i PDIPFS -p RepackInput --packoutputdir RepackedFiles`
  * To Delete files: `GTToolsSharp -i PDIPFS -p RepackInput --packoutputdir RepackedFiles --packremovefiles` (Needs files_to_remove.txt in current folder)
## Repacking files
Does not repack GT.VOL.

Make sure to make backups of the files you are reverting. If you get a black screen upon starting the game, revert your files.


## Compiling
If you somehow want to compile this, Visual Studio 2019 Preview & .NET Core 5.0 is required.



