namespace WinMac.Dock;

/// <summary>
/// Geometría del Dock en DIP. Las PROPORCIONES (todas respecto al ancho del ícono, <see cref="BaseSize"/>) están CALIBRADAS con una captura real de macOS Tahoe
/// (docs/09-analisis-referencia-1.md): son medidas directas con ±1 px de incertidumbre; la captura estaba recortada y de escala desconocida, por eso se usan razones.
/// El tamaño absoluto del ícono es una preferencia del usuario (como el control «Tamaño» del Dock), no una proporción.
/// SIN CALIBRAR todavía (esperan las capturas 2 y 3): magnificación (<see cref="MaxScale"/>, <see cref="Radius"/>), etiqueta y salto de lanzamiento.
/// </summary>
public sealed record DockMetrics
{
    /// <summary>Lado visible del ícono en reposo (DIP). Preferencia del usuario.</summary>
    public double BaseSize { get; init; } = 60;

    // ── Proporciones medidas (macOS ÷ ancho del ícono = 87 px en la captura) ──
    public double GapRatio { get; init; } = 0.345;            // hueco entre íconos: 30 px
    public double PadXRatio { get; init; } = 0.339;           // margen lateral: 29,5 px
    public double PadTopRatio { get; init; } = 0.333;         // margen superior: 29 px
    public double PadBottomRatio { get; init; } = 0.322;      // margen inferior: 28 px (incluye el indicador)
    public double BarRadiusOfBarHeight { get; init; } = 0.298;// radio ÷ altura de la barra (ajuste de círculo: 43,2 px sobre 145)
    public double SeparatorLeftRatio { get; init; } = 0.494;  // último ícono → línea: 43 px
    public double SeparatorRightRatio { get; init; } = 0.460; // línea → siguiente ícono: 40 px
    public double SeparatorLineRatio { get; init; } = 0.023;  // grosor de la línea: 2 px
    public double SeparatorHeightOfBarHeight { get; init; } = 0.745; // 108 px sobre 145
    public double DotRatio { get; init; } = 0.080;            // diámetro del indicador: 7 px
    public double DotCenterBelowIconRatio { get; init; } = 0.217; // centro del indicador bajo el ícono: 18,9 px

    // ── Sin calibrar (conservan los valores provisionales anteriores; ver docs/09 §5) ──
    public double MaxScale { get; init; } = 1.7;
    public double Radius { get; init; } = 150;      // alcance de la magnificación (DIP absolutos)
    public double ShadowMargin { get; init; } = 28;
    public double BounceHeight { get; init; } = 22; // salto máximo al lanzar una app
    public double LabelHeight { get; init; } = 28;  // etiqueta con el nombre sobre el ícono bajo el cursor
    public double LabelGap { get; init; } = 10;

    // ── Derivados en DIP ──
    public double Gap => BaseSize * GapRatio;
    public double PadX => BaseSize * PadXRatio;
    public double PadTop => BaseSize * PadTopRatio;
    public double PadBottom => BaseSize * PadBottomRatio;
    public double BarHeight => BaseSize + PadTop + PadBottom;
    public double BarRadius => BarHeight * BarRadiusOfBarHeight;
    public double SeparatorLeft => BaseSize * SeparatorLeftRatio;
    public double SeparatorRight => BaseSize * SeparatorRightRatio;
    public double SeparatorLine => Math.Max(1, BaseSize * SeparatorLineRatio);
    public double SeparatorWidth => SeparatorLeft + SeparatorLine + SeparatorRight - Gap; // espacio extra que aporta respecto a un hueco normal
    public double SeparatorHeight => BarHeight * SeparatorHeightOfBarHeight;
    public double DotSize => BaseSize * DotRatio;
    public double DotCenterBelowIcon => BaseSize * DotCenterBelowIconRatio;

    /// <summary>Altura de ventana: barra + holgura de magnificación + salto + etiqueta + márgenes de sombra.</summary>
    public double WindowHeight => BarHeight + BaseSize * (MaxScale - 1) + BounceHeight + LabelHeight + LabelGap + ShadowMargin * 2;
    /// <summary>Distancia total que recorre el dock al ocultarse.</summary>
    public double HideDistance => BarHeight + ShadowMargin + 10;
}
