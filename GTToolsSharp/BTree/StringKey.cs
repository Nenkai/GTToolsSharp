using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.BTree
{
    public class StringKey
    {
        public StringKey(string val)
            => Value = val;

        public string Value { get; private set; }
    }
}
