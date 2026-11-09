using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Rendering;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Hatsu Live Text: atajo → el capturador de regiones de siempre (<see cref="CaptureOverlay"/>, pantalla congelada, varios monitores, Esc cancela) → OCR LOCAL de Windows (<c>Windows.Media.Ocr</c>, sin Internet y
/// sin enviar nada fuera) → panel con el texto y copia al portapapeles. Los idiomas disponibles son los paquetes de reconocimiento que Windows tenga instalados; si no hay ninguno se explica cómo añadirlo.
/// El texto copiado entra en el portapapeles normal, así que el historial de la Isla lo recoge sin un historial aparte.
/// </summary>
internal sealed class LiveTextFeature : IHatsuFeature
{
    public const string Owner = "livetext";
    private readonly BarHost _host; private readonly Action<string> _log;
    private FeatureScope? _scope; private BarConfig _cfg = new(); private LiveTextPanel? _panel; private bool _busy;

    public LiveTextFeature(BarHost host, Action<string> log) { _host = host; _log = log; }
    public string Id => FeatureConfig.LiveText;
    public string Title => "Live Text";
    public string Description => "Reconoce el texto de una región de la pantalla con el OCR local de Windows y lo copia.";
    public string? Info()
    {
        try { var l = AvailableLanguages(); return l.Count == 0 ? "sin idiomas de reconocimiento instalados" : "idiomas: " + string.Join(", ", l); } catch (Exception) { return null; }
    }

    public void Start(FeatureScope scope)
    {
        _scope = scope; _host.LiveTextRun = Run; scope.Add(() => _host.LiveTextRun = null);
        _host.RegisterModuleAction(scope, new SpotAction("livetext:run", "Live Text: reconocer texto de la pantalla", "Elige una región y copia su texto (módulo Live Text)", "live text ocr reconocer texto pantalla", Id, ActionRisk.Normal, [], null,
            () => { _host.PostToUi(Run); return ActionResult.Success; }));
        scope.Add(() => { _panel?.Dispose(); _panel = null; });
    }

    public void Reconfigure(BarConfig cfg)
    {
        _cfg = cfg;
        var r = _host.Binder.Bind(_scope, Owner, "run", "Live Text", "Barra · Live Text", cfg.LtHotkey, Run);
        if (!r.Ok && cfg.LtHotkey.Length > 0) _log($"[livetext] atajo {cfg.LtHotkey}: {r.Explain()}");
    }

    public static IReadOnlyList<string> AvailableLanguages() => OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public void Run()
    {
        if (_busy || _panel is not null) return;
        if (!_host.BeginRegionPick(OnRegion, "Live Text")) _log("[livetext] no se abrió: ya hay una captura en curso");
    }

    private void OnRegion(byte[] frame, CaptureRect frameRect, CaptureRect region)
    {
        if (region.W < 4 || region.H < 4) return;
        _busy = true; var px = CaptureGeometry.Crop(frame, frameRect, region); var cfg = _cfg; var token = _scope?.Token ?? CancellationToken.None;
        Task.Run(async () =>
        {
            string? text = null, lang = null, error = null;
            try { (text, lang) = await RecognizeAsync(px, region.W, region.H, cfg).ConfigureAwait(false); }
            catch (Exception e) { error = e is InvalidOperationException ? e.Message : "No se pudo reconocer el texto: " + e.GetType().Name + ": " + e.Message; _log("[livetext] " + error); }
            if (token.IsCancellationRequested) { _busy = false; return; }                                  // el módulo se apagó mientras se reconocía
            _host.PostToUi(() => { _busy = false; if (!token.IsCancellationRequested) ShowResult(text, lang, error, region); });
        });
    }

    private async Task<(string Text, string Lang)> RecognizeAsync(byte[] bgra, int w, int h, BarConfig cfg)
    {
        var avail = AvailableLanguages(); var profile = Windows.System.UserProfile.GlobalizationPreferences.Languages.ToList();
        string? tag = LiveText.PickLanguage(avail, cfg.LtLanguage, profile);
        if (tag is null) throw new InvalidOperationException(LiveText.NoLanguages);
        var lang = OcrEngine.AvailableRecognizerLanguages.First(l => l.LanguageTag == tag); var engine = OcrEngine.TryCreateFromLanguage(lang) ?? throw new InvalidOperationException("Windows no pudo iniciar el reconocimiento de texto para " + tag);
        int max = (int)OcrEngine.MaxImageDimension;
        if (w > max || h > max) throw new InvalidOperationException($"La región es demasiado grande para el reconocimiento ({w}×{h}; máximo {max} px por lado). Elige una región más pequeña.");
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bmp);
        var text = LiveText.Join(result.Lines.Select(l => l.Text).ToList(), cfg.LtKeepLineBreaks);
        if (text.Length == 0) throw new InvalidOperationException("No se encontró texto en la región (idioma de reconocimiento: " + tag + ").");
        return (text, tag);
    }

    private void ShowResult(string? text, string? lang, string? error, CaptureRect region)
    {
        bool copied = false;
        bool shelved = false;
        if (text is not null && _cfg.LtAutoCopy) { _host.CopyTextPublic(text); copied = true; }
        if (text is not null && _cfg.LtAutoShelf && _host.ShelfAvailable) { _host.SendToShelf(text); shelved = true; }
        _panel?.Dispose();
        _panel = new LiveTextPanel(region, text, lang, error, copied, shelved, t => _host.CopyTextPublic(t), _host.ShelfAvailable ? t => _host.SendToShelf(t) : null, () => { var p = _panel; _panel = null; p?.Dispose(); }, _log);
        _panel.Show();
        _log(error is null ? $"[livetext] {text!.Length} caracteres reconocidos ({lang}){(copied ? ", copiados" : "")}" : "[livetext] sin resultado");
    }
}

