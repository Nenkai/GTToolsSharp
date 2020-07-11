using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    public class SearchResult
    {
        public uint lowerBound;
        public uint upperBound;

        public uint index = ~0u;
        public uint maxIndex;
    }
}
