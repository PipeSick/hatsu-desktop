using WinMac.Dock;

namespace WinMac.Host;

public enum BoxKind
{
    /// <summary>Superficie que contiene otras cosas (tarjeta, marco): se puede solapar con lo que lleva dentro, no con otras superficies.</summary>
    Card,
    /// <summary>Texto: ocupa de verdad su caja (alto estimado por <see cref="TextFit"/>); no puede pisar a otro texto ni a un control.</summary>
    Text,
    /// <summary>Botón, interruptor, segmento: zona clicable.</summary>
    Control,
    /// <summary>Adorno (puntos, logotipo): no cuenta para las superposiciones de texto.</summary>
    Decor,
}

public sealed record LayoutBox(string Id, double X, double Y, double W, double H, BoxKind Kind, bool Enabled = true)
{
    public double Right => X + W; public double Bottom => Y + H;
    public bool Overlaps(LayoutBox o) => X < o.Right - 0.01 && o.X < Right - 0.01 && Y < o.Bottom - 0.01 && o.Y < Bottom - 0.01;
    public bool Contains(LayoutBox o) => o.X >= X - 0.01 && o.Y >= Y - 0.01 && o.Right <= Right + 0.01 && o.Bottom <= Bottom + 0.01;
}

/// <summary>Estimación (conservadora) del alto de un texto con ajuste de línea, para reservar espacio real en lugar de posiciones absolutas. Segoe UI: ≈0,56 em por carácter.</summary>
public static class TextFit
{
    public const double Em = 0.56;
    public static int Lines(string? text, double widthDip, double fontPx)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int perLine = Math.Max(1, (int)Math.Floor(widthDip / (fontPx * Em))); int lines = 0;
        foreach (var para in text.Split('\n'))
        {
            int cur = 0; lines++;
            foreach (var w in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int need = cur == 0 ? w.Length : cur + 1 + w.Length;
                if (need <= perLine) cur = need; else { if (cur > 0) lines++; cur = Math.Min(w.Length, perLine); }
            }
        }
        return lines;
    }
    public static double Height(string? text, double widthDip, double fontPx, double lineH) => Lines(text, widthDip, fontPx) * lineH;
}

/// <summary>
/// ÚNICA fuente de las medidas del asistente (DIP, ventana de 800×560): la usan el controlador (zonas clicables), el pintor (dibujo) y las pruebas de geometría. Nada se coloca con posiciones
/// sueltas: cada bloque de texto reserva el alto que necesita y lo que sigue empieza debajo. Márgenes: 40 a cada lado; contenido entre y = 92 y 484; pie con botones en y = 492.
/// </summary>
public static class OnboardingLayout
{
    public const double W = OnboardingController.WinW, H = OnboardingController.WinH, MarginX = 40, ContentTop = 92, ContentBottom = 484, FooterY = 492, ButtonH = 44;
    public const double FontBody = 14.5, FontSmall = 12.5, LineBody = 20, LineSmall = 17;

