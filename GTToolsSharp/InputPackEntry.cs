using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp;

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

    /// <summary>
    /// When the file was last modified to use against packing cache.
    /// </summary>
    public DateTime LastModified { get; set; }

    public bool IsAddedFile { get; set; }

    /// <summary>
    /// MD5 checksum of the file, mostly used for patching.
    /// </summary>
    public string MD5Checksum { get; set; }
}
