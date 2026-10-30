namespace WinMac.Dock;

/// <summary>
/// Niveles de «vidrio» con coste creciente. Todos usan solo APIs documentadas.
/// Tint: tinte plano + borde + sombra, sin fondo (coste mínimo, cualquier GPU).
/// WallpaperBlur: desenfoca y satura la imagen del fondo de pantalla recortada tras el dock (sin captura continua).
/// LiveCapture: captura en vivo con Windows.Graphics.Capture. NO implementado: requiere análisis de coste (docs/05-liquid-glass.md).
/// </summary>
public enum GlassTier { Tint, WallpaperBlur, LiveCapture }

/// <summary>Parámetros del material. Provisionales [H]; se calibran contra referencias.</summary>
public sealed record GlassStyle
{
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
