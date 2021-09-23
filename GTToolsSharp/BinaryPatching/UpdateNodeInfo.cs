using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

namespace GTToolsSharp.BinaryPatching
{
    public class UpdateNodeInfo
    {
        public Dictionary<uint, NodeInfo> Entries { get; private set; } = new Dictionary<uint, NodeInfo>();

        public void ParseNodeInfo(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("UPDATENODEINFO path does not exist on local filesystem.");

            using var sr = new StreamReader(path);

            int line = 0;
            string currentLine;
            while (sr.Peek() != -1)
            {
                line++;

                currentLine = sr.ReadLine();
                string[] args = currentLine.Split(' ');
                if (args.Length < 7)
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} needs 7 arguments, has {args.Length} ({currentLine})");
                    continue;
                }

                if (!uint.TryParse(args[0], out uint newEntryIndex))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse new entry index [0]");
                    continue;
                }

                if (!uint.TryParse(args[1], out uint newFileSize))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse new file size [1]");
                    continue;
                }

                if (!byte.TryParse(args[2], out byte oldFileState))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse old file state [2]");
                    continue;
                }

                if (!uint.TryParse(args[3], out uint currentEntryIndex))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse current entry index [3]");
                    continue;
                }

                if (!byte.TryParse(args[5], out byte newFileState))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse new file state [5]");
                    continue;
                }

                if (!uint.TryParse(args[6], out uint compressedFileSize))
                {
                    Program.Log($"[X] UPDATENODEINFO: Line {line} - could not parse compressed file size [6]");
                    continue;
                }

                var info = new NodeInfo();
                info.NewEntryIndex = newEntryIndex;
                info.NewFileSize = newFileSize;
                info.OldFileInfoFlags = (TPPSFileState)oldFileState;
                info.CurrentEntryIndex = currentEntryIndex;
                info.NewCompressedFileSize = compressedFileSize;
                info.NewFileInfoFlags = (TPPSFileState)newFileState;
                info.MD5Checksum = args[4];

                Entries.Add(info.NewEntryIndex, info);
            }
        }

        public void WriteNodeInfo(string outputPath)
        {
            using var sw = new StreamWriter(outputPath);
            foreach (var entry in Entries.Values)
            {
                sw.WriteLine($"{entry.NewEntryIndex:D6} {entry.NewFileSize:D10} {(byte)entry.OldFileInfoFlags}" +
                    $" {entry.CurrentEntryIndex:D6} {entry.MD5Checksum} {(byte)entry.NewFileInfoFlags} {entry.NewCompressedFileSize:D10} {entry.NewCompressedFileSize:D10}");
            }
        }

        public bool TryGetEntry(uint index, out NodeInfo info)
            => Entries.TryGetValue(index, out info);

    }
    
    [Flags]
    public enum TPPSFileState
    {
        Uncompressed,
        Compressed,
        CompressedInAndOut
    }
}