    public static IReadOnlyList<LayoutBox> Compute(OnboardingController c)
    {
        var b = new List<LayoutBox>();
        void Add(string id, double x, double y, double w, double h, BoxKind k, bool en = true) => b.Add(new(id, x, y, w, h, k, en));
        double SmallH(string? t, double w) => TextFit.Height(t, w, FontSmall, LineSmall);

        // cabecera (todas las pantallas menos la bienvenida)
        if (c.Step != OnboardingStep.Welcome)
        {
            Add("h.logo", 40, 26, 40, 40, BoxKind.Decor); Add("h.title", 92, 24, 500, 30, BoxKind.Text); Add("h.sub", 92, 54, 500, 20, BoxKind.Text); Add("h.brand", 600, 28, 160, 20, BoxKind.Text);
        }
        var ch = c.Choices;
        switch (c.Step)
        {
            case OnboardingStep.Welcome:
            {
                bool mig = c.Mode == OnboardingMode.Migration;
                Add("w.logo", 344, 84, 112, 112, BoxKind.Decor); Add("w.title", 100, 206, 600, 44, BoxKind.Text); Add("w.tag", 100, 252, 600, 30, BoxKind.Text);
                string body = mig ? MigrationBody : WelcomeBody;
                Add("w.body", 140, 296, 520, TextFit.Height(body, 520, FontBody, LineBody), BoxKind.Text);
                if (mig) { Add("keep", 270, 392, 260, 46, BoxKind.Control); Add("next", 270, 446, 260, 32, BoxKind.Control); Add("w.note", 120, 484, 560, TextFit.Height(MigrationNote, 560, FontSmall, LineSmall), BoxKind.Text); }
                else
                {
                    Add("next", 290, 392, 220, 46, BoxKind.Control); Add(c.Mode == OnboardingMode.Rerun ? "cancel" : "skip", 270, 446, 260, 32, BoxKind.Control);
                    if (c.Mode != OnboardingMode.Rerun) Add("w.note", 120, 484, 560, TextFit.Height(WelcomeSkipNote, 560, FontSmall, LineSmall), BoxKind.Text);
                }
                break;
            }

            case OnboardingStep.Components:
                for (int i = 0; i < OnboardingRules.Components.Length; i++)
                {
                    string id = OnboardingRules.Components[i]; double x = 40 + (i % 2) * 370, y = ContentTop + (i / 2) * 120, w = 350, h = 112;
                    Add("c:" + id, x, y, w, h, BoxKind.Control);   // toda la tarjeta es clicable
                    Add("cn:" + id, x + 18, y + 10, w - 90, 24, BoxKind.Text); Add("cs:" + id, x + w - 64, y + 12, 46, 26, BoxKind.Decor);
                    double dh = TextFit.Height(OnboardingRules.Description(id), w - 36, FontSmall, LineSmall); Add("cd:" + id, x + 18, y + 42, w - 36, dh, BoxKind.Text);
                    if (OnboardingRules.RequiresBar(id)) Add("cr:" + id, x + 18, y + 42 + dh + 4, 168, 20, BoxKind.Text);
                }
                if (c.Note is { } cn) Add("note", 40, 452, 720, SmallH(cn, 720), BoxKind.Text);
                Footer(b, "Siguiente");
                break;

            case OnboardingStep.Appearance:
            {
                double y = 92; const double colW = 420;
                Add("a.l1", 40, y, colW, 22, BoxKind.Text); Segs(b, "theme", 3, 40, y + 28, colW, true); y += 28 + 38 + 4;
                Add("a.n1", 40, y, colW, SmallH(ThemeNote, colW), BoxKind.Text); y += SmallH(ThemeNote, colW) + 14;
                Add("a.l2", 40, y, colW, 22, BoxKind.Text); Segs(b, "size", DockSettings.Presets.Length, 40, y + 28, colW, ch.Dock); y += 28 + 38 + 14;
                Add("a.l3", 40, y, colW, 22, BoxKind.Text); Segs(b, "align", 3, 40, y + 28, colW, ch.Dock); y += 28 + 38 + 14;
                Add("a.l4", 40, y, colW, 22, BoxKind.Text); Segs(b, "bg", 2, 40, y + 28, colW, ch.Bar); y += 28 + 38 + 8;
                string? off = !ch.Dock ? AppearanceDockOff : !ch.Bar ? AppearanceBarOff : null;
                if (off is not null) Add("a.off", 40, y, colW, SmallH(off, colW), BoxKind.Text);
                // vista previa: escritorio horizontal (16:10)
                Add("p.card", 480, 96, 280, 196, BoxKind.Card); Add("p.desk", 492, 108, 256, 160, BoxKind.Decor); Add("p.cap", 492, 270, 256, 18, BoxKind.Text);
                Footer(b, "Siguiente", dots: true);
                break;
            }

            case OnboardingStep.Windows:
            {
                double y = 92;
                foreach (var id in new[] { "autostart", "hidetask" })
                {
                    bool auto = id == "autostart", en = auto || ch.Dock; string title = auto ? AutostartTitle : HideTitle, desc = auto ? AutostartDesc : en ? HideDesc : HideNeedsDock;
                    double dh = TextFit.Height(desc, 600, FontSmall, LineSmall), h = Math.Max(96, 20 + 28 + dh + 14);
                    Add("t:" + id, 40, y, 720, h, BoxKind.Control, en);
                    Add("tt:" + id, 60, y + 12, 600, 28, BoxKind.Text); Add("td:" + id, 60, y + 44, 600, dh, BoxKind.Text); Add("ts:" + id, 690, y + 14, 46, 26, BoxKind.Decor);
                    y += h + 12;
                }
                double ih = TextFit.Height(CloseInfo, 680, FontSmall, LineSmall) + 28; Add("i.card", 40, y, 720, ih, BoxKind.Card); Add("i.text", 60, y + 14, 680, ih - 28, BoxKind.Text); y += ih + 8;
                if (c.Note is { } wn) Add("note", 40, y, 720, SmallH(wn, 720), BoxKind.Text);
                Footer(b, "Siguiente", dots: true);
                break;
            }

            case OnboardingStep.Review:
            {
                var rows = OnboardingRules.Review(ch); double lh = 28, ch2 = 24 + rows.Count * lh;
                Add("r.card", 40, 92, 720, ch2, BoxKind.Card);
                for (int i = 0; i < rows.Count; i++) { Add("rl:" + i, 64, 104 + i * lh, 250, lh, BoxKind.Text); Add("rv:" + i, 320, 104 + i * lh, 420, lh, BoxKind.Text); }
                Add("back", 40, FooterY, 120, ButtonH, BoxKind.Control); Add("apply", 540, FooterY, 220, ButtonH, BoxKind.Control); Add("dots", 350, 508, 100, 10, BoxKind.Decor);
                break;
            }

            case OnboardingStep.Result:
            {
                var r = c.Result;
                if (c.Busy || r is null) { Add("x.card", 40, 92, 720, 80, BoxKind.Card); Add("x.busy", 64, 108, 672, 48, BoxKind.Text); break; }
                int n = Math.Min(r.Items.Count, MaxResultRows); const double rowH = 30;
                string msg = c.ResultMessage; double mh = TextFit.Height(msg, 672, FontSmall, LineSmall);
                double y = 92 + 14;
                for (int i = 0; i < n; i++) { Add("xd:" + i, 62, y + 9, 12, 12, BoxKind.Decor); Add("xl:" + i, 84, y, 232, rowH, BoxKind.Text); Add("xt:" + i, 324, y, 412, rowH, BoxKind.Text); y += rowH; }
                if (r.Items.Count > n) { Add("x.more", 84, y, 650, 22, BoxKind.Text); y += 24; }
                y += 6; Add("x.line", 64, y, 672, 1, BoxKind.Decor); y += 10;
                Add("x.msg", 64, y, 672, mh, BoxKind.Text); y += mh;
                bool hasTech = r.Items.Any(i => i.Technical is not null);
                if (hasTech) { y += 6; Add("details", 64, y, 220, 22, BoxKind.Control); y += 22; if (c.ShowDetails) { double th = r.Items.Where(i => i.Technical is not null).Sum(i => TextFit.Height($"{i.Label}: {i.Technical}", 672, 11.5, 16)); Add("x.tech", 64, y + 4, 672, th, BoxKind.Text); y += th + 4; } }
                y += 14; Add("x.card", 40, 92, 720, y - 92, BoxKind.Card);
                if (r.EssentialFailed) { Add("back", 40, FooterY, 170, ButtonH, BoxKind.Control); Add("cancel", 380, FooterY, 190, ButtonH, BoxKind.Control); Add("retry", 600, FooterY, 160, ButtonH, BoxKind.Control); }
                else if (r.HasWarnings) { Add("retry", 370, FooterY, 150, ButtonH, BoxKind.Control); Add("accept", 540, FooterY, 220, ButtonH, BoxKind.Control); }
                else Add("finish", 570, FooterY, 190, ButtonH, BoxKind.Control);
                break;
            }
        }
        return b;
    }

