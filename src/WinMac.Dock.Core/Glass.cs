namespace WinMac.Dock;

/// <summary>
/// Niveles de «vidrio» con coste creciente. Todos usan solo APIs documentadas.
/// Tint: tinte plano + borde + sombra, sin fondo (coste mínimo, cualquier GPU).
/// WallpaperBlur: desenfoca y satura la imagen del fondo de pantalla recortada tras el dock (sin captura continua).
/// LiveCapture: captura en vivo con Windows.Graphics.Capture. NO implementado: requiere análisis de coste (docs/05-liquid-glass.md).
/// </summary>
public enum GlassTier { Tint, WallpaperBlur, LiveCapture }

public enum DockTheme { Light, Dark }
public enum ThemeMode { System, Light, Dark }

/// <summary>Parámetros del material. Provisionales [H]; se calibran contra referencias.</summary>
public sealed record GlassStyle
{
    public DockTheme Theme { get; init; } = DockTheme.Light;
    public double TintR { get; init; } = 1;   // color del tinte (blanco = vidrio claro)
    public double TintG { get; init; } = 1;
    public double TintB { get; init; } = 1;
    public double MarkR { get; init; } = 1;   // color de separadores e indicadores
    public double MarkG { get; init; } = 1;
    public double MarkB { get; init; } = 1;
    public double MarkAlpha { get; init; } = 0.85;
    public double SeparatorAlpha { get; init; } = 0.38;

    /// <summary>
    /// Presets. It8: cada tema tiene un MATERIAL propio, no solo otro tinte. Antes el tinte (α 0,20–0,40) era tan tenue que el fondo de pantalla desenfocado mandaba y el Dock
    /// se veía casi igual (p. ej. rosado/lila con un fondo rosa) en claro y en oscuro. Ahora el tinte tapa lo bastante el color del fondo y se atenúa su saturación:
    /// oscuro = cristal grafito con un tinte violeta muy sutil; claro = cristal blanco translúcido con marcas oscuras. El contraste de las marcas se comprueba en las pruebas.
    /// </summary>
    public static GlassStyle For(DockTheme t) => t == DockTheme.Light ? new GlassStyle
    {
        Theme = DockTheme.Light, TintR = 1, TintG = 1, TintB = 1, TintAlpha = 0.42, Brightness = 0.10, TintBottomFactor = 0.85, Saturation = 0.80,
        MarkR = 0.10, MarkG = 0.10, MarkB = 0.13, MarkAlpha = 0.78, SeparatorAlpha = 0.30,
        RimTopAlpha = 0.60, RimBottomAlpha = 0.40, SideContourAlpha = 0.14, ShadowAlpha = 0.10, TopSheenAlpha = 0.20,
    } : new GlassStyle
    {
        Theme = DockTheme.Dark, TintR = 0.20, TintG = 0.18, TintB = 0.26, TintAlpha = 0.62, Brightness = 0.0, TintBottomFactor = 0.92, Saturation = 0.75,
        MarkR = 1, MarkG = 1, MarkB = 1, MarkAlpha = 0.88, SeparatorAlpha = 0.40,
        ShadowAlpha = 0.16, RimTopAlpha = 0.26, RimBottomAlpha = 0.18, SideContourAlpha = 0.22,
    };

    /// <summary>Color efectivo (R,G,B de 0 a 1) del cristal arriba, compuesto sobre un fondo gris uniforme: lo que lee el usuario tras el desenfoque.</summary>
    public (double R, double G, double B) EffectiveOver(double backdropGray)
    {
        double a = Math.Clamp(TintAlpha + Brightness, 0, 1);
        return (a * TintR + (1 - a) * backdropGray, a * TintG + (1 - a) * backdropGray, a * TintB + (1 - a) * backdropGray);
    }

    public double TintBottomFactor { get; init; } = 0.8;   // opacidad del tinte abajo ÷ arriba
    public double SideContourAlpha { get; init; } = 0.20;  // contorno oscuro fino (macOS: ≈ −13…−25 de luminancia en los costados)
    public double RimWidth { get; init; } = 1.25;          // filete claro arriba/abajo (macOS: ≈ 2 px en la captura de escala desconocida)
    public double SheenFraction { get; init; } = 0.10;     // alto del resplandor interior superior ÷ barra (macOS: ≈ 6–8 px de 145)
    public double BlurSigma { get; init; } = 22;        // px de dispositivo a 96 dpi
    public double Saturation { get; init; } = 1.0;      // medido: sin realce apreciable (inconcluso, ver docs/09); antes 1,7
    public double Brightness { get; init; } = 0.10;     // aditivo sobre blanco (0..1)
    public double TintAlpha { get; init; } = 0.20;
    public double RimTopAlpha { get; init; } = 0.32;    // filete superior: α blanco ≈ 0,26–0,36 medido (antes 0,80)
    public double RimBottomAlpha { get; init; } = 0.26; // filete inferior: ≈ 0,26 medido
    public double InnerRimAlpha { get; init; } = 0.0;   // eliminado (no aparece en la referencia)
    public double ShadowBlur { get; init; } = 12;
    public double ShadowAlpha { get; init; } = 0.06;    // opacidad ACUMULADA junto al borde (medido lateral ≤ 2–6 %; antes ≈ 51 % por apilado)
    public double ShadowOffsetY { get; init; } = 4;     // sin medir abajo (recorte de 9 px); arriba no hay sombra
    public double TopSheenAlpha { get; init; } = 0.12;  // resplandor suave en el borde superior
}

/// <summary>It8: cuándo hay que cambiar el material del Dock. Puro: el anfitrión lo consulta en cada aviso de Windows y en su repaso periódico.</summary>
public static class DockThemeTracker
{
    /// <returns>true si el material actual no corresponde al tema que manda (el de Windows con <see cref="ThemeMode.System"/>, o el elegido a mano).</returns>
    public static bool NeedsRestyle(ThemeMode mode, DockTheme system, DockTheme current, out DockTheme target)
    {
        target = mode switch { ThemeMode.Light => DockTheme.Light, ThemeMode.Dark => DockTheme.Dark, _ => system };
        return target != current;
    }
}
