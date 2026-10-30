namespace WinMac.Dock;

/// <summary>
/// Geometría del Dock en DIP. Los valores por defecto son una APROXIMACIÓN PROVISIONAL [H] de macOS Tahoe;
/// se calibrarán con capturas de referencia (docs/02-fidelidad.md). Todo parámetro de fidelidad vive aquí, no disperso en el render.
/// </summary>
public sealed record DockMetrics
{
    public double BaseSize { get; init; } = 60;
    public double MaxScale { get; init; } = 1.7;
    public double Radius { get; init; } = 150;      // alcance de la magnificación
    public double Gap { get; init; } = 6;
    public double PadX { get; init; } = 10;
    public double PadTop { get; init; } = 8;
    public double PadBottom { get; init; } = 14;    // incluye el espacio del indicador de app abierta
    public double BarRadius { get; init; } = 26;
    public double SeparatorGap { get; init; } = 10;
    public double DotSize { get; init; } = 4;
    public double ShadowMargin { get; init; } = 28;
    public double BounceHeight { get; init; } = 22; // salto máximo al lanzar una app
    public double LabelHeight { get; init; } = 28;  // etiqueta con el nombre sobre el ícono bajo el cursor
    public double LabelGap { get; init; } = 10;

    public double BarHeight => BaseSize + PadTop + PadBottom;
    public double SeparatorWidth => SeparatorGap * 2 + 1;
    /// <summary>Altura de ventana: barra + holgura de magnificación + salto + márgenes de sombra.</summary>
    public double WindowHeight => BarHeight + BaseSize * (MaxScale - 1) + BounceHeight + LabelHeight + LabelGap + ShadowMargin * 2;
    /// <summary>Distancia total que recorre el dock al ocultarse.</summary>
    public double HideDistance => BarHeight + ShadowMargin + 10;
}