    public const int MaxResultRows = 8;

    private static void Footer(List<LayoutBox> b, string next, bool dots = true)
    {
        b.Add(new("back", 40, FooterY, 120, ButtonH, BoxKind.Control)); b.Add(new("next", 640, FooterY, 120, ButtonH, BoxKind.Control));
        if (dots) b.Add(new("dots", 350, 508, 100, 10, BoxKind.Decor));
    }

    /// <summary>Segmentos de un control de opciones: un contenedor (Decor) y un segmento clicable por opción.</summary>
    private static void Segs(List<LayoutBox> b, string id, int n, double x, double y, double w, bool enabled)
    {
        b.Add(new(id + ".seg", x, y, w, 38, BoxKind.Decor));
        for (int i = 0; i < n; i++) b.Add(new($"{id}:{i}", x + i * w / n, y, w / n, 38, BoxKind.Control, enabled));
    }

    // textos que ocupan espacio (los usan el pintor y las pruebas: una sola copia)
    public const string WelcomeBody = "Hatsu Desktop personaliza Windows con herramientas opcionales. Tú eliges cuáles usar y puedes cambiarlo después en Ajustes. Hasta que confirmes, no se modifica nada en tu equipo.";
    public const string MigrationBody = "Encontramos tu configuración anterior y la conservamos tal cual: tus módulos, tu inicio automático y tus ajustes siguen como estaban. Puedes seguir así o revisar qué componentes quieres usar. No se cambia nada hasta que lo confirmes.";
    public const string MigrationNote = "Puedes abrir el asistente cuando quieras en Ajustes › General › «Abrir el asistente de configuración».";
    public const string WelcomeSkipNote = "Omitir activa los componentes predeterminados (Dock, Barra superior y Dynamic Island). No cambia el inicio automático ni oculta la barra de tareas.";
    public const string ThemeNote = "Se aplica al Dock. La Barra superior y la Isla siguen siempre el tema de Windows.";
    public const string AppearanceDockOff = "El Dock está desactivado: sus opciones no se aplican.";
    public const string AppearanceBarOff = "La Barra superior está desactivada: su fondo no se aplica.";
    public const string AutostartTitle = "Iniciar Hatsu Desktop al iniciar sesión en Windows";
    public const string AutostartDesc = "Añade una entrada de inicio solo para tu usuario. Puedes quitarla cuando quieras en Ajustes. Desactivado por defecto.";
    public const string HideTitle = "Ocultar la barra de tareas original mientras Hatsu Desktop funcione";
    public const string HideDesc = "Hatsu Desktop la restaura al salir y, si se cierra de golpe, un guardián la devuelve. No se modifica Explorer de forma permanente. Desactivado por defecto.";
    public const string HideNeedsDock = "Necesita el Dock: sin él no quedaría ninguna forma de cambiar de ventana.";
    public const string CloseInfo = "Cerrar ventanas no cierra Hatsu Desktop: sigue en segundo plano, con su icono en la bandeja. Para salir del todo, usa «Salir» en el menú de la bandeja.";
}

