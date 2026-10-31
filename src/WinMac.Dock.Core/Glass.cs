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
    /// Presets. CALIBRADO con la referencia 1 (docs/09), con estas salvedades: la captura NO indicaba si el Dock estaba en apariencia clara u oscura
    /// (la ganancia de luminancia y el tinte gris medido se aplican al preset OSCURO, que es el que encaja con la captura y con el tema de Windows del usuario; PENDIENTE de confirmar);
    /// el material del preset CLARO conserva sus valores anteriores salvo la saturación, el borde y la sombra.
    /// </summary>
    public static GlassStyle For(DockTheme t) => t == DockTheme.Light ? new GlassStyle() : new GlassStyle
    {
        Theme = DockTheme.Dark, Brightness = 0.0, TintAlpha = 0.40, TintBottomFactor = 0.95, TintR = 0.31, TintG = 0.31, TintB = 0.31,
        ShadowAlpha = 0.08, SeparatorAlpha = 0.40,
    };

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
