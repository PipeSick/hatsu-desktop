using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace WinMac.Apps;

/// <summary>
/// Caché de iconos en memoria (LRU) y en disco (formato propio «WMI1»: cabecera + BGRA comprimido con zlib). La caché de disco guarda además el «sello» del origen
/// (tamaño + fecha del archivo): si el ejecutable cambia, la entrada se descarta. Una entrada de disco corrupta se ignora y se borra; nunca lanza hacia el llamador.
/// </summary>
public sealed class IconCache
{
    private readonly int _max; private readonly string? _dir;
    private readonly Dictionary<string, LinkedListNode<(string Key, IconBitmap Bmp)>> _map = new(); private readonly LinkedList<(string Key, IconBitmap Bmp)> _lru = new();
    private readonly object _lock = new();

    public IconCache(int maxMemoryItems = 512, string? diskDirectory = null) { _max = Math.Max(1, maxMemoryItems); _dir = diskDirectory; }

    public int MemoryCount { get { lock (_lock) return _map.Count; } }

    public IconBitmap? GetMemory(string key)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var n)) return null;
            _lru.Remove(n); _lru.AddFirst(n); return n.Value.Bmp;
        }
    }

    public void PutMemory(string key, IconBitmap bmp)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var old)) { _lru.Remove(old); _map.Remove(key); }
            _map[key] = _lru.AddFirst((key, bmp));
            while (_map.Count > _max) { var last = _lru.Last!; _lru.RemoveLast(); _map.Remove(last.Value.Key); }
        }
    }

    public void Remove(string key) { lock (_lock) { if (_map.TryGetValue(key, out var n)) { _lru.Remove(n); _map.Remove(key); } } if (_dir is not null) TryDelete(DiskPath(key)); }

    // ---------------------------------------------------------------- disco
    private string DiskPath(string key) => Path.Combine(_dir!, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + ".wmicon");

    public IconBitmap? GetDisk(string key, string stamp)
    {
        if (_dir is null) return null; string path = DiskPath(key);
        IconBitmap? bmp = null; bool bad = false;
        try
        {
            if (!File.Exists(path)) return null;
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (br.ReadInt32() != 0x31494D57 /* "WMI1" */) bad = true;
                else
                {
                    string st = br.ReadString(); int w = br.ReadInt32(), h = br.ReadInt32();
                    if (st != stamp || w < 1 || h < 1 || w > IconValidator.MaxSide || h > IconValidator.MaxSide) bad = true;
                    else
                    {
                        var px = new byte[w * h * 4]; int got = 0;
                        using (var z = new ZLibStream(fs, CompressionMode.Decompress)) { while (got < px.Length) { int n = z.Read(px, got, px.Length - got); if (n <= 0) break; got += n; } }
                        var candidate = got == px.Length ? new IconBitmap(w, h, px) : null;
                        if (candidate is not null && IconValidator.IsValid(candidate)) bmp = candidate; else bad = true;
                    }
                }
            }
        }
        catch (Exception) { bad = true; }
        if (bad) TryDelete(path);   // fuera del using: en Windows no se puede borrar un archivo con el manejador abierto
        return bmp;
    }

    public void PutDisk(string key, string stamp, IconBitmap bmp)
    {
        if (_dir is null) return;
        try
        {
            Directory.CreateDirectory(_dir); string path = DiskPath(key), tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) using (var bw = new BinaryWriter(fs))
            {
                bw.Write(0x31494D57); bw.Write(stamp); bw.Write(bmp.Width); bw.Write(bmp.Height); bw.Flush();
                using var z = new ZLibStream(fs, CompressionLevel.Fastest, leaveOpen: true); z.Write(bmp.Bgra, 0, bmp.Bgra.Length);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception) { /* la caché de disco es opcional */ }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }
}
