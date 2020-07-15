using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp
{
    public class InputPackEntry
    {
        /// <summary>
        /// Full path on the file system.
        /// </summary>
        public string FullPath { get; set; }

        /// <summary>
        /// Path relative to the volume.
        /// </summary>
        public string VolumeDirPath { get; set; }

        /// <summary>
        /// Uncompressed file size of this entry.
        /// </summary>
        public uint FileSize { get; set; }
    }
}
