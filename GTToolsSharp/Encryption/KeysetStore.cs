using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GTToolsSharp.Encryption;

namespace GTToolsSharp
{
    public static class KeysetStore
    {
        /// <summary>
        /// Default keys, Gran Turismo 5
        /// </summary>
        public static readonly Keyset Keyset_GT5P_JP_DEMO = new Keyset("GT5P_JP_DEMO", "PDIPFS-071020-02", default, new BufferDecryptManager
        {
            Keys = new()
            {
                { "car/lod/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE4oAo5c" },
                { "car/menu/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE6GuajW" },
                { "car/interior/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE5JUizQ" },
                { "car/meter/00030131", "SoyoGvyMYKCCjcYBCI8rY3GMy9eQlvy3KpEfuL2qZE5iDFjf" },
                { "piece/car_thumb_M/gtr_07_01.img", "cjg1NDJzZDVmNGgyNXM0cnQ2eTJkcjg0Z3pkZmJ3ZmEtdwwS" },

                { "car/lod/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPI1QYGR" },
                { "car/menu/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPJS5l7P" },
                { "car/interior/00200032", "KeaQvtvmSURh566l5+kUB1DmtHtv8OVbCesIXJ0ETPKP9PTn" },
                { "piece/car_thumb_M/impreza_wrx_sti_07_03.img", "cTM0NWgzNTZ5djJoZzRmMTIzNDQ1NjQ1ajZqMjRoNWY4Rqik" },
            }
        });

        public static readonly Keyset Keyset_GT5P_US_SPEC3 = new Keyset("GT5P_US_SPEC_III", "SONORA-550937027", new Key(0x4B0A7FFD, 0xCD1FE36D, 0x504AB1B5, 0x364A172F));
        public static readonly Keyset Keyset_GT5P_EU_SPEC3 = new Keyset("GT5P_EU_SPEC_III", "TOTTORI-562314254", new Key(0x5F29A71B, 0xA80945CF, 0xBECCA74F, 0x07C9800F));
        public static readonly Keyset Keyset_GT5_EU = new Keyset("GT5_EU", "KALAHARI-37863889", new Key(0x2DEE26A7, 0x412D99F5, 0x883C94E9, 0x0F1A7069));
        public static readonly Keyset Keyset_GT5_US = new Keyset("GT5_US", "PATAGONIAN-22798263", new Key(0x5A1A59E5, 0x4D3546AB, 0xF30AF68B, 0x89F08D0D));
        public static readonly Keyset Keyset_GT6 = new Keyset("GT6", "PISCINAS-323419048", new Key(0xAA1B6A59, 0xE70B6FB3, 0x62DC6095, 0x6A594A25));
    }
}
