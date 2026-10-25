using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GTToolsSharp.Encryption;

/// <summary>
/// Version-specific constants for GT7 builds after 1.00, loaded from a user-supplied JSON file
/// (--keyset); none are shipped here. Fields: "build" (label), "index_state" (64 bytes, hex),
/// "crc_seed", "header_magic", "volume_data_key" (32 bytes, hex) and "mph_hash" {"init",
/// "hash_mul", "seed_mul", "seed_add", "char_mul", "char_add"} (hex strings).
/// Index: ChaCha20 whose ENTIRE 16-word initial state is <see cref="IndexState"/> (word 12 = block
/// counter), then the CRC un-scramble of the first 0x800 bytes seeded with <see cref="CrcSeed"/>.
/// The header after that is little-endian (FileDeviceMPHSuperintendentHeaderV2).
/// </summary>
public class GT7Keyset
{
    public static GT7Keyset Current { get; set; }

    public string Build { get; set; }
    public byte[] IndexState { get; set; }
    public uint CrcSeed { get; set; }
    public uint HeaderMagic { get; set; }
    public byte[] VolumeDataKey { get; set; }

    public uint MphHashInit { get; set; }
    public ulong MphHashMul { get; set; }
    public uint MphSeedMul { get; set; }
    public uint MphSeedAdd { get; set; }
    public ulong MphCharMul { get; set; }
    public ulong MphCharAdd { get; set; }

    public static GT7Keyset Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement r = doc.RootElement;
        JsonElement mph = r.GetProperty("mph_hash");

        var k = new GT7Keyset
        {
            Build = r.TryGetProperty("build", out var b) ? b.GetString() : null,
            IndexState = Convert.FromHexString(r.GetProperty("index_state").GetString()),
            CrcSeed = (uint)Hex(r.GetProperty("crc_seed")),
            HeaderMagic = (uint)Hex(r.GetProperty("header_magic")),
            VolumeDataKey = Convert.FromHexString(r.GetProperty("volume_data_key").GetString()),
            MphHashInit = (uint)Hex(mph.GetProperty("init")),
            MphHashMul = Hex(mph.GetProperty("hash_mul")),
            MphSeedMul = (uint)Hex(mph.GetProperty("seed_mul")),
            MphSeedAdd = (uint)Hex(mph.GetProperty("seed_add")),
            MphCharMul = Hex(mph.GetProperty("char_mul")),
            MphCharAdd = Hex(mph.GetProperty("char_add")),
        };

        if (k.IndexState.Length != 64)
            throw new InvalidDataException("index_state must be 64 bytes");
        if (k.VolumeDataKey.Length != 32)
            throw new InvalidDataException("volume_data_key must be 32 bytes");
        return k;
    }

    private static ulong Hex(JsonElement e)
    {
        string s = e.GetString();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        return ulong.Parse(s, NumberStyles.HexNumber);
    }

    /// <summary>ChaCha20 with a caller-supplied full initial state; word 12 = 64-byte block index.</summary>
    public void DecryptIndex(Span<byte> data)
    {
        Span<uint> init = stackalloc uint[16];
        Span<uint> x = stackalloc uint[16];
        Span<byte> ks = stackalloc byte[64];
        for (int i = 0; i < 16; i++)
            init[i] = BinaryPrimitives.ReadUInt32LittleEndian(IndexState.AsSpan(i * 4));

        for (int block = 0, pos = 0; pos < data.Length; block++, pos += 64)
        {
            init[12] = (uint)block;
            init.CopyTo(x);
            for (int r = 0; r < 10; r++)
            {
                QR(x, 0, 4, 8, 12); QR(x, 1, 5, 9, 13); QR(x, 2, 6, 10, 14); QR(x, 3, 7, 11, 15);
                QR(x, 0, 5, 10, 15); QR(x, 1, 6, 11, 12); QR(x, 2, 7, 8, 13); QR(x, 3, 4, 9, 14);
            }
            for (int i = 0; i < 16; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(ks.Slice(i * 4), x[i] + init[i]);

            int n = Math.Min(64, data.Length - pos);
            for (int i = 0; i < n; i++)
                data[pos + i] ^= ks[i];
        }
    }

    private static void QR(Span<uint> x, int a, int b, int c, int d)
    {
        x[a] += x[b]; x[d] = uint.RotateLeft(x[d] ^ x[a], 16);
        x[c] += x[d]; x[b] = uint.RotateLeft(x[b] ^ x[c], 12);
        x[a] += x[b]; x[d] = uint.RotateLeft(x[d] ^ x[a], 8);
        x[c] += x[d]; x[b] = uint.RotateLeft(x[b] ^ x[c], 7);
    }
}
