namespace WinMac.Host.SettingsV2;

/// <summary>Modo de tema de Ajustes V2. «System» sigue a Windows; los tres comparten estructura y componentes, solo cambia la paleta.</summary>
public enum SettingsThemeMode { System, Light, Dark }

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Hex(int rgb, byte a = 255) => new((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), a);

    /// <summary>Luminancia relativa WCAG de este color ya compuesto sobre <paramref name="under"/> (opaco).</summary>
    public double Luminance(Rgba under)
    {
        double a = A / 255.0;
        static double Ch(double c) { c /= 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double r = Ch(R * a + under.R * (1 - a)), g = Ch(G * a + under.G * (1 - a)), b = Ch(B * a + under.B * (1 - a));
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    /// <summary>Contraste WCAG (1–21) de <paramref name="fg"/> sobre <paramref name="bg"/> (el fondo se toma opaco sobre <paramref name="under"/>).</summary>
    public static double Contrast(Rgba fg, Rgba bg, Rgba under)
    {
        var opaqueBg = new Rgba((byte)Math.Round(bg.R * (bg.A / 255.0) + under.R * (1 - bg.A / 255.0)), (byte)Math.Round(bg.G * (bg.A / 255.0) + under.G * (1 - bg.A / 255.0)), (byte)Math.Round(bg.B * (bg.A / 255.0) + under.B * (1 - bg.A / 255.0)));
        double l1 = fg.Luminance(opaqueBg), l2 = opaqueBg.Luminance(opaqueBg);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}

/// <summary>Tokens de color de Ajustes V2: violeta/lavanda con superficies diferenciadas (claro: lavanda translúcido; oscuro: grafito con acento lavanda, no un claro invertido).</summary>
public sealed record SettingsPalette(
    Rgba BackdropTop, Rgba BackdropBottom, Rgba Sidebar, Rgba Surface, Rgba SurfaceRaised, Rgba Stroke, Rgba Text, Rgba TextSecondary, Rgba TextFaint,
    Rgba Accent, Rgba AccentSoft, Rgba OnAccent, Rgba Success, Rgba Warning, Rgba Danger, Rgba SwitchOff, Rgba Field, Rgba Shadow, bool Dark)
{
    public static readonly SettingsPalette Light = new(
        BackdropTop: Rgba.Hex(0xF3EDFF), BackdropBottom: Rgba.Hex(0xE6DDFA), Sidebar: Rgba.Hex(0xFFFFFF, 150), Surface: Rgba.Hex(0xFFFFFF, 205), SurfaceRaised: Rgba.Hex(0xFFFFFF, 235),
        Stroke: Rgba.Hex(0xB9A8E8, 110), Text: Rgba.Hex(0x1B1730), TextSecondary: Rgba.Hex(0x58506E), TextFaint: Rgba.Hex(0x7A7192),
        Accent: Rgba.Hex(0x6B3FD1), AccentSoft: Rgba.Hex(0xE4D9FB), OnAccent: Rgba.Hex(0xFFFFFF), Success: Rgba.Hex(0x17763E), Warning: Rgba.Hex(0x9A5B00), Danger: Rgba.Hex(0xC0262D),
        SwitchOff: Rgba.Hex(0xC9C0DE), Field: Rgba.Hex(0xFFFFFF, 200), Shadow: Rgba.Hex(0x3B2A78, 40), Dark: false);

    public static readonly SettingsPalette DarkPalette = new(
        BackdropTop: Rgba.Hex(0x1A1626), BackdropBottom: Rgba.Hex(0x120F1C), Sidebar: Rgba.Hex(0x1F1B2C, 200), Surface: Rgba.Hex(0x26212F, 235), SurfaceRaised: Rgba.Hex(0x302A3D),
        Stroke: Rgba.Hex(0x4A4260, 150), Text: Rgba.Hex(0xF2EEFA), TextSecondary: Rgba.Hex(0xB8B0CB), TextFaint: Rgba.Hex(0x978EAE),
        Accent: Rgba.Hex(0xB79CFF), AccentSoft: Rgba.Hex(0x3C3160), OnAccent: Rgba.Hex(0x1B1236), Success: Rgba.Hex(0x5FD38D), Warning: Rgba.Hex(0xF0B24D), Danger: Rgba.Hex(0xFF7A80),
        SwitchOff: Rgba.Hex(0x4A4360), Field: Rgba.Hex(0x1B1726, 230), Shadow: Rgba.Hex(0x000000, 90), Dark: true);

    /// <summary>La paleta que corresponde al modo elegido; «System» usa el tema de Windows.</summary>
    public static SettingsPalette Resolve(SettingsThemeMode mode, bool systemDark) => mode switch { SettingsThemeMode.Light => Light, SettingsThemeMode.Dark => DarkPalette, _ => systemDark ? DarkPalette : Light };

    public static SettingsThemeMode ParseMode(string? s) => Enum.TryParse<SettingsThemeMode>(s, ignoreCase: true, out var m) ? m : SettingsThemeMode.System;
}

/// <summary>Escala de espaciado, radios y tipografía (DIP): una sola fuente para todas las páginas.</summary>
public static class SettingsTokens
{
    public const double S1 = 4, S2 = 8, S3 = 12, S4 = 16, S5 = 24, S6 = 32;
    public const double RadiusCard = 16, RadiusControl = 10, RadiusPill = 999;
    public const double ControlH = 34, SwitchW = 46, SwitchH = 26, MinTouch = 32;
    public static readonly TextStyle PageTitle = new("title", 28, TextWeight.Semibold), PageSubtitle = new("subtitle", 14), SectionTitle = new("section", 17, TextWeight.Semibold),
        CardTitle = new("cardtitle", 14.5, TextWeight.Semibold), Body = new("body", 13.5), Small = new("small", 12.5), Caption = new("caption", 11.5), Nav = new("nav", 14);
}
