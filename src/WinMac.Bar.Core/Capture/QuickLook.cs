namespace WinMac.Bar.Capture;

/// <summary>
/// Quick Look de las capturas (sección 11): tras guardar, una miniatura flotante abajo a la derecha (por encima del Dock) unos segundos y luego se desvanece;
/// nunca abre sola la vista grande. Un clic abre la vista previa centrada con acciones. Geometría, tiempos y teclas puros: se prueban sin Windows.
/// </summary>
public static class QuickLook
{
    public const double VisibleSeconds = 5, FadeSeconds = 0.45;

    public static readonly (string Id, string Label)[] Actions =
        [("copy", "Copiar"), ("edit", "Editar"), ("annotate", "Anotar"), ("crop", "Recortar"), ("share", "Compartir"), ("saveas", "Guardar como…"), ("open", "Abrir"), ("delete", "Eliminar")];

    /// <summary>
    /// Thumbnail rectangle (screen px): bottom-right of the monitor's work area, above the Dock when its window is there (with auto-hide it reserves nothing),
    /// 16 DIP from the edges, at most 220×150 DIP keeping the image's proportions.
    /// </summary>
    public static CaptureRect ThumbRect(CaptureMonitor mon, CaptureRect? dock, int imgW, int imgH)
    {
        double s = mon.Scale <= 0 ? 1 : mon.Scale; var work = mon.WorkArea; int margin = (int)Math.Round(16 * s);
        double maxW = 220 * s, maxH = 150 * s, k = Math.Min(maxW / Math.Max(1, imgW), maxH / Math.Max(1, imgH));
        int w = Math.Max((int)(60 * s), (int)Math.Round(imgW * k)), h = Math.Max((int)(40 * s), (int)Math.Round(imgH * k));
        int right = work.Right - margin, bottom = work.Bottom - margin;
        // Never under the Dock. Its WINDOW is taller than the visible Dock (room for the magnified icons), so its top is not used: when the Dock reserves its
        // band the work area already ends above it; otherwise (auto-hide) the visible Dock is about 110 DIP above the bottom of its window.
        if (dock is { } d && d.W > 0 && d.X < right && d.Right > right - w && d.Bottom > bottom) bottom = Math.Min(bottom, d.Bottom - (int)Math.Round(110 * s) - margin);
        return new CaptureRect(right - w, bottom - h, w, h);
    }

    /// <summary>Opacity at <paramref name="t"/> seconds: full while visible (and always while the pointer is on it), then a short fade.</summary>
    public static double Alpha(double t, bool hovering) => hovering || t <= VisibleSeconds ? 1 : Math.Max(0, 1 - (t - VisibleSeconds) / FadeSeconds);
    public static bool Expired(double t, bool hovering) => !hovering && t >= VisibleSeconds + FadeSeconds;

    /// <summary>Preview window (screen px): centred on the monitor's work area, the image at most 78 % × 70 % of it, plus title and toolbar.</summary>
    public static (CaptureRect Window, CaptureRect Image) PreviewRect(CaptureMonitor mon, int imgW, int imgH)
    {
        double s = mon.Scale <= 0 ? 1 : mon.Scale; var work = mon.WorkArea;
        int pad = (int)(18 * s), title = (int)(40 * s), bar = (int)(58 * s), minW = (int)(720 * s);
        double maxW = work.W * 0.78, maxH = work.H * 0.70 - title - bar;
        double k = Math.Min(1.0, Math.Min(maxW / Math.Max(1, imgW), Math.Max(80, maxH) / Math.Max(1, imgH)));
        int iw = Math.Max(1, (int)(imgW * k)), ih = Math.Max(1, (int)(imgH * k));
        int ww = Math.Min(work.W, Math.Max(minW, iw + 2 * pad)), wh = Math.Min(work.H, ih + title + bar + pad);
        int wx = work.X + (work.W - ww) / 2, wy = work.Y + (work.H - wh) / 2;
        return (new CaptureRect(wx, wy, ww, wh), new CaptureRect((ww - iw) / 2, title, iw, ih));
    }

    /// <summary>Keys in the preview: Esc and Space close (Space as in macOS), Ctrl+C copies, Enter opens the editor.</summary>
    public static string? KeyAction(int vk, bool ctrl) => vk switch
    {
        0x1B or 0x20 => "close",
        0x0D => "edit",
        0x43 when ctrl => "copy",
        _ => null,
    };

    /// <summary>Area-averaged downscale of top-down BGRA pixels (the thumbnail and the preview never show aliasing).</summary>
    public static byte[] Scale(byte[] px, int w, int h, int tw, int th)
    {
        var o = new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
        {
            int y0 = y * h / th, y1 = Math.Max(y0 + 1, (y + 1) * h / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = x * w / tw, x1 = Math.Max(x0 + 1, (x + 1) * w / tw); long b = 0, g = 0, r = 0, a = 0; int n = 0;
                for (int yy = y0; yy < y1; yy += Math.Max(1, (y1 - y0) / 4))
                    for (int xx = x0; xx < x1; xx += Math.Max(1, (x1 - x0) / 4)) { int i = (yy * w + xx) * 4; b += px[i]; g += px[i + 1]; r += px[i + 2]; a += px[i + 3]; n++; }
                int j = (y * tw + x) * 4; o[j] = (byte)(b / n); o[j + 1] = (byte)(g / n); o[j + 2] = (byte)(r / n); o[j + 3] = (byte)(a / n);
            }
        }

        return o;
    }
}
