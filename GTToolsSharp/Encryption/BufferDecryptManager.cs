using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTToolsSharp.Encryption;

/// <summary>
/// Used for decryption of specific keys (used in GT5P JP Demo).
/// </summary>
public class BufferDecryptManager
{
    /// <summary>
    /// Keys for decryption. (Path, base64 Key)
    /// </summary>
    public Dictionary<string, string> Keys { get; set; }

}
