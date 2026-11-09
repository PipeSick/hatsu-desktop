namespace WinMac.Dock;

/// <summary>Color con componentes 0–1 (sin depender de Direct2D, para probar los temas sin Windows).</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1f);

/// <summary>
/// Paleta común de las superficies PROPIAS de WinMac que se dibujan con colores fijos (diálogos, ventana principal, «Acerca de este PC», menús propios). Sigue el tema de
/// APLICACIONES de Windows (<c>AppsUseLightTheme</c>), nunca el fondo de pantalla ni las ventanas de otras apps. Pura: los pintores la convierten a su color nativo.
/// <para>Convención: el «tinte» es blanco en el tema oscuro y casi negro en el claro. <see cref="Ink"/> es para texto e iconos (el alfa se eleva en el tema claro para conservar
/// el contraste); <see cref="Fill"/> es para superficies translúcidas (botones, tarjetas, separadores).</para>
/// </summary>
public sealed class UiPalette
{
    public DockTheme Theme { get; }
    public bool IsDark => Theme == DockTheme.Dark;
    /// <summary>Fondo de la ventana o del diálogo.</summary>
    public Rgba Window { get; }
    /// <summary>Superficie elevada (tarjetas, botón neutro).</summary>
    public Rgba Surface { get; }
    public Rgba Accent { get; } = new(0.04f, 0.52f, 1f);
    /// <summary>Acción destructiva (botón relleno) y su variante con el puntero encima.</summary>
    public Rgba Danger { get; }
    public Rgba DangerHover { get; }
    public Rgba Success { get; } = new(0.20f, 0.78f, 0.35f);
    public Rgba Warning { get; } = new(1f, 0.70f, 0.10f);
    private readonly (float R, float G, float B) _tint;

    private UiPalette(DockTheme t)
    {
        Theme = t;
        if (t == DockTheme.Dark) { Window = new(0.125f, 0.130f, 0.150f); Surface = new(0.225f, 0.230f, 0.265f); _tint = (1f, 1f, 1f); Danger = new(0.745f, 0.250f, 0.270f); DangerHover = new(0.80f, 0.29f, 0.31f); }
        else { Window = new(0.975f, 0.975f, 0.985f); Surface = new(0.905f, 0.910f, 0.925f); _tint = (0.07f, 0.07f, 0.09f); Danger = new(0.780f, 0.180f, 0.200f); DangerHover = new(0.72f, 0.14f, 0.16f); }
    }

    public static UiPalette For(DockTheme t) => t == DockTheme.Dark ? Dark : Light;
    public static readonly UiPalette Dark = new(DockTheme.Dark), Light = new(DockTheme.Light);

    /// <summary>Texto e iconos con el «peso» <paramref name="a"/> (0–1) que tendrían en el tema oscuro. En el claro el alfa sube para que el texto secundario siga leyéndose.</summary>
    public Rgba Ink(float a) => new(_tint.R, _tint.G, _tint.B, IsDark ? a : a >= 0.4f ? 0.32f + 0.68f * a : a * 0.8f);
    /// <summary>Superficies translúcidas (botones, separadores, tarjetas sobre la ventana).</summary>
    public Rgba Fill(float a) => new(_tint.R, _tint.G, _tint.B, IsDark ? a : a * 0.55f);
    /// <summary>Texto sobre un fondo de color intenso (botón de acción, fila resaltada): siempre blanco.</summary>
    public Rgba OnColor(float a = 1f) => new(1f, 1f, 1f, a);

    // ── contraste (WCAG) para las pruebas
    public static double Luminance(Rgba c)
    {
        static double Lin(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    /// <summary>Compone <paramref name="fg"/> (con su alfa) sobre <paramref name="bg"/> opaco.</summary>
    public static Rgba Over(Rgba fg, Rgba bg) => new(fg.R * fg.A + bg.R * (1 - fg.A), fg.G * fg.A + bg.G * (1 - fg.A), fg.B * fg.A + bg.B * (1 - fg.A));

    public static double Contrast(Rgba fg, Rgba bg)
    {
        double la = Luminance(Over(fg, bg)), lb = Luminance(bg); if (la < lb) (la, lb) = (lb, la);
        return (la + 0.05) / (lb + 0.05);
    }
}
