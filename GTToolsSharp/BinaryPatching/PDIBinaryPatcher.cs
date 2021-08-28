using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

namespace GTToolsSharp.BinaryPatching
{
    public class PDIBinaryPatcher
    {
        public Dictionary<uint, NodeInfo> Entries { get; private set; } = new Dictionary<uint, NodeInfo>();

        public static PDIBinaryPatcher ParseNodeInfo(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("UPDATENODEINFO path does not exist on local filesystem.");

            using var sr = new StreamReader(path);


            string currentLine;
            while (sr.Peek() != -1)
            {
                currentLine = sr.ReadLine();
                string[] args = currentLine.Split(' ');
                if (args.Length < 6)
                {
                    Program.Log($"[X] UPDATENODEINFO: Position {sr.BaseStream.Position} needs 6 arguments, has {args.Length} ({currentLine})");
                    continue;
                }


            }

            return null;
        }
    }
}
