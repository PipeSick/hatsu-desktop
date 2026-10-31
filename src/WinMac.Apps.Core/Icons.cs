namespace WinMac.Apps;

/// <summary>Píxeles BGRA con alfa PREMULTIPLICADO, filas de arriba abajo. Conserva el tamaño y la proporción originales (no se recorta ni se fuerza a cuadrado).</summary>
public sealed record IconBitmap(int Width, int Height, byte[] Bgra);

public readonly record struct IconRequest(AppIdentity App, string? OverridePath = null, int Size = 256)
{
    /// <summary>Clave lógica (sin sello de modificación). Incluye el icono alternativo: cambiarlo produce otra clave.</summary>
    public string Key => $"{App.Id}|{OverridePath ?? ""}|{Size}";
}

public static class IconValidator
{
    public const int MaxSide = 1024;

    /// <summary>Un icono corrupto o vacío no debe llegar nunca al render. Devuelve el motivo del rechazo o null si es válido.</summary>
    public static string? Reject(IconBitmap? b)
    {
        if (b is null) return "nulo";
        if (b.Width < 1 || b.Height < 1 || b.Width > MaxSide || b.Height > MaxSide) return $"dimensiones fuera de rango ({b.Width}x{b.Height})";
        if (b.Bgra is null || b.Bgra.Length != b.Width * b.Height * 4) return "tamaño de búfer incoherente";
        if ((long)Math.Max(b.Width, b.Height) > 8L * Math.Min(b.Width, b.Height)) return "proporción absurda";
        int visible = 0; for (int i = 3; i < b.Bgra.Length; i += 4) if (b.Bgra[i] > 8) visible++;
        if (visible < Math.Max(4, b.Width * b.Height / 200)) return "prácticamente transparente";
        return null;
    }

    public static bool IsValid(IconBitmap? b) => Reject(b) is null;
}

/// <summary>Ajuste a una caja SIN deformar: mantiene la proporción y centra. Es lo que debe usar el render para dibujar cualquier icono.</summary>
public static class IconLayout
{
    public static (double X, double Y, double W, double H) AspectFit(int srcW, int srcH, double boxX, double boxY, double boxW, double boxH)
    {
        double s = Math.Min(boxW / srcW, boxH / srcH); double w = srcW * s, h = srcH * s;
        return (boxX + (boxW - w) / 2, boxY + (boxH - h) / 2, w, h);
    }
}

/// <summary>Color del mosaico de reemplazo, DETERMINISTA (FNV-1a + mezcla): el mismo Id da siempre el mismo tono, entre procesos y ejecuciones.</summary>
public static class FallbackIcon
{
    public static double Hue(string id)
    {
        uint h = 2166136261; foreach (char c in id) { h ^= c; h *= 16777619; }
        h ^= h >> 16; h *= 0x85ebca6b; h ^= h >> 13; h *= 0xc2b2ae35; h ^= h >> 16;   // mezcla final (FNV solo agrupa ids parecidos)
        return (h % 360) / 360.0;
    }
    public static string Initial(string title) => string.IsNullOrEmpty(title) ? "?" : title[..1].ToUpperInvariant();
}