/// <summary>Paleta del asistente. Marca Flux: #7C3AED, #A78BFA, #C4B5FD, #EDE9FE, #0B0618. Sobre superficies claras el texto de acento usa variantes más oscuras para cumplir el contraste (WCAG AA, 4,5:1).</summary>
public sealed record OnboardingPalette(bool Dark, int BgTop, int BgBottom, int Ink, int Muted, int AccentText, int Warn, int Bad, int Good, int Neutral, double GlassAlpha, int GlassRgb)
{
    public static readonly OnboardingPalette DarkTheme = new(true, 0x0B0618, 0x1E1042, 0xF5F2FF, 0xB9AEDC, 0xC4B5FD, 0xFBBF24, 0xF87171, 0x4ADE80, 0x9A93B8, 0.075, 0xFFFFFF);
    public static readonly OnboardingPalette LightTheme = new(false, 0xEDE9FE, 0xFAF8FF, 0x1E1236, 0x54457F, 0x6D28D9, 0x9A4406, 0xB91C1C, 0x166534, 0x7A7396, 0.66, 0xFFFFFF);
    public static OnboardingPalette For(bool dark) => dark ? DarkTheme : LightTheme;

    /// <summary>Superficies sobre las que se escribe: fondo (arriba y abajo) y tarjeta de vidrio sobre cada uno.</summary>
    public IEnumerable<int> Surfaces() { yield return BgTop; yield return BgBottom; yield return Over(BgTop); yield return Over(BgBottom); }
    private int Over(int bg) { static int Mix(int a, int b, double t) => (int)Math.Round(a + (b - a) * t); int r = Mix(bg >> 16 & 255, GlassRgb >> 16 & 255, GlassAlpha), g = Mix(bg >> 8 & 255, GlassRgb >> 8 & 255, GlassAlpha), bl = Mix(bg & 255, GlassRgb & 255, GlassAlpha); return r << 16 | g << 8 | bl; }

    public static double Luminance(int rgb)
    {
        static double C(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(rgb >> 16 & 255) + 0.7152 * C(rgb >> 8 & 255) + 0.0722 * C(rgb & 255);
    }
    public static double Contrast(int a, int b) { double la = Luminance(a), lb = Luminance(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05); }
}
