using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WinMac.Bar;
using WinMac.Dock;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Host.App;

/// <summary>
/// Dibuja el asistente de primera ejecución con Direct2D en la identidad Flux (violeta y lavanda, superficies translúcidas, bordes redondeados, claro/oscuro, DPI por el dispositivo).
/// Las medidas salen de <see cref="OnboardingController.Hits"/>: lo que se dibuja es exactamente lo que se pulsa. Unidades en DIP.
/// </summary>
internal sealed class OnboardingPainter : IDisposable
{
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx; private readonly IDWriteFactory _dw; private readonly bool _dark;
    private readonly IDWriteTextFormat _h1, _h2, _body, _bodyB, _small, _tag;
    private readonly ID2D1SolidColorBrush _b; private ID2D1Bitmap? _logo;

    // paleta Flux de referencia: #7C3AED, #A78BFA, #C4B5FD, #EDE9FE, #0B0618 (no sustituye los temas de la interfaz; es el fondo del asistente)
    private static Color4 C(int rgb, float a = 1f) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    private static readonly Color4 Violet = C(0x7C3AED), Lilac = C(0xA78BFA), Lavender = C(0xC4B5FD), Mist = C(0xEDE9FE), Night = C(0x0B0618);
    private Color4 BgTop => _dark ? Night : Mist;
    private Color4 BgBottom => _dark ? C(0x1E1042) : C(0xFAF8FF);
    private Color4 Ink => _dark ? C(0xF5F2FF) : C(0x1E1236);
    private Color4 Muted => _dark ? C(0xB9AEDC) : C(0x5B4B86);
    private Color4 Glass => _dark ? new Color4(1, 1, 1, .075f) : new Color4(1, 1, 1, .66f);
    private Color4 GlassStrong => _dark ? new Color4(1, 1, 1, .14f) : new Color4(1, 1, 1, .92f);
    private Color4 Edge => _dark ? new Color4(1, 1, 1, .16f) : new Color4(.486f, .227f, .929f, .20f);
    private Color4 TrackOff => _dark ? new Color4(1, 1, 1, .20f) : new Color4(.35f, .29f, .53f, .28f);
    private Color4 Warn => C(0xF59E0B); private Color4 Bad => C(0xEF4444); private Color4 Good => C(0x22C55E);
    private static RawRectF R(double l, double t, double r, double b) => new((float)l, (float)t, (float)r, (float)b);

    public OnboardingPainter(D2DDevice dev, bool dark)
    {
        _dev = dev; _ctx = dev.Context; _dark = dark; _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _h1 = _dw.CreateTextFormat("Segoe UI Semibold", 30); _h2 = _dw.CreateTextFormat("Segoe UI Semibold", 22); _body = _dw.CreateTextFormat("Segoe UI", 14.5f);
        _bodyB = _dw.CreateTextFormat("Segoe UI Semibold", 14.5f); _small = _dw.CreateTextFormat("Segoe UI", 12.5f); _tag = _dw.CreateTextFormat("Segoe UI", 18);
    }

