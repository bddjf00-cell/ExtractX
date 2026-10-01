using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace ExtractX.Core;

/// <summary>
/// Escritor ZIP con cifrado tradicional PKWARE (ZipCrypto), a pulso:
/// compatible con WinRAR, 7-Zip y el Explorador para descifrar.
/// Estructura: cabecera local + datos cifrados + directorio central + EOCD.
/// </summary>
public static class ZipCryptoWriter
{
    public static async Task WriteAsync(
        List<(string EntryName, string SourcePath, bool IsDir)> items,
        string outputZip, System.IO.Compression.CompressionLevel level,
        string password, IProgress<double>? progress, CancellationToken ct)
    {
        byte[] pwd = Encoding.UTF8.GetBytes(password);
        bool deflate = level != System.IO.Compression.CompressionLevel.NoCompression;
        using var fout = File.Create(outputZip);
        var central = new MemoryStream();
        long offset = 0;
        int done = 0;

        foreach (var (name, src, isDir) in items)
        {
            ct.ThrowIfCancellationRequested();
            string entryName = name.Replace('\\', '/');
            byte[] nameBytes = Encoding.UTF8.GetBytes(entryName);
            DateTime mtime;
            byte[] raw;

            if (isDir)
            {
                if (!entryName.EndsWith('/')) { entryName += "/"; nameBytes = Encoding.UTF8.GetBytes(entryName); }
                mtime = DateTime.Now;
                raw = Array.Empty<byte>();
            }
            else
            {
                mtime = File.GetLastWriteTime(src);
                raw = await File.ReadAllBytesAsync(src, ct);
            }

            uint crc = Crc32(raw);
            byte[] payload = (deflate && !isDir) ? Deflate(raw, level) : raw;
            ushort method = (deflate && !isDir) ? (ushort)8 : (ushort)0;

            // Cabecera de cifrado de 12 bytes (11 aleatorios + byte alto del CRC)
            byte[] encHeader = new byte[12];
            RandomNumberGenerator.Fill(encHeader.AsSpan(0, 11));
            encHeader[11] = (byte)(crc >> 24);

            var keys = InitKeys(pwd);
            byte[] encPayload = new byte[encHeader.Length + payload.Length];
            for (int i = 0; i < encHeader.Length; i++) encPayload[i] = EncryptByte(keys, encHeader[i]);
            for (int i = 0; i < payload.Length; i++) encPayload[i + encHeader.Length] = EncryptByte(keys, payload[i]);

            ushort dosTime = ToDosTime(mtime);
            ushort dosDate = ToDosDate(mtime);
            const ushort flags = 0x0801; // bit 0: cifrado · bit 11: UTF-8

            using (var lh = new BinaryWriter(fout, Encoding.UTF8, leaveOpen: true))
            {
                lh.Write(0x04034B50u);
                lh.Write((ushort)20);
                lh.Write(flags);
                lh.Write(method);
                lh.Write(dosTime);
                lh.Write(dosDate);
                lh.Write(crc);
                lh.Write((uint)encPayload.Length);
                lh.Write((uint)raw.Length);
                lh.Write((ushort)nameBytes.Length);
                lh.Write((ushort)0);
                lh.Write(nameBytes);
            }
            await fout.WriteAsync(encPayload, ct);

            using (var ch = new BinaryWriter(central, Encoding.UTF8, leaveOpen: true))
            {
                ch.Write(0x02014B50u);
                ch.Write((ushort)(63 << 8 | 20));
                ch.Write((ushort)20);
                ch.Write(flags);
                ch.Write(method);
                ch.Write(dosTime);
                ch.Write(dosDate);
                ch.Write(crc);
                ch.Write((uint)encPayload.Length);
                ch.Write((uint)raw.Length);
                ch.Write((ushort)nameBytes.Length);
                ch.Write((ushort)0);
                ch.Write((ushort)0);
                ch.Write((ushort)0);
                ch.Write((ushort)0);
                ch.Write(isDir ? 0x10u << 16 : 0x20u << 16);
                ch.Write((uint)offset);
                ch.Write(nameBytes);
            }
            offset += 30 + nameBytes.Length + encPayload.Length;

            done++;
            progress?.Report(items.Count == 0 ? 100 : done * 100.0 / items.Count);
        }

        long centralStart = offset;
        central.Seek(0, SeekOrigin.Begin);
        await central.CopyToAsync(fout, ct);
        using (var eo = new BinaryWriter(fout, Encoding.UTF8, leaveOpen: true))
        {
            eo.Write(0x06054B50u);
            eo.Write((ushort)0);
            eo.Write((ushort)0);
            eo.Write((ushort)items.Count);
            eo.Write((ushort)items.Count);
            eo.Write((uint)central.Length);
            eo.Write((uint)centralStart);
            eo.Write((ushort)0);
        }
    }

    private static byte[] Deflate(byte[] raw, System.IO.Compression.CompressionLevel level)
    {
        using var ms = new MemoryStream();
        using (var df = new DeflateStream(ms, level, leaveOpen: true))
            df.Write(raw, 0, raw.Length);
        return ms.ToArray();
    }

    // ---- ZipCrypto ----

    private static uint[] InitKeys(byte[] pwd)
    {
        var k = new uint[] { 0x12345678, 0x23456789, 0x34567890 };
        foreach (byte c in pwd) UpdateKeys(k, c);
        return k;
    }

    private static void UpdateKeys(uint[] k, byte c)
    {
        k[0] = Crc32Update(k[0], c);
        k[1] = (k[1] + (k[0] & 0xFF)) * 134775813 + 1;
        k[2] = Crc32Update(k[2], (byte)(k[1] >> 24));
    }

    private static byte EncryptByte(uint[] k, byte plain)
    {
        uint temp = (k[2] | 2);
        byte enc = (byte)(plain ^ ((temp * (temp ^ 1)) >> 8 & 0xFF));
        UpdateKeys(k, plain);
        return enc;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data) crc = Crc32Update(crc, b);
        return ~crc;
    }

    private static uint Crc32Update(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }

    private static ushort ToDosTime(DateTime dt) =>
        (ushort)(((dt.Hour & 0x1F) << 11) | ((dt.Minute & 0x3F) << 5) | ((dt.Second / 2) & 0x1F));

    private static ushort ToDosDate(DateTime dt) =>
        (ushort)((((dt.Year - 1980) & 0x7F) << 9) | (((int)dt.Month & 0xF) << 5) | ((int)dt.Day & 0x1F));
}
