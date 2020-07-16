using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.IO.Compression;

using GTToolsSharp.BTree;

using Syroot.BinaryData.Memory;

namespace GTToolsSharp
{
    public class EntryPacker
    {
        private GTVolume _volume;
        private string ParentDirectory;
        private string OutDir;

        public EntryPacker(GTVolume baseVolume, string outDir, string parentDir)
        {
            _volume = baseVolume;
            OutDir = outDir;
            ParentDirectory = parentDir;
        }

        public unsafe void PackFromKey(FileEntryKey entryKey)
        {
            string entryPath = _volume.GetEntryPath(entryKey, ParentDirectory);
            if (string.IsNullOrEmpty(entryPath))
            {
                Program.Log($"Could not determine entry path for Entry key at name Index {entryKey.NameIndex}");
                return;
            }

            if (entryKey.Flags.HasFlag(EntryKeyFlags.Directory))
            {
                var childEntryBTree = new FileEntryBTree(_volume.TOCData, (int)_volume.RootAndFolderOffsets[(int)entryKey.DirEntryIndex]);
                var childPacker = new EntryPacker(_volume, OutDir, entryPath);
                childEntryBTree.TraverseAndPack(childPacker);
            }
            else if (entryKey.Flags.HasFlag(EntryKeyFlags.File))
            {
                if (_volume.FilesToPack.TryGetValue(entryPath, out InputPackEntry packEntry))
                {
                    var nodeBTree = new FileInfoBTree(_volume.TOCData, (int)_volume.NodeTreeOffset);
                    var nodeKey = new FileInfoKey(entryKey.DirEntryIndex);

                    uint nodeIndex = nodeBTree.SearchIndexByKey(nodeKey);

                    // Get our target file
                    byte[] fileData = File.ReadAllBytes(packEntry.FullPath);

                    // Prepare the scrambled file path
                    string outPath = Path.Combine(this.OutDir, PDIPFSPathResolver.GetPathFromSeed(nodeKey.FileIndex)).Replace('\\', '/');

                    byte[] finalData;
                    if ((nodeKey.Flags & 0xF) != 0) // Compressed?
                    {
                        // Create the file in memory
                        using var outputStream = new MemoryStream();
                        using var bs = new BinaryWriter(outputStream);

                        bs.Write(GTVolume.ZLIB_MAGIC);
                        bs.Write(0u - packEntry.FileSize);

                        using (var ds = new DeflateStream(outputStream, CompressionMode.Compress, leaveOpen: true))
                            ds.Write(fileData, 0, fileData.Length);

                        // Get the size for it
                        finalData = outputStream.ToArray();
                    }

                    _volume.CryptData(finalData, nodeKey.FileIndex);

                    // Write it.
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    File.WriteAllBytes(outPath, finalData);
                    Program.Log($"[/] Packed {packEntry.VolumeDirPath} -> {outPath}", true);

                    // Edit TOC. (Disabled for now, until toc stuff is figured out)
                    /*
                    nodeKey.CompressedSize = (uint)compData.Length;
                    nodeKey.UncompressedSize = (uint)fileData.Length;
                    SpanWriter sw = new SpanWriter(_volume.TOCData, Syroot.BinaryData.Core.Endian.Big);
                    sw.Position = (int)nodeKey.KeyOffset;
                    sw.Position += 1; // Skip flags

                    // Really dirty, just to advance to where we need to be
                    SpanReader sr = new SpanReader(sw.Span, Syroot.BinaryData.Core.Endian.Big);
                    sr.Position = sw.Position;
                    Utils.DecodeBitsAndAdvance(ref sr);
                    sw.Position = sr.Position;

                    EncodeAndAdvance(ref sw, nodeKey.CompressedSize);
                    EncodeAndAdvance(ref sw, nodeKey.UncompressedSize);
                    */
                }
            }
        }

        public static void EncodeAndAdvance(ref SpanWriter sw, uint value)
        {
            uint mask = 0x80;
            Span<byte> buffer = Array.Empty<byte>();

            if (value <= 0x7F)
            {
                sw.WriteByte((byte)value);
                return;
            }
            else if (value <= 0x3FFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(2, 2);
            }
            else if (value <= 0x1FFFFF)
            {
                Span<byte> tempBuf = BitConverter.GetBytes(value).AsSpan();
                tempBuf.Reverse();
                buffer = tempBuf.Slice(1, 3);
            }
            else if (value <= 0xFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
            }
            else if (value <= 0xFFFFFFFF)
            {
                buffer = BitConverter.GetBytes(value);
                buffer.Reverse();
                buffer = new byte[] { 0, buffer[0], buffer[1], buffer[2], buffer[3] };
            }
            else
                throw new Exception("????");

            for (int i = 1; i < buffer.Length; i++)
            {
                buffer[0] += (byte)mask;
                mask >>= 1;
            }

            sw.WriteBytes(buffer);
        }
    }
}
