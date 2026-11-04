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
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx; private readonly IDWriteFactory _dw; private readonly bool _dark; public string? Watermark { get; set; }
    private readonly IDWriteTextFormat _h1, _h2, _body, _bodyB, _small, _tag, _chip, _tiny;
    private readonly ID2D1SolidColorBrush _b; private ID2D1Bitmap? _logo;

    // paleta Flux de referencia: #7C3AED, #A78BFA, #C4B5FD, #EDE9FE, #0B0618 (no sustituye los temas de la interfaz). El texto sobre superficies claras usa variantes más oscuras (OnboardingPalette).
    private static Color4 C(int rgb, float a = 1f) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    private static readonly Color4 Violet = C(0x7C3AED), Lilac = C(0xA78BFA), Night = C(0x0B0618), VioletDeep = C(0x6D28D9), VioletDeeper = C(0x5B21B6);
    private readonly OnboardingPalette _p;
    private Color4 BgTop => C(_p.BgTop); private Color4 BgBottom => C(_p.BgBottom);
    private Color4 Ink => C(_p.Ink); private Color4 Muted => C(_p.Muted); private Color4 AccentText => C(_p.AccentText);
    private Color4 Warn => C(_p.Warn); private Color4 Bad => C(_p.Bad); private Color4 Good => C(_p.Good); private Color4 Neutral => C(_p.Neutral);
    private Color4 Glass => _dark ? new Color4(1, 1, 1, .075f) : new Color4(1, 1, 1, .66f);
    private Color4 GlassStrong => _dark ? new Color4(1, 1, 1, .14f) : new Color4(1, 1, 1, .92f);
    private Color4 Edge => _dark ? new Color4(1, 1, 1, .16f) : new Color4(.486f, .227f, .929f, .20f);
    private Color4 TrackOff => _dark ? new Color4(1, 1, 1, .20f) : new Color4(.35f, .29f, .53f, .28f);
    private static RawRectF R(double l, double t, double r, double b) => new((float)l, (float)t, (float)r, (float)b);

    public OnboardingPainter(D2DDevice dev, bool dark)
    {
        _dev = dev; _ctx = dev.Context; _dark = dark; _p = OnboardingPalette.For(dark); _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _h1 = _dw.CreateTextFormat("Segoe UI Semibold", 30); _h2 = _dw.CreateTextFormat("Segoe UI Semibold", 22); _body = _dw.CreateTextFormat("Segoe UI", 14.5f);
        _bodyB = _dw.CreateTextFormat("Segoe UI Semibold", 14.5f); _small = _dw.CreateTextFormat("Segoe UI", 12.5f); _tag = _dw.CreateTextFormat("Segoe UI", 18); _chip = _dw.CreateTextFormat("Segoe UI Semibold", 11.5f); _tiny = _dw.CreateTextFormat("Segoe UI", 11.5f);
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
        if (primary) { Gradient(h.X, h.Y, h.W, h.H, h.H / 2, hover ? VioletDeep : Violet, hover ? VioletDeeper : VioletDeep); }
        else { RR(h.X, h.Y, h.W, h.H, h.H / 2, hover ? GlassStrong : Glass); RRStroke(h.X, h.Y, h.W, h.H, h.H / 2, Edge); }
        Text(label, _bodyB, h.X, h.Y, h.W, h.H, primary ? new Color4(1, 1, 1, 1) : Ink, center: true);
    }
    // ───────────────────────────── pantallas (todas las medidas salen de OnboardingLayout)
    private Dictionary<string, LayoutBox> _l = new();
    private LayoutBox L(string id) => _l[id];
    private bool Has(string id) => _l.ContainsKey(id);
    private void TextIn(string id, string? s, IDWriteTextFormat f, Color4 c, bool center = false, bool right = false, bool wrap = false, bool top = false) { var b = L(id); Text(s, f, b.X, b.Y, b.W, b.H, c, center, right, wrap, top); }

    public void Paint(OnboardingController c)
    {
        _l = OnboardingLayout.Compute(c).ToDictionary(x => x.Id, x => x); var ctx = _ctx; ctx.BeginDraw();
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
            if (Has("dots")) Dots(c);
            if (Has("note")) TextIn("note", c.Note, _small, Warn, wrap: true, top: true);
            if (Watermark is { } wm) Text(wm, _small, 0, 4, OnboardingController.WinW, 14, Muted, center: true);
        }
        finally { ctx.EndDraw(); }
    }

    private void Dots(OnboardingController c)
    {
        var d = L("dots"); int cur = Math.Max(0, Array.IndexOf(OnboardingController.Flow, c.Step));
        for (int i = 0; i < OnboardingController.Flow.Length; i++) Dot(d.X + 10 + i * 20, d.Y + d.H / 2, i == cur ? 4.5 : 3.2, i <= cur ? Violet : TrackOff);
    }

    private void Header(OnboardingController c, string title, string? sub)
    {
        var lg = L("h.logo"); if (_logo is not null) _ctx.DrawBitmap(_logo, new RawRectF((float)lg.X, (float)lg.Y, (float)lg.Right, (float)lg.Bottom), 1f, BitmapInterpolationMode.Linear, null);
        TextIn("h.title", title, _h2, Ink); if (sub is not null) TextIn("h.sub", sub, _small, Muted); TextIn("h.brand", "Hatsu Desktop", _small, Muted, right: true);
    }

    private void Welcome(OnboardingController c)
    {
        double cx = OnboardingController.WinW / 2;
        Orb(cx, 140, 150, new Color4(Violet.R, Violet.G, Violet.B, _dark ? .45f : .28f));
        var lg = L("w.logo"); if (_logo is not null) _ctx.DrawBitmap(_logo, new RawRectF((float)lg.X, (float)lg.Y, (float)lg.Right, (float)lg.Bottom), 1f, BitmapInterpolationMode.Linear, null);
        bool mig = c.Mode == OnboardingMode.Migration;
        TextIn("w.title", mig ? "WinMac ahora es Hatsu Desktop" : "Bienvenido a Hatsu Desktop", _h1, Ink, center: true); TextIn("w.tag", mig ? "Tu configuración sigue como estaba." : "Tu escritorio, a tu estilo.", _tag, AccentText, center: true);
        TextIn("w.body", mig ? OnboardingLayout.MigrationBody : OnboardingLayout.WelcomeBody, _body, Muted, center: true, wrap: true, top: true);
        foreach (var h in c.Hits)
            if (h.Id is "next" && !mig) Button(h, c.Mode == OnboardingMode.Resume ? "Continuar" : "Comenzar", true, c.Hover == h.Id);
            else if (h.Id == "keep") Button(h, "Mantener mi configuración", true, c.Hover == h.Id);
            else Text(h.Id == "skip" ? "Omitir configuración" : h.Id == "next" ? "Revisar componentes" : "Cancelar", c.Hover == h.Id ? _bodyB : _body, h.X, h.Y, h.W, h.H, c.Hover == h.Id ? Ink : Muted, center: true);
        if (Has("w.note")) TextIn("w.note", mig ? OnboardingLayout.MigrationNote : OnboardingLayout.WelcomeSkipNote, _small, Muted, center: true, wrap: true, top: true);
    }

    private void Components(OnboardingController c)
    {
        foreach (var h in c.Hits.Where(h => h.Id.StartsWith("c:")))
        {
            string id = h.Id[2..]; bool on = OnboardingRules.Get(c.Choices, id); bool hov = c.Hover == h.Id;
            Card(h.X, h.Y, h.W, h.H, hov);
            TextIn("cn:" + id, OnboardingRules.Name(id), _bodyB, Ink); TextIn("cd:" + id, OnboardingRules.Description(id), _small, Muted, wrap: true, top: true);
            var sw = L("cs:" + id); Switch(sw.X, sw.Y, on);
            if (Has("cr:" + id)) { var r = L("cr:" + id); RR(r.X, r.Y, r.W, r.H, 10, new Color4(Violet.R, Violet.G, Violet.B, _dark ? .28f : .14f)); Text(OnboardingRules.RequiresBarText, _chip, r.X, r.Y, r.W, r.H, AccentText, center: true); }
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
        TextIn("a.l1", "Tema del Dock", _bodyB, Ink); SegGroup(c, "theme", OnboardingRules.ThemeNames, (int)ch.Theme, true);
        TextIn("a.n1", OnboardingLayout.ThemeNote, _small, Muted, wrap: true, top: true);
        TextIn("a.l2", "Tamaño del Dock", _bodyB, Ink); SegGroup(c, "size", DockSettings.Presets.Select(p => p.Name).ToArray(), ch.DockSize, ch.Dock);
        TextIn("a.l3", "Posición del Dock", _bodyB, Ink); SegGroup(c, "align", OnboardingRules.AlignmentNames, (int)ch.DockAlignment, ch.Dock);
        TextIn("a.l4", "Fondo de la Barra superior", _bodyB, Ink); SegGroup(c, "bg", OnboardingRules.BackgroundNames, ch.BarBackground == BarBackground.Glass ? 0 : 1, ch.Bar);
        if (Has("a.off")) TextIn("a.off", !ch.Dock ? OnboardingLayout.AppearanceDockOff : OnboardingLayout.AppearanceBarOff, _small, Muted, wrap: true, top: true);
        Preview(c);
        Nav(c);
    }

    private void SegGroup(OnboardingController c, string id, string[] names, int selected, bool enabled)
    {
        var box = L(id + ".seg"); RR(box.X, box.Y, box.W, box.H, 12, Glass); RRStroke(box.X, box.Y, box.W, box.H, 12, Edge);
        for (int i = 0; i < names.Length; i++)
        {
            var s = L($"{id}:{i}"); bool sel = i == selected, hov = c.Hover == s.Id && enabled;
            if (sel) Gradient(s.X + 3, s.Y + 3, s.W - 6, s.H - 6, 9, enabled ? Violet : TrackOff, enabled ? VioletDeep : TrackOff);
            else if (hov) RR(s.X + 3, s.Y + 3, s.W - 6, s.H - 6, 9, Glass);
            Text(names[i], _body, s.X, s.Y, s.W, s.H, sel ? new Color4(1, 1, 1, enabled ? 1f : .8f) : enabled ? Ink : Muted, center: true);
        }
    }

    /// <summary>Vista previa esquemática HORIZONTAL de escritorio (16:10; no es una captura): refleja las elecciones reales.</summary>
    private void Preview(OnboardingController c)
    {
        var ch = c.Choices; var card = L("p.card"); var d = L("p.desk"); bool dockDark = ch.Theme == ThemeMode.Dark || (ch.Theme == ThemeMode.System && _dark);
        Card(card.X, card.Y, card.W, card.H);
        Gradient(d.X, d.Y, d.W, d.H, 8, C(0x5B21B6), C(0xC084FC));
        if (ch.Bar) { RR(d.X, d.Y, d.W, 14, 0, ch.BarBackground == BarBackground.Glass ? new Color4(1, 1, 1, .30f) : new Color4(1, 1, 1, .05f)); Text("Hatsu", _tiny, d.X + 6, d.Y, 50, 14, new Color4(1, 1, 1, .95f)); Text("12:34", _tiny, d.Right - 56, d.Y, 50, 14, new Color4(1, 1, 1, .95f), right: true); }
        if (ch.Notch) RR(d.X + d.W / 2 - 22, d.Y + 2, 44, 10, 5, new Color4(0, 0, 0, .85f));
        RR(d.X + 40, d.Y + 34, d.W - 120, d.H - 80, 6, new Color4(1, 1, 1, .16f)); RR(d.X + 40, d.Y + 34, d.W - 120, 10, 6, new Color4(1, 1, 1, .22f));   // una ventana de muestra
        if (ch.Dock)
        {
            int sz = ch.DockSize >= 0 ? ch.DockSize : 1; double icon = 8 + sz * 3.2, pad = 4, n = 5, gap = 4, dw = n * (icon + gap) + pad * 2 - gap, dh = icon + pad * 2;
            double dx = ch.DockAlignment switch { DockAlignment.Left => d.X + 10, DockAlignment.Right => d.Right - dw - 10, _ => d.X + (d.W - dw) / 2 }, dy = d.Bottom - dh - 8;
            RR(dx, dy, dw, dh, 8, dockDark ? new Color4(.1f, .1f, .14f, .80f) : new Color4(.96f, .96f, 1f, .85f));
            for (int i = 0; i < n; i++) RR(dx + pad + i * (icon + gap), dy + pad, icon, icon, icon * .24, i == 0 ? Lilac : dockDark ? new Color4(1, 1, 1, .38f) : new Color4(.3f, .2f, .5f, .45f));
        }
        else Text("Sin Dock", _tiny, d.X, d.Bottom - 26, d.W, 20, new Color4(1, 1, 1, .9f), center: true);
        TextIn("p.cap", "Vista previa esquemática", _small, Muted, center: true);
    }

    private void WindowsStep(OnboardingController c)
    {
        var ch = c.Choices;
        foreach (var h in c.Hits.Where(h => h.Id.StartsWith("t:")))
        {
            string key = h.Id[2..]; bool auto = key == "autostart", on = auto ? ch.Autostart : ch.HideTaskbar, hov = c.Hover == h.Id && h.Enabled;
            Card(h.X, h.Y, h.W, h.H, hov);
            TextIn("tt:" + key, auto ? OnboardingLayout.AutostartTitle : OnboardingLayout.HideTitle, _bodyB, h.Enabled ? Ink : Muted);
            TextIn("td:" + key, auto ? OnboardingLayout.AutostartDesc : h.Enabled ? OnboardingLayout.HideDesc : OnboardingLayout.HideNeedsDock, _small, Muted, wrap: true, top: true);
            var sw = L("ts:" + key); Switch(sw.X, sw.Y, on && h.Enabled, h.Enabled);
        }
        var ic = L("i.card"); Card(ic.X, ic.Y, ic.W, ic.H); TextIn("i.text", OnboardingLayout.CloseInfo, _small, Muted, wrap: true, top: true);
        Nav(c);
    }

    private void Review(OnboardingController c)
    {
        var card = L("r.card"); Card(card.X, card.Y, card.W, card.H);
        var rows = OnboardingRules.Review(c.Choices);
        for (int i = 0; i < rows.Count; i++)
        {
            var (l, v) = rows[i]; bool windows = l is "Iniciar con Windows" or "Barra de tareas de Windows";
            TextIn("rl:" + i, l, _bodyB, windows ? AccentText : Ink); TextIn("rv:" + i, v, _body, v == "Desactivado" ? Muted : Ink);
        }
        foreach (var h in c.Hits) Button(h, h.Id == "back" ? "Atrás" : "Aplicar y comenzar", h.Id == "apply", c.Hover == h.Id);
    }

    private void Result(OnboardingController c)
    {
        var card = L("x.card"); Card(card.X, card.Y, card.W, card.H);
        if (c.Busy || c.Result is null) { TextIn("x.busy", "Aplicando tus elecciones y comprobando que cada módulo arranca…", _body, Muted, wrap: true, top: true); return; }
        var r = c.Result;
        for (int i = 0; i < Math.Min(r.Items.Count, OnboardingLayout.MaxResultRows); i++)
        {
            var it = r.Items[i]; var d = L("xd:" + i);
            var col = it.Inactive ? Neutral : it.Outcome == ApplyOutcome.Ok ? Good : it.Outcome == ApplyOutcome.Warning ? Warn : Bad;
            Dot(d.X + 6, d.Y + 6, 6, col); TextIn("xl:" + i, it.Label, _bodyB, Ink);
            TextIn("xt:" + i, it.Detail ?? (it.Outcome == ApplyOutcome.Ok ? "Listo" : ""), _small, it.Outcome == ApplyOutcome.Ok ? Muted : col);
        }
        if (Has("x.more")) TextIn("x.more", $"…y {r.Items.Count - OnboardingLayout.MaxResultRows} más (ver el registro)", _small, Muted);
        var ln = L("x.line"); RR(ln.X, ln.Y, ln.W, 1, 0, Edge);
        TextIn("x.msg", c.ResultMessage, _small, r.AllOk ? Muted : Warn, wrap: true, top: true);
        if (Has("details")) { var dt = L("details"); Text(c.ShowDetails ? "Detalles técnicos ▴" : "Detalles técnicos ▾", _small, dt.X, dt.Y, dt.W, dt.H, AccentText); }
        if (Has("x.tech")) TextIn("x.tech", string.Join("\n", r.Items.Where(i => i.Technical is not null).Select(i => $"{i.Label}: {i.Technical}")), _tiny, Muted, wrap: true, top: true);
        foreach (var h in c.Hits.Where(h => h.Id != "details")) Button(h, h.Id switch { "back" => "Volver a revisar", "retry" => "Reintentar", "cancel" => "Cerrar sin aplicar", "accept" => "Continuar de todos modos", _ => "Empezar" }, h.Id is "finish" or "accept" || (h.Id == "retry" && r.EssentialFailed), c.Hover == h.Id);
    }

    public void Dispose() { _logo?.Dispose(); _b.Dispose(); _h1.Dispose(); _h2.Dispose(); _body.Dispose(); _bodyB.Dispose(); _small.Dispose(); _tag.Dispose(); _chip.Dispose(); _tiny.Dispose(); _dw.Dispose(); }
}
