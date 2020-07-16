# GTToolsSharp
A port of gttool from flatz for Gran Turismo 5/6 to C#. Unfinished yet, also supports PDIPFS. Repack is planned.

The motivation for this tool is to port over the near unreadability of the other tool, along with future features to be planned.

Source code for the PDIPFS path resolver is implemented. Files can be extracted for GT.VOL (main build volume) and PDIPFS (update patches).
To unpack certain builds you will need the keys for each one of them. Only the keys for GT5 (EU) are provided.

## Usage
To Unpack: `GTToolsSharp -i <input GT.VOL or PDIPFS path> -o <Folder to extract to> --unpack (--noprint)`
To Repack: `GTToolsSharp -i <PDIPFS path only> -p <Folder with source files to pack i.e car/decken/00> --packoutputdir <output of repacked files>`

## Repacking files
Probably very buggy and unsafe to use!

Does not build header and table of contents as they seem to break the games even with the correct output file sizes.
Will only work on PDIPFS. Files most likely would have to be existing & already in a PDIPFS path to work.
Make sure to make backups of the files you are reverting. If you get a black screen upon starting the game, revert your files.

## Compiling
If you somehow want to compile this, Visual Studio 2019 Preview & .NET Core 5.0 is required.