/// <summary>Panel con el texto reconocido: Intro o «Copiar» copian, Esc o clic fuera cierran. Dibujado con Direct2D sobre el monitor de la región.</summary>
internal sealed class LiveTextPanel : IDisposable
{
    private readonly CaptureRect _region; private readonly string? _text, _lang, _error; private bool _copied; private readonly Action<string> _copy; private readonly Action<string>? _toShelf; private bool _shelved; private readonly Action _closed; private readonly Action<string> _log;
    private D2DOverlay? _ov; private IDWriteFactory? _dw; private IDWriteTextFormat? _title, _body, _btn; private CaptureMonitor _mon = null!; private McRectLocal _card, _copyBtn, _closeBtn, _shelfBtn; private bool _gone;
    private readonly record struct McRectLocal(double X, double Y, double W, double H) { public bool Contains(double x, double y) => x >= X && x <= X + W && y >= Y && y <= Y + H; }

    public LiveTextPanel(CaptureRect region, string? text, string? lang, string? error, bool copied, bool shelved, Action<string> copy, Action<string>? toShelf, Action closed, Action<string> log)
    { _region = region; _text = text; _lang = lang; _error = error; _copied = copied; _shelved = shelved; _copy = copy; _toShelf = toShelf; _closed = closed; _log = log; }

    public void Show()
    {
        var mons = ScreenGrabber.Monitors(); int cx = _region.X + _region.W / 2, cy = _region.Y + _region.H / 2;
        _mon = mons.FirstOrDefault(m => cx >= m.Bounds.X && cx < m.Bounds.X + m.Bounds.W && cy >= m.Bounds.Y && cy < m.Bounds.Y + m.Bounds.H) ?? mons[0];
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _title = _dw.CreateTextFormat("Segoe UI Semibold", 16); _body = _dw.CreateTextFormat("Segoe UI", 14); _btn = _dw.CreateTextFormat("Segoe UI Semibold", 14); _btn.TextAlignment = TextAlignment.Center; _btn.ParagraphAlignment = ParagraphAlignment.Center;
        _ov = new D2DOverlay("LiveText", clickThrough: false, Msg, _log); _ov.Show(_mon.Bounds, _mon.Scale, focus: true); Redraw();
    }

    private double S => _mon.Scale;
    private IntPtr? Msg(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        double x = (short)((long)l & 0xFFFF) / S, y = (short)(((long)l >> 16) & 0xFFFF) / S;
        switch ((int)msg)
        {
            case 0x202:
                if (_text is not null && _copyBtn.Contains(x, y)) { _copy(_text); _copied = true; Redraw(); }
                else if (_text is not null && _toShelf is not null && _shelfBtn.Contains(x, y)) { _toShelf(_text); _shelved = true; Redraw(); }
                else if (_closeBtn.Contains(x, y) || !_card.Contains(x, y)) Close();
                return IntPtr.Zero;
            case 0x100:
                int vk = (int)w;
                if (vk == 0x1B) Close(); else if ((vk == 0x0D || vk == 0x43) && _text is not null) { _copy(_text); _copied = true; Redraw(); }
                return IntPtr.Zero;
            case 0x6 when ((long)w & 0xFFFF) == 0: Close(); return IntPtr.Zero;
        }

        return null;
    }

    private void Close() { if (_gone) return; _gone = true; _closed(); }

