using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;
using Syroot.BinaryData.Core;
using Syroot.BinaryData.Memory;

namespace GTToolsSharp.Headers
{
    public class FileDeviceGTFS2Header : VolumeHeaderBase
    {
        private static readonly byte[] HeaderMagic = { 0x5B, 0x74, 0x51, 0x62 };

        public override int HeaderSize => 0xA0;

        public ulong PFSVersion { get; set; }

        /// <summary>
        /// Total size of the volume.
        /// </summary>
        public ulong TotalVolumeSize { get; set; }

        /// <summary>
        /// Title ID for this header.
        /// </summary>
        public string TitleID { get; set; }

        public bool HasCustomGameID { get; set; }

        public override void Read(Span<byte> buffer)
        {
            SpanReader sr = new SpanReader(buffer, Endian.Big);

            Magic = sr.ReadBytes(4);
            ToCNodeIndex = sr.ReadUInt32();
            CompressedTOCSize = sr.ReadUInt32();
            ExpandedTOCSize = sr.ReadUInt32();
            SerialNumber = sr.ReadUInt64();
            TotalVolumeSize = sr.ReadUInt64();
            TitleID = sr.ReadString0();
        }

        public override byte[] Serialize()
        {
            throw new NotImplementedException();
        }
    }
}
