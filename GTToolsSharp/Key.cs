using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp
{
    public struct Key
    {
        public uint[] Data { get; set; }

        public Key(uint data1, uint data2, uint data3, uint data4)
        {
            Data = new[] { data1, data2, data3, data4 };
        }
    }
}