    /// <summary>Globo junto a la región reconocida (debajo, o encima si no hay lugar): resalta la región, dice «Texto copiado» y muestra un adelanto con los botones justos. Clic fuera o Esc lo cierran.</summary>
    private void Redraw() => _ov?.Draw((c, w, h) =>
    {
        using var b = c.CreateSolidColorBrush(new Color4(0, 0, 0, 0.30f)); c.FillRectangle(new RawRectF(0, 0, (float)w, (float)h), b);
        double rx = (_region.X - _mon.Bounds.X) / S, ry = (_region.Y - _mon.Bounds.Y) / S, rw = _region.W / S, rh = _region.H / S;
        var sel = new RoundedRectangle { Rect = new RawRectF((float)rx, (float)ry, (float)(rx + rw), (float)(ry + rh)), RadiusX = 6, RadiusY = 6 };
        b.Color = new Color4(0.22f, 0.74f, 0.97f, 0.14f); c.FillRoundedRectangle(sel, b); b.Color = new Color4(0.22f, 0.74f, 0.97f, 1f); c.DrawRoundedRectangle(sel, b, 2f);

        var (preview, more) = _text is null ? (_error ?? "", 0) : LiveText.Preview(_text);
        double cw = Math.Min(380, w - 40); using var tl = _dw!.CreateTextLayout(preview, _body!, (float)(cw - 32), 78f); tl.WordWrapping = WordWrapping.Wrap;
        double th = Math.Min(78, tl.Metrics.Height); double ch = 14 + 24 + 4 + th + (more > 0 ? 20 : 0) + 12 + 32 + 14;
        double cx = Math.Clamp(rx + rw - cw, 20, Math.Max(20, w - cw - 20)), cy = ry + rh + 12;
        if (cy + ch > h - 20) cy = ry - 12 - ch; if (cy < 20) cy = Math.Clamp(ry + rh - ch - 12, 20, Math.Max(20, h - ch - 20));
        _card = new McRectLocal(cx, cy, cw, ch);
        var rr = new RoundedRectangle { Rect = new RawRectF((float)cx, (float)cy, (float)(cx + cw), (float)(cy + ch)), RadiusX = 14, RadiusY = 14 };
        b.Color = new Color4(0.07f, 0.04f, 0.16f, 0.97f); c.FillRoundedRectangle(rr, b); b.Color = new Color4(1, 1, 1, 0.18f); c.DrawRoundedRectangle(rr, b, 1);

        string head = _error is not null ? "Live Text" : _copied ? "✓ Texto copiado" : "Texto reconocido";
        b.Color = _error is null && _copied ? new Color4(0.43f, 0.91f, 0.72f, 1) : new Color4(1, 1, 1, 0.95f);
        using (var t = _dw.CreateTextLayout(head + (_error is null && _lang is not null ? $" · {_lang}" : ""), _title!, (float)(cw - 32), 24)) c.DrawTextLayout(new Vector2((float)cx + 16, (float)cy + 12), t, b);
        b.Color = new Color4(1, 1, 1, _error is null ? 0.82f : 0.75f); c.DrawTextLayout(new Vector2((float)cx + 16, (float)cy + 14 + 24 + 4), tl, b);
        double by = cy + 14 + 24 + 4 + th + (more > 0 ? 20 : 0) + 12;
        if (more > 0) { b.Color = new Color4(1, 1, 1, 0.5f); using var m = _dw.CreateTextLayout($"… y {more} línea(s) más (se copian completas)", _body!, (float)(cw - 32), 20); c.DrawTextLayout(new Vector2((float)cx + 16, (float)(by - 12 - 20)), m, b); }
        _closeBtn = new McRectLocal(cx + cw - 16 - 34, by, 34, 32);
        _copyBtn = _text is null ? default : new McRectLocal(_closeBtn.X - 8 - 92, by, 92, 32);
        _shelfBtn = _text is null || _toShelf is null ? default : new McRectLocal(_copyBtn.X - 8 - 152, by, 152, 32);
        void Button(McRectLocal r, string label, bool primary) { b.Color = primary ? new Color4(0.49f, 0.23f, 0.93f, 1) : new Color4(1, 1, 1, 0.14f); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF((float)r.X, (float)r.Y, (float)(r.X + r.W), (float)(r.Y + r.H)), RadiusX = 16, RadiusY = 16 }, b); b.Color = new Color4(1, 1, 1, 1); using var l = _dw!.CreateTextLayout(label, _btn!, (float)r.W, (float)r.H); c.DrawTextLayout(new Vector2((float)r.X, (float)r.Y), l, b); }
        if (_text is not null && _toShelf is not null) Button(_shelfBtn, _shelved ? "En la estantería ✓" : "A la estantería", false);
        if (_text is not null) Button(_copyBtn, _copied ? "Copiado ✓" : "Copiar", !_copied);
        Button(_closeBtn, "✕", false);
    });

    public void Dispose() { _gone = true; _ov?.Dispose(); _ov = null; _title?.Dispose(); _body?.Dispose(); _btn?.Dispose(); _dw?.Dispose(); }
}
