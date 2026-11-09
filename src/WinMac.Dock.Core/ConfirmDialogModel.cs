namespace WinMac.Dock;

/// <summary>Icono del diálogo de confirmación (cada acción tiene el suyo; ningún diálogo cambia la lógica de su acción).</summary>
public enum ConfirmKind { Warning, Power, Restart, SignOut, Trash, ForceQuit, Info }

/// <summary>Un rectángulo en DIP.</summary>
public readonly record struct DipRect(float X, float Y, float W, float H) { public float Right => X + W; public float Bottom => Y + H; public bool Contains(float x, float y) => x >= X && x < Right && y >= Y && y < Bottom; }

/// <summary>Reparto del diálogo (DIP): puro, para probarlo sin Windows. Basado en la referencia aprobada (09_referencia_diseno_confirmaciones.png).</summary>
public sealed record ConfirmFrame(float Width, float Height, DipRect Tile, DipRect Title, DipRect Body, float SeparatorY, DipRect Cancel, DipRect Action, float Radius, float ButtonRadius);

public static class ConfirmLayout
{
    public const float Width = 440, Pad = 24, Tile = 56, TileGap = 16, Radius = 22, ButtonRadius = 12, ButtonH = 44, ButtonGap = 12, SeparatorGap = 18, MinTitleH = 28;

    /// <param name="titleH">Alto medido del título (envuelto al ancho disponible).</param>
    /// <param name="bodyH">Alto medido de la descripción.</param>
    /// <param name="onlyOk">Un solo botón (avisos): ocupa todo el ancho.</param>
    public static ConfirmFrame Compute(float titleH, float bodyH, bool onlyOk, float width = Width)
    {
        float textX = Pad + Tile + TileGap, textW = width - textX - Pad;
        titleH = Math.Max(MinTitleH, titleH);
        float header = Math.Max(Tile, titleH + 6 + bodyH);                        // la altura del encabezado: la del icono o la del texto, la que sea mayor
        float top = Pad; float tileY = top + (header - Tile) / 2;
        float sepY = top + header + SeparatorGap, by = sepY + SeparatorGap + 1;
        float inner = width - 2 * Pad, bw = onlyOk ? inner : (inner - ButtonGap) / 2;
        var cancel = onlyOk ? new DipRect(0, 0, 0, 0) : new DipRect(Pad, by, bw, ButtonH);
        var action = new DipRect(onlyOk ? Pad : Pad + bw + ButtonGap, by, bw, ButtonH);
        return new ConfirmFrame(width, by + ButtonH + Pad, new DipRect(Pad, tileY, Tile, Tile), new DipRect(textX, top + (header - (titleH + 6 + bodyH)) / 2, textW, titleH),
            new DipRect(textX, top + (header - (titleH + 6 + bodyH)) / 2 + titleH + 6, textW, bodyH), sepY, cancel, action, Radius, ButtonRadius);
    }
}

/// <summary>Colores del diálogo según el tema y si la acción destruye algo.</summary>
public static class ConfirmStyle
{
    /// <summary>(fondo del icono, color del icono, botón de acción, botón de acción con el puntero).</summary>
    public static (Rgba Tile, Rgba Glyph, Rgba Action, Rgba ActionHover) Colors(ConfirmKind kind, bool danger, UiPalette p)
    {
        if (danger || kind is ConfirmKind.Power or ConfirmKind.Restart or ConfirmKind.SignOut or ConfirmKind.Trash or ConfirmKind.ForceQuit)
            return (new Rgba(p.Danger.R, p.Danger.G, p.Danger.B, p.IsDark ? 0.24f : 0.14f), p.IsDark ? new Rgba(1f, 0.45f, 0.47f) : p.Danger, p.Danger, p.DangerHover);
        if (kind == ConfirmKind.Warning) return (new Rgba(p.Warning.R, p.Warning.G, p.Warning.B, 0.22f), p.IsDark ? p.Warning : new Rgba(0.75f, 0.45f, 0f), p.Accent, new Rgba(0.02f, 0.45f, 0.93f));
        return (new Rgba(p.Accent.R, p.Accent.G, p.Accent.B, 0.20f), p.IsDark ? new Rgba(0.45f, 0.72f, 1f) : p.Accent, p.Accent, new Rgba(0.02f, 0.45f, 0.93f));
    }
}