    private void Brush(Color4 c) => _b.Color = c;
    private void RR(double x, double y, double w, double h, double r, Color4 c) { Brush(c); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(x, y, x + w, y + h), RadiusX = (float)r, RadiusY = (float)r }, _b); }
    private void RRStroke(double x, double y, double w, double h, double r, Color4 c, float t = 1f) { Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(x + t / 2, y + t / 2, x + w - t / 2, y + h - t / 2), RadiusX = (float)r, RadiusY = (float)r }, _b, t); }
    private void Dot(double cx, double cy, double r, Color4 c) { Brush(c); _ctx.FillEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)r, (float)r), _b); }

    private void Text(string? s, IDWriteTextFormat f, double x, double y, double w, double h, Color4 c, bool center = false, bool right = false, bool wrap = false, bool top = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        using var l = _dw.CreateTextLayout(s, f, (float)Math.Max(1, w), (float)Math.Max(1, h)); l.WordWrapping = wrap ? WordWrapping.Wrap : WordWrapping.NoWrap; l.ParagraphAlignment = top ? ParagraphAlignment.Near : ParagraphAlignment.Center;
        l.TextAlignment = right ? TextAlignment.Trailing : center ? TextAlignment.Center : TextAlignment.Leading;
        if (!wrap) { using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell); }
        Brush(c); _ctx.DrawTextLayout(new Vector2((float)x, (float)y), l, _b);
    }

    private void Gradient(double x, double y, double w, double h, double r, Color4 a, Color4 b2, bool vertical = false)
    {
        using var stops = _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = a }, new GradientStop { Position = 1, Color = b2 }]);
        using var br = _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2((float)x, (float)y), EndPoint = vertical ? new Vector2((float)x, (float)(y + h)) : new Vector2((float)(x + w), (float)(y + h)) }, stops);
        _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(x, y, x + w, y + h), RadiusX = (float)r, RadiusY = (float)r }, br);
    }

    private void Orb(double cx, double cy, double r, Color4 c)
    {
        using var stops = _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = c }, new GradientStop { Position = 1, Color = new Color4(c.R, c.G, c.B, 0) }]);
        using var br = _ctx.CreateRadialGradientBrush(new RadialGradientBrushProperties { Center = new Vector2((float)cx, (float)cy), RadiusX = (float)r, RadiusY = (float)r }, stops);
        _ctx.FillEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)r, (float)r), br);
    }

    private void Card(double x, double y, double w, double h, bool strong = false) { RR(x, y, w, h, 16, strong ? GlassStrong : Glass); RRStroke(x, y, w, h, 16, Edge); }
    private void Switch(double x, double y, bool on, bool enabled = true)
    {
        RR(x, y, 46, 26, 13, on ? Violet : TrackOff); Dot(x + (on ? 33 : 13), y + 13, 10, new Color4(1, 1, 1, enabled ? 1f : .6f));
        if (!enabled) RR(x, y, 46, 26, 13, new Color4(0, 0, 0, _dark ? .35f : .12f));
    }
    private void Button(OnboardingHit h, string label, bool primary, bool hover)
    {
        if (primary) { Gradient(h.X, h.Y, h.W, h.H, h.H / 2, hover ? Lilac : Violet, hover ? Violet : C(0x6D28D9)); }
        else { RR(h.X, h.Y, h.W, h.H, h.H / 2, hover ? GlassStrong : Glass); RRStroke(h.X, h.Y, h.W, h.H, h.H / 2, Edge); }
        Text(label, _bodyB, h.X, h.Y, h.W, h.H, primary ? new Color4(1, 1, 1, 1) : Ink, center: true);
    }
    private void Segments(OnboardingController c, string id, string[] names, int selected, double y, bool enabled)
    {
        double w = OnboardingController.SegW, x = OnboardingController.SegX, h = OnboardingController.SegH, sw = w / names.Length;
        RR(x, y, w, h, 12, Glass); RRStroke(x, y, w, h, 12, Edge);
        for (int i = 0; i < names.Length; i++)
        {
            bool sel = i == selected, hov = c.Hover == $"{id}:{i}" && enabled;
            if (sel) Gradient(x + i * sw + 3, y + 3, sw - 6, h - 6, 9, enabled ? Violet : TrackOff, enabled ? C(0x6D28D9) : TrackOff);
            else if (hov) RR(x + i * sw + 3, y + 3, sw - 6, h - 6, 9, Glass);
            Text(names[i], _body, x + i * sw, y, sw, h, sel ? new Color4(1, 1, 1, enabled ? 1f : .8f) : enabled ? Ink : Muted, center: true);
        }
    }

    // ───────────────────────────── pantallas
    public void Paint(OnboardingController c)
    {
        var ctx = _ctx; ctx.BeginDraw();
        try
        {
            ctx.Clear(BgTop);
            Gradient(0, 0, OnboardingController.WinW, OnboardingController.WinH, 0, BgTop, BgBottom, vertical: true);
            Orb(130, 90, 260, new Color4(Violet.R, Violet.G, Violet.B, _dark ? .30f : .20f)); Orb(700, 470, 300, new Color4(Lilac.R, Lilac.G, Lilac.B, _dark ? .22f : .26f));
            _logo ??= BrandIcon.Create(ctx, 128);
            switch (c.Step)
            {
                case OnboardingStep.Welcome: Welcome(c); break;
                case OnboardingStep.Components: Header(c, "Elige qué quieres usar", "Puedes cambiarlo cuando quieras en Ajustes. Lo desactivado no se inicia."); Components(c); break;
                case OnboardingStep.Appearance: Header(c, "Apariencia", "Ajustes reales de Hatsu Desktop; el resto sigue el tema de Windows."); Appearance(c); break;
                case OnboardingStep.Windows: Header(c, "Integración con Windows", "Nada se cambia en Windows hasta que pulses «Aplicar y comenzar»."); WindowsStep(c); break;
                case OnboardingStep.Review: Header(c, "Revisa tus elecciones", "Aún no se ha cambiado nada. Puedes volver atrás."); Review(c); break;
                case OnboardingStep.Result: Header(c, c.StepTitle, null); Result(c); break;
            }
            if (c.Step != OnboardingStep.Welcome) Dots(c);
            if (c.Note is { } n && c.Step != OnboardingStep.Result) Text(n, _small, 40, 452, 720, 34, C(0xF59E0B), wrap: true);
        }
        finally { ctx.EndDraw(); }
    }

    private void Dots(OnboardingController c)
    {
        int cur = Math.Max(0, Array.IndexOf(OnboardingController.Flow, c.Step)); if (c.Step == OnboardingStep.Result) cur = OnboardingController.Flow.Length - 1;
        for (int i = 0; i < OnboardingController.Flow.Length; i++) Dot(OnboardingController.WinW / 2 - 40 + i * 20, 520 + 0, i == cur ? 4.5 : 3.2, i <= cur ? Violet : TrackOff);
    }

    private void Header(OnboardingController c, string title, string? sub)
    {
        if (_logo is not null) _ctx.DrawBitmap(_logo, new RawRectF(40, 28, 40 + 40, 28 + 40), 1f, BitmapInterpolationMode.Linear, null);
        Text(title, _h2, 92, 24, 640, 32, Ink); if (sub is not null) Text(sub, _small, 92, 54, 660, 22, Muted);
        Text("Hatsu Desktop", _small, 600, 28, 160, 20, Muted, right: true);
    }

    private void Welcome(OnboardingController c)
    {
        double cx = OnboardingController.WinW / 2;
        Orb(cx, 150, 150, new Color4(Violet.R, Violet.G, Violet.B, _dark ? .45f : .28f));
        if (_logo is not null) _ctx.DrawBitmap(_logo, new RawRectF((float)cx - 56, 92, (float)cx + 56, 204), 1f, BitmapInterpolationMode.Linear, null);
        Text("Bienvenido a Hatsu Desktop", _h1, 0, 220, OnboardingController.WinW, 44, Ink, center: true);
        Text("Tu escritorio, a tu estilo.", _tag, 0, 266, OnboardingController.WinW, 30, Lilac, center: true);
        Text("Hatsu Desktop personaliza Windows con herramientas opcionales: tú eliges cuáles usar y puedes cambiarlo después en Ajustes. Hasta que confirmes, no se modifica nada en tu equipo.",
            _body, 170, 312, 460, 70, Muted, center: true, wrap: true, top: true);
        foreach (var h in c.Hits)
            if (h.Id == "next") Button(h, c.Mode == OnboardingMode.Resume ? "Continuar" : "Comenzar", true, c.Hover == h.Id);
            else Text(h.Id == "skip" ? "Omitir configuración" : "Cancelar", c.Hover == h.Id ? _bodyB : _body, h.X, h.Y, h.W, h.H, c.Hover == h.Id ? Ink : Muted, center: true);
        if (c.Mode != OnboardingMode.Rerun) Text("Omitir usa valores seguros: Dock, Barra e Isla activados; nada se cambia en Windows.", _small, 100, 486, 600, 24, Muted, center: true);
    }

    private void Components(OnboardingController c)
    {
        foreach (var h in c.Hits.Where(h => h.Id.StartsWith("c:")))
        {
            string id = h.Id[2..]; bool on = OnboardingRules.Get(c.Choices, id); bool hov = c.Hover == h.Id;
            Card(h.X, h.Y, h.W, h.H, hov);
            Text(OnboardingRules.Name(id), _bodyB, h.X + 18, h.Y + 12, h.W - 90, 24, Ink);
            Text(OnboardingRules.Description(id), _small, h.X + 18, h.Y + 38, h.W - 92, 40, Muted, wrap: true, top: true);
            Switch(h.X + h.W - 64, h.Y + 14, on);
        }
        Nav(c);
    }

    private void Nav(OnboardingController c)
    {
        foreach (var h in c.Hits.Where(h => h.Id is "back" or "next")) Button(h, h.Id == "back" ? "Atrás" : "Siguiente", h.Id == "next", c.Hover == h.Id);
    }

    private void Appearance(OnboardingController c)
    {
        var ch = c.Choices;
        Text("Tema del Dock", _bodyB, 40, 100, 420, 24, Ink); Segments(c, "theme", OnboardingRules.ThemeNames, (int)ch.Theme, 132, true);
        Text("La Barra superior y la Isla siguen siempre el tema de Windows.", _small, 40, 172, 420, 20, Muted);
        Text("Tamaño del Dock", _bodyB, 40, 178, 420, 24, Ink);
        Segments(c, "size", DockSettings.Presets.Select(p => p.Name).ToArray(), ch.DockSize, 206, ch.Dock);
        Text("Posición del Dock", _bodyB, 40, 252, 420, 24, Ink); Segments(c, "align", OnboardingRules.AlignmentNames, (int)ch.DockAlignment, 280, ch.Dock);
        Text("Fondo de la Barra superior", _bodyB, 40, 326, 420, 24, Ink); Segments(c, "bg", OnboardingRules.BackgroundNames, ch.BarBackground == BarBackground.Glass ? 0 : 1, 354, ch.Bar);
        if (!ch.Dock) Text("El Dock está desactivado: sus opciones no se aplican.", _small, 40, 400, 420, 20, Muted);
        else if (!ch.Bar) Text("La Barra superior está desactivada: su fondo no se aplica.", _small, 40, 400, 420, 20, Muted);
        Preview(c, 490, 100, 270, 330);
        Nav(c);
    }

    /// <summary>Vista previa esquemática (no es una captura): refleja las elecciones reales.</summary>
    private void Preview(OnboardingController c, double x, double y, double w, double h)
    {
        var ch = c.Choices; bool dockDark = ch.Theme == ThemeMode.Dark || (ch.Theme == ThemeMode.System && _dark);
        Card(x - 10, y - 10, w + 20, h + 20);
        Gradient(x, y, w, h, 12, C(0x5B21B6), C(0xC084FC));
        if (ch.Bar) RR(x, y, w, 20, 0, ch.BarBackground == BarBackground.Glass ? new Color4(1, 1, 1, .28f) : new Color4(1, 1, 1, .04f));
        if (ch.Bar) { Text("Hatsu", _small, x + 8, y, 60, 20, new Color4(1, 1, 1, .95f)); Text("12:34", _small, x + w - 68, y, 60, 20, new Color4(1, 1, 1, .95f), right: true); }
        if (ch.Notch) RR(x + w / 2 - 30, y + 2, 60, 16, 8, new Color4(0, 0, 0, .85f));
        if (ch.Dock)
        {
            int sz = ch.DockSize >= 0 ? ch.DockSize : 1; double icon = 12 + sz * 4.5, pad = 6, n = 5, dw = n * (icon + 6) + pad * 2, dh = icon + pad * 2;
            double dx = ch.DockAlignment switch { DockAlignment.Left => x + 12, DockAlignment.Right => x + w - dw - 12, _ => x + (w - dw) / 2 };
            RR(dx, y + h - dh - 14, dw, dh, 12, dockDark ? new Color4(.1f, .1f, .14f, .78f) : new Color4(.96f, .96f, 1f, .82f));
            for (int i = 0; i < n; i++) RR(dx + pad + i * (icon + 6) + 3, y + h - dh - 14 + pad, icon, icon, icon * .24, i == 0 ? Lilac : dockDark ? new Color4(1, 1, 1, .35f) : new Color4(.3f, .2f, .5f, .4f));
        }
        else Text("Sin Dock", _small, x, y + h - 40, w, 24, new Color4(1, 1, 1, .85f), center: true);
        Text("Vista previa esquemática", _small, x - 10, y + h + 12, w + 20, 18, Muted, center: true);
    }

    private void WindowsStep(OnboardingController c)
    {
        var ch = c.Choices;
        foreach (var h in c.Hits.Where(h => h.Id.StartsWith("t:")))
        {
            bool auto = h.Id == "t:autostart", on = auto ? ch.Autostart : ch.HideTaskbar, hov = c.Hover == h.Id && h.Enabled;
            Card(h.X, h.Y, h.W, h.H, hov);
            Text(auto ? "Iniciar Hatsu Desktop al iniciar sesión en Windows" : "Ocultar la barra de tareas original mientras Hatsu Desktop esté funcionando", _bodyB, h.X + 20, h.Y + 12, h.W - 110, 26, h.Enabled ? Ink : Muted);
            Text(auto ? "Añade una entrada de inicio solo para tu usuario. Puedes quitarla cuando quieras en Ajustes. Desactivado por defecto."
                      : h.Enabled ? "Hatsu Desktop la restaura al salir y, si se cierra de golpe, un guardián la devuelve. No se modifica Explorer de forma permanente. Desactivado por defecto."
                                  : "Necesita el Dock: sin él no quedaría ninguna forma de cambiar de ventana.", _small, h.X + 20, h.Y + 42, h.W - 110, 56, Muted, wrap: true, top: true);
            Switch(h.X + h.W - 70, h.Y + 20, on && h.Enabled, h.Enabled);
        }
        Card(40, 344, 720, 80);
        Text("Cerrar ventanas no cierra Hatsu Desktop: sigue en segundo plano, con su icono en la bandeja. Para salir del todo, usa «Salir» en el menú de la bandeja.", _small, 60, 354, 680, 60, Muted, wrap: true, top: true);
        Nav(c);
    }

    private void Review(OnboardingController c)
    {
        Card(40, 96, 720, 380);
        var rows = OnboardingRules.Review(c.Choices); double y = 108, lh = Math.Min(28, 356.0 / Math.Max(1, rows.Count));
        foreach (var (l, v) in rows)
        {
            bool windows = l is "Iniciar con Windows" or "Barra de tareas de Windows";
            Text(l, _bodyB, 64, y, 250, lh, windows ? Lilac : Ink); Text(v, _body, 320, y, 420, lh, v == "Desactivado" ? Muted : Ink); y += lh;
        }
        foreach (var h in c.Hits) Button(h, h.Id == "back" ? "Atrás" : "Aplicar y comenzar", h.Id == "apply", c.Hover == h.Id);
    }

    private void Result(OnboardingController c)
    {
        Card(40, 96, 720, 380);
        if (c.Busy || c.Result is null) { Text("Aplicando tus elecciones y comprobando que cada módulo arranca…", _body, 64, 120, 670, 40, Muted); return; }
        double y = 108;
        foreach (var it in c.Result.Items)
        {
            var col = it.Outcome == ApplyOutcome.Ok ? Good : it.Outcome == ApplyOutcome.Warning ? Warn : Bad;
            Dot(66, y + 13, 6, col); Text(it.Label, _bodyB, 84, y, 250, 26, Ink); Text(it.Detail ?? (it.Outcome == ApplyOutcome.Ok ? "Listo" : ""), _small, 340, y, 400, 26, it.Outcome == ApplyOutcome.Ok ? Muted : col); y += 28;
            if (y > 450) break;
        }
        string msg = c.Result.RolledBack ? "No se aplicó nada: se restauró el estado anterior. Puedes reintentar o cerrar el asistente." : c.Result.EssentialFailed ? "Algo esencial falló." : c.Result.HasWarnings ? "Algunas cosas no se aplicaron. Puedes reintentar o continuar y revisarlo después en Ajustes." : "Todo se aplicó y los módulos están en marcha. Puedes cambiar lo que quieras en Ajustes.";
        Text(msg, _small, 40, 446 + 4, 720, 40, c.Result.AllOk ? Good : Warn, wrap: true, top: true);
        foreach (var h in c.Hits) Button(h, h.Id switch { "back" => "Volver a revisar", "retry" => "Reintentar", "cancel" => "Cerrar sin aplicar", "accept" => "Continuar de todos modos", _ => "Empezar" }, h.Id is "finish" or "accept" || (h.Id == "retry" && c.Result.EssentialFailed), c.Hover == h.Id);
    }

    public void Dispose() { _logo?.Dispose(); _b.Dispose(); _h1.Dispose(); _h2.Dispose(); _body.Dispose(); _bodyB.Dispose(); _small.Dispose(); _tag.Dispose(); _dw.Dispose(); }
}
