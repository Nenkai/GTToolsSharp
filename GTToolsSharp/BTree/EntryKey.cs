using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    public class EntryKey
    {
        public EntryKeyFlags Flags;
        public uint NameIndex;
        public uint ExtIndex;
        public uint LinkIndex;
    }

    [Flags]
    public enum EntryKeyFlags
    {
        Directory = 0x01,
        File = 0x02,
    }
}
