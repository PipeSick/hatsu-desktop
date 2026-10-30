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

    public static GlassStyle For(DockTheme t) => t == DockTheme.Light ? new GlassStyle() : new GlassStyle
    {
        Theme = DockTheme.Dark, Saturation = 1.4, Brightness = 0.0, TintAlpha = 0.34, TintR = 0.07, TintG = 0.08, TintB = 0.11,
        RimTopAlpha = 0.55, RimBottomAlpha = 0.08, InnerRimAlpha = 0.16, ShadowAlpha = 0.45, TopSheenAlpha = 0.10, SeparatorAlpha = 0.30,
    };

    public double BlurSigma { get; init; } = 22;        // px de dispositivo a 96 dpi
    public double Saturation { get; init; } = 1.7;
    public double Brightness { get; init; } = 0.10;     // aditivo sobre blanco (0..1)
    public double TintAlpha { get; init; } = 0.20;
    public double RimTopAlpha { get; init; } = 0.80;    // borde especular superior
    public double RimBottomAlpha { get; init; } = 0.12;
    public double InnerRimAlpha { get; init; } = 0.30;  // segundo filo interior
    public double ShadowBlur { get; init; } = 16;
    public double ShadowAlpha { get; init; } = 0.30;
    public double ShadowOffsetY { get; init; } = 10;
    public double TopSheenAlpha { get; init; } = 0.22;  // brillo suave en la mitad superior (reflejo de «cristal»)
}
