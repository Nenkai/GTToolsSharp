using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GTToolsSharp.BTree;

namespace GTToolsSharp.BinaryPatching
{
    public class NodeInfo
    {
        /// <summary>
        /// The new entry for the file info.
        /// </summary>
        public uint NewEntryIndex { get; set; }

        /// <summary>
        /// The new expected file size for the file info.
        /// </summary>
        public uint NewFileSize { get; set; }
        
        /// <summary>
        /// The new expected file flags for the file info.
        /// </summary>
        public FileInfoFlags NewFileInfoFlags { get; set; }

        /// <summary>
        /// The older entry index for the file info.
        /// </summary>
        public uint CurrentEntryIndex { get; set; }

        /// <summary>
        /// The older file info flags.
        /// </summary>
        public FileInfoFlags OldFileInfoFlags { get; set; }

        /// <summary>
        /// The older compressed file size.
        /// </summary>
        public uint NewCompressedFileSize { get; set; }
    }
}
