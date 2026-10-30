using System.IO.Compression;

namespace WinMac.Dock.Imaging;

/// <summary>Codificador PNG mínimo (RGBA 8 bits, sin dependencias). Sirve para entregar renders fuera de pantalla desde CI.</summary>
public static class PngWriter
{
    public static byte[] EncodeRgba(byte[] rgba, int width, int height)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("Tamaño de búfer incorrecto.");
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width); WriteBE(ihdr, 4, height); ihdr[8] = 8; ihdr[9] = 6; // 8 bits, RGBA
        Chunk(ms, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[width * 4 + 1];
            for (int y = 0; y < height; y++)
            {
                row[0] = 0; Buffer.BlockCopy(rgba, y * width * 4, row, 1, width * 4);
                z.Write(row, 0, row.Length);
            }
        }
        Chunk(ms, "IDAT", raw.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>Convierte BGRA premultiplicado (Direct2D) en RGBA directo, compuesto sobre un fondo opaco opcional.</summary>
    public static byte[] BgraPremulToRgba(byte[] bgra, int stride, int width, int height)
    {
        var o = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int s = y * stride + x * 4, d = (y * width + x) * 4;
                byte a = bgra[s + 3];
                if (a == 0) { o[d] = o[d + 1] = o[d + 2] = 0; o[d + 3] = 0; continue; }
                o[d] = (byte)Math.Min(255, bgra[s + 2] * 255 / a); o[d + 1] = (byte)Math.Min(255, bgra[s + 1] * 255 / a);
                o[d + 2] = (byte)Math.Min(255, bgra[s] * 255 / a); o[d + 3] = a;
            }
        return o;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc32(t, 0xFFFFFFFF); crc = Crc32(data, crc) ^ 0xFFFFFFFF;
        var c = new byte[4]; WriteBE(c, 0, (int)crc); s.Write(c);
    }
    private static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static readonly uint[] Table = BuildTable();
    private static uint[] BuildTable() { var t = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[n] = c; } return t; }
    private static uint Crc32(byte[] d, uint crc) { foreach (var b in d) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8); return crc; }
}
