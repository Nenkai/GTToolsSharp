using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;
using System.IO;

using GTToolsSharp.Utils;
using PDTools.Utils;

namespace GTToolsSharp.BTree
{
    public class StringBTree : BTree<StringKey>
    {
        public StringBTree(Memory<byte> buffer, PFSBTree parentToC)
            : base(buffer, parentToC)
        {

        }



        /// <summary>
        /// Adds a new string to the tree. Returns whether it was actually added.
        /// </summary>
        /// <param name="entry">Entry value.</param>
        /// <param name="entryIndex">Entry index, always returned.</param>
        /// <returns></returns>
        public bool TryAddNewString(string entry, out uint entryIndex)
        {
            StringKey existing = Entries.FirstOrDefault(e => e.Value.Equals(entry));
            if (existing != null)
            {
                entryIndex = (uint)Entries.IndexOf(existing);
                return false;
            }

            var newKey = new StringKey(entry);
            Entries.Add(newKey);
            Entries.Sort(StringLengthSorter);
            entryIndex = (uint)Entries.IndexOf(newKey);
            return true;
        }

        public int GetIndexOfString(string value)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Value == value)
                    return i;
            }

            return -1;
        }

        private static int StringLengthSorter(StringKey value1, StringKey value2)
        {
            string v1 = value1.Value;
            string v2 = value2.Value;

            int min = v1.Length > v2.Length ? v2.Length : v1.Length;
            for (int i = 0; i < min; i++)
            {
                if (v1[i] < v2[i])
                    return -1;
                else if (v1[i] > v2[i])
                    return 1;
            }
            if (v1.Length < v2.Length)
                return -1;
            else if (v1.Length > v2.Length)
                return 1;

            return 0;
        }

        public override int EqualToKeyCompareOp(StringKey key, Span<byte> data)
        {
            throw new NotImplementedException();
        }

        public override int LessThanKeyCompareOp(StringKey key, Span<byte> data)
        {
            throw new NotImplementedException();
        }

        public override StringKey SearchByKey(Span<byte> data)
        {
            throw new NotImplementedException();
        }
    }
}
