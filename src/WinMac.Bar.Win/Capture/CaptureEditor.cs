using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// The basic capture editor (docs/26): crop, pen, arrow, rectangle, text and highlighter over a screenshot, with undo/redo, copy, save and save as.
/// Non-destructive: the original file is never changed; «Guardar» writes «… (editada).png» next to it and «Guardar como» asks where. The image is drawn and
/// exported with Direct2D at its real resolution (the view only scales it to fit). A normal window of the bar process (no separate app).
/// </summary>
internal sealed class CaptureEditor : IDisposable
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000;
    private const int WM_SIZE = 0x5, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_KEYDOWN = 0x100, WM_CHAR = 0x102, WM_DPICHANGED = 0x2E0;

    private readonly BarNative.WndProc _proc; private readonly Action<string> _log; private readonly Action<byte[], int, int> _copy; private readonly Action _closed;
    private readonly string _path; private readonly byte[] _px; private readonly EditDocument _doc;
    private IntPtr _hwnd; private D2DDevice? _dev; private ID2D1DeviceContext? _ctx; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _target; private ID2D1Bitmap? _image;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _ui; private ID2D1SolidColorBrush? _b; private ID2D1StrokeStyle? _round;
    private readonly Dictionary<int, IDWriteTextFormat> _textFormats = [];
    private EditTool _tool = EditTool.Arrow; private EditColor _color = EditColor.Red; private float _scale = 1;
    private (float X, float Y)? _from; private (float X, float Y) _cur; private List<(float X, float Y)>? _stroke;
    private (float X, float Y)? _textAt; private string _text = "";
    private string? _status; private bool _saved, _disposed;
    private readonly List<(string Id, RawRectF Rect)> _buttons = [];

    private CaptureEditor(string path, byte[] px, int w, int h, Action<byte[], int, int> copy, Action closed, Action<string> log)
    {
        _path = path; _px = px; _doc = new EditDocument(w, h); _copy = copy; _closed = closed; _log = log; _proc = WndProcImpl;
    }

    public string Path => _path;
    /// <summary>Toolbar buttons in client px (for the CI to click like a user).</summary>
    public IReadOnlyList<(string Id, RawRectF Rect)> Buttons => _buttons;
    public IntPtr Handle => _hwnd;

    /// <summary>Opens the editor for an image file (decoded with Windows' own codecs). Throws if it is not a readable image.</summary>
    public static async Task<(byte[] Px, int W, int H)> LoadAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);   // read once: the original is not kept open or locked
        using var ms = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var wr = new global::Windows.Storage.Streams.DataWriter(ms.GetOutputStreamAt(0))) { wr.WriteBytes(bytes); await wr.StoreAsync(); await wr.FlushAsync(); wr.DetachStream(); }
        var dec = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms);
        var data = await dec.GetPixelDataAsync(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            new global::Windows.Graphics.Imaging.BitmapTransform(), global::Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, global::Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return (data.DetachPixelData(), (int)dec.OrientedPixelWidth, (int)dec.OrientedPixelHeight);
    }

    public static CaptureEditor Open(string path, byte[] px, int w, int h, Action<byte[], int, int> copy, Action closed, Action<string> log)
    {
        var e = new CaptureEditor(path, px, w, h, copy, closed, log); e.Create(); return e;
    }

    private void Create()
    {
        BarNative.GetCursorPos(out var c);
        _hwnd = InstanceWindows.Create("WinMacCaptureEditor", 32512, _proc, 0, "Editar captura — " + System.IO.Path.GetFileName(_path), WS_OVERLAPPEDWINDOW, c.X - 200, c.Y - 200, 400, 300);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana del editor: " + Marshal.GetLastWin32Error());
        _scale = Math.Max(1, BarNative.GetDpiForWindow(_hwnd)) / 96f;
        // 80 % of the monitor where it opens, never bigger than needed for the image at 100 %
        var mon = ScreenGrabber.Monitors().FirstOrDefault(m => m.Bounds.Contains(c.X, c.Y)) ?? ScreenGrabber.Monitors().FirstOrDefault(m => m.Primary);
        var area = mon?.Bounds ?? new CaptureRect(0, 0, 1280, 800);
        int ww = (int)Math.Min(area.W * 0.8, Math.Max(760 * _scale, _doc.Width + 80 * _scale)), wh = (int)Math.Min(area.H * 0.8, Math.Max(520 * _scale, _doc.Height + 160 * _scale));
        BarNative.SetWindowPos(_hwnd, IntPtr.Zero, area.X + (area.W - ww) / 2, area.Y + (area.H - wh) / 2, ww, wh, 0x0004 | 0x0040);

        _dev = new D2DDevice(); _ctx = _dev.Context; _dev.SetDpi(1.0);
        _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _round = _dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _ui = _dw.CreateTextFormat("Segoe UI", 13 * _scale); _ui.WordWrapping = WordWrapping.NoWrap;
        BarNative.GetClientRect(_hwnd, out var rc);
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _hwnd, new SwapChainDescription1 { Width = (uint)Math.Max(1, rc.Right), Height = (uint)Math.Max(1, rc.Bottom), Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipDiscard });
        BindTarget();
        unsafe { fixed (byte* p = _px) _image = _ctx.CreateBitmap(new SizeI(_doc.Width, _doc.Height), (IntPtr)p, (uint)(_doc.Width * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); }
        _strokeWidth = EditDocument.DefaultWidth(_doc.Width, _doc.Height);
        BarNative.ShowWindow(_hwnd, 5); BarNative.SetForegroundWindow(_hwnd);
        Render();
    }

    private float _strokeWidth = 4;

    private void BindTarget()
    {
        _ctx!.Target = null; _target?.Dispose();
        using var surface = _swap!.GetBuffer<IDXGISurface>(0);
        _target = _ctx.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _ctx.Target = _target;
    }

    private float ToolbarH => 52 * _scale;
    private float StatusH => 26 * _scale;

    private EditView View()
    {
        BarNative.GetClientRect(_hwnd, out var rc);
        float pad = 16 * _scale;
        return EditView.Fit(_doc.Crop, pad, ToolbarH + pad, Math.Max(10, rc.Right - 2 * pad), Math.Max(10, rc.Bottom - ToolbarH - StatusH - 2 * pad));
    }

    private void Brush(EditColor c, float a = 1) => _b!.Color = new Color4(c.R, c.G, c.B, a);
    private void Brush(float r, float g, float b, float a) => _b!.Color = new Color4(r, g, b, a);

    private void Render()
    {
        if (_ctx is null || _swap is null || _disposed) return;
        try
        {
            BarNative.GetClientRect(_hwnd, out var rc);
            _ctx.BeginDraw(); _ctx.Clear(new Color4(0.12f, 0.12f, 0.13f, 1));
            var v = View(); var crop = _doc.Crop;
            _ctx.Transform = Matrix3x2.CreateScale(v.Scale) * Matrix3x2.CreateTranslation(v.OffsetX, v.OffsetY);
            _ctx.PushAxisAlignedClip(new RawRectF(crop.X, crop.Y, crop.Right, crop.Bottom), AntialiasMode.Aliased);
            DrawScene(preview: true);
            _ctx.PopAxisAlignedClip();
            if (_tool == EditTool.Crop && _from is { } f) { Brush(1, 1, 1, 0.95f); using var dash = _dev!.Factory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Dash }); _ctx.DrawRectangle(Norm(f.X, f.Y, _cur.X, _cur.Y), _b!, 1.5f / v.Scale, dash); }
            _ctx.Transform = Matrix3x2.Identity;
            DrawToolbar(rc.Right);
            DrawStatus(rc.Right, rc.Bottom);
            _ctx.EndDraw(); _swap.Present(1, PresentFlags.None);
        }
        catch (Exception e) { _log("[editor] dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>Image + annotations in IMAGE coordinates (the caller sets the transform). Shared by the screen and the export, so what you see is what is saved.</summary>
    private void DrawScene(bool preview)
    {
        _ctx!.DrawBitmap(_image!, new RawRectF(0, 0, _doc.Width, _doc.Height), 1, preview ? Vortice.Direct2D1.BitmapInterpolationMode.Linear : Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor, null);
        foreach (var a in _doc.Annotations) Draw(a);
        if (!preview) return;
        if (_stroke is { Count: > 1 }) Draw(new PenStroke(_color, _strokeWidth, _stroke));
        if (_from is { } f)
            switch (_tool)
            {
                case EditTool.Arrow: Draw(new ArrowShape(_color, _strokeWidth, f.X, f.Y, _cur.X, _cur.Y)); break;
                case EditTool.Rectangle: Draw(new RectShape(_color, _strokeWidth, f.X, f.Y, _cur.X, _cur.Y)); break;
                case EditTool.Highlight: Draw(new HighlightShape(_color, f.X, f.Y, _cur.X, _cur.Y)); break;
            }
        if (_textAt is { } t)
        {
            Draw(new TextShape(_color, TextSize(), t.X, t.Y, _text + "|"));
        }
    }

    private float TextSize() => Math.Clamp(Math.Max(_doc.Width, _doc.Height) / 45f, 16f, 64f);

    private static RawRectF Norm(float x1, float y1, float x2, float y2) => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    private void Draw(Annotation a)
    {
        switch (a)
        {
            case PenStroke p:
                Brush(p.Color);
                for (int i = 1; i < p.Points.Count; i++) _ctx!.DrawLine(new Vector2(p.Points[i - 1].X, p.Points[i - 1].Y), new Vector2(p.Points[i].X, p.Points[i].Y), _b!, p.Width, _round);
                break;
            case ArrowShape r:
                Brush(r.Color);
                _ctx!.DrawLine(new Vector2(r.X1, r.Y1), new Vector2(r.X2, r.Y2), _b!, r.Width, _round);
                var (ha, hb) = EditDocument.ArrowHead(r.X1, r.Y1, r.X2, r.Y2, r.Width * 4.5f);
                _ctx.DrawLine(new Vector2(r.X2, r.Y2), new Vector2(ha.X, ha.Y), _b!, r.Width, _round);
                _ctx.DrawLine(new Vector2(r.X2, r.Y2), new Vector2(hb.X, hb.Y), _b!, r.Width, _round);
                break;
            case RectShape r: Brush(r.Color); _ctx!.DrawRectangle(Norm(r.X1, r.Y1, r.X2, r.Y2), _b!, r.Width, _round); break;
            case HighlightShape h: Brush(h.Color, 0.38f); _ctx!.FillRectangle(Norm(h.X1, h.Y1, h.X2, h.Y2), _b!); break;
            case TextShape t:
                int size = (int)Math.Round(t.Size);
                if (!_textFormats.TryGetValue(size, out var fmt)) { fmt = _dw!.CreateTextFormat("Segoe UI Semibold", size); fmt.WordWrapping = WordWrapping.NoWrap; _textFormats[size] = fmt; }
                using (var l = _dw!.CreateTextLayout(t.Text, fmt, 20000, size * 2))
                {
                    Brush(0, 0, 0, 0.55f); _ctx!.DrawTextLayout(new Vector2(t.X + size / 14f, t.Y + size / 14f), l, _b!);   // a soft shadow keeps it readable on any background
                    Brush(t.Color); _ctx.DrawTextLayout(new Vector2(t.X, t.Y), l, _b!);
                }
                break;
        }
    }

    private static readonly (string Id, string Label)[] ToolButtons =
        [("crop", "Recortar"), ("pen", "Lápiz"), ("arrow", "Flecha"), ("rect", "Rectángulo"), ("text", "Texto"), ("highlight", "Resaltar")];
    private static readonly (string Id, string Label)[] FileButtons =
        [("undo", "Deshacer"), ("redo", "Rehacer"), ("copy", "Copiar"), ("save", "Guardar"), ("saveas", "Guardar como…")];

    private static EditTool ToolOf(string id) => id switch { "crop" => EditTool.Crop, "pen" => EditTool.Pen, "rect" => EditTool.Rectangle, "text" => EditTool.Text, "highlight" => EditTool.Highlight, _ => EditTool.Arrow };

    private void DrawToolbar(float width)
    {
        Brush(0.17f, 0.17f, 0.18f, 1); _ctx!.FillRectangle(new RawRectF(0, 0, width, ToolbarH), _b!);
        _buttons.Clear(); float x = 10 * _scale, h = 32 * _scale, y = (ToolbarH - h) / 2;
        foreach (var (id, label) in ToolButtons) x = Button(id, label, x, y, h, ToolOf(id) == _tool, enabled: true) + 4 * _scale;
        x += 10 * _scale;
        for (int i = 0; i < EditColor.Palette.Length; i++)
        {
            var c = EditColor.Palette[i]; float d = 20 * _scale, cy = ToolbarH / 2;
            var r = new RawRectF(x, cy - d / 2, x + d, cy + d / 2);
            Brush(c); _ctx.FillEllipse(new Ellipse(new Vector2(x + d / 2, cy), d / 2, d / 2), _b!);
            Brush(1, 1, 1, c == _color ? 1f : 0.25f); _ctx.DrawEllipse(new Ellipse(new Vector2(x + d / 2, cy), d / 2 + 2 * _scale, d / 2 + 2 * _scale), _b!, c == _color ? 2 * _scale : 1);
            _buttons.Add(("color" + i, r)); x += d + 10 * _scale;
        }

        float right = width - 10 * _scale;
        for (int i = FileButtons.Length - 1; i >= 0; i--)
        {
            var (id, label) = FileButtons[i];
            using var l = _dw!.CreateTextLayout(label, _ui!, 1000, h); float bw = l.Metrics.Width + 24 * _scale;
            bool en = id switch { "undo" => _doc.CanUndo, "redo" => _doc.CanRedo, _ => true };
            Button(id, label, right - bw, y, h, id == "save", en); right -= bw + 6 * _scale;
        }
    }

    private float Button(string id, string label, float x, float y, float h, bool accent, bool enabled)
    {
        using var l = _dw!.CreateTextLayout(label, _ui!, 1000, h); float w = l.Metrics.Width + 24 * _scale;
        var r = new RawRectF(x, y, x + w, y + h);
        if (accent) Brush(0.04f, 0.52f, 1f, enabled ? 1 : 0.4f); else Brush(1, 1, 1, enabled ? 0.10f : 0.04f);
        _ctx!.FillRoundedRectangle(new RoundedRectangle { Rect = r, RadiusX = 8 * _scale, RadiusY = 8 * _scale }, _b!);
        Brush(1, 1, 1, enabled ? 0.95f : 0.35f); _ctx.DrawTextLayout(new Vector2(x + 12 * _scale, y + (h - l.Metrics.Height) / 2), l, _b!);
        if (enabled) _buttons.Add((id, r));
        return x + w;
    }

    private void DrawStatus(float width, float height)
    {
        var crop = _doc.Crop;
        string tip = _tool switch
        {
            EditTool.Crop => "Arrastra para recortar", EditTool.Pen => "Arrastra para dibujar", EditTool.Arrow => "Arrastra para dibujar una flecha",
            EditTool.Rectangle => "Arrastra para dibujar un rectángulo", EditTool.Text => _textAt is null ? "Haz clic donde quieras escribir" : "Escribe · Intro para terminar · Esc para descartar",
            _ => "Arrastra para resaltar",
        };
        string text = $"{crop.W} × {crop.H} px  ·  {tip}  ·  Ctrl+Z deshacer · Ctrl+Y rehacer · Ctrl+C copiar · Ctrl+S guardar" + (_status is null ? "" : "  ·  " + _status);
        using var l = _dw!.CreateTextLayout(text, _ui!, width, StatusH);
        Brush(1, 1, 1, 0.6f); _ctx!.DrawTextLayout(new Vector2(12 * _scale, height - StatusH + (StatusH - l.Metrics.Height) / 2), l, _b!);
    }

    // ───────────────────────────── export (real resolution)
    /// <summary>The cropped image with its annotations, rendered with the same code as the screen, as BGRA (alpha 255).</summary>
    private (byte[] Px, int W, int H) Export()
    {
        var crop = _doc.Crop; var ctx = _ctx!;
        var props = new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target);
        using var target = ctx.CreateBitmap(new SizeI(crop.W, crop.H), IntPtr.Zero, 0, props);
        var old = ctx.Target; ctx.Target = target;
        ctx.BeginDraw(); ctx.Clear(new Color4(0, 0, 0, 1)); ctx.Transform = Matrix3x2.CreateTranslation(-crop.X, -crop.Y);
        DrawScene(preview: false);
        ctx.Transform = Matrix3x2.Identity; ctx.EndDraw(); ctx.Target = old;
        using var cpu = ctx.CreateBitmap(new SizeI(crop.W, crop.H), IntPtr.Zero, 0, new BitmapProperties1(props.PixelFormat, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read);
        try
        {
            var px = new byte[crop.W * crop.H * 4];
            for (int row = 0; row < crop.H; row++) Marshal.Copy(map.Bits + (int)(row * map.Pitch), px, row * crop.W * 4, crop.W * 4);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return (px, crop.W, crop.H);
        }
        finally { cpu.Unmap(); }
    }

    private void Copy()
    {
        CommitText(); var (px, w, h) = Export(); _copy(px, w, h); _status = "Copiada al portapapeles"; _log($"[editor] copiada ({w}x{h})"); Render();
    }

    private void Save(string? target)
    {
        CommitText();
        string path = target ?? EditDocument.EditedPath(_path, File.Exists);
        var (px, w, h) = Export();
        bool jpeg = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
        _status = "Guardando…"; Render();
        Task.Run(async () =>
        {
            try
            {
                if (target is not null && File.Exists(target)) File.Delete(target);   // the user chose to replace it in the Save As dialog
                await ScreenGrabber.SaveAsync(px, w, h, path, jpeg).ConfigureAwait(false);
                _log($"[editor] guardada: {System.IO.Path.GetFileName(path)} ({w}x{h}, {_doc.Annotations.Count} anotación(es))");
                _saved = true; _status = "Guardada como " + System.IO.Path.GetFileName(path);
            }
            catch (Exception e) { _status = "No se pudo guardar"; _log("[editor] no se pudo guardar: " + e.GetType().Name + ": " + e.Message); }
            if (!_disposed) BarNative.InvalidateRect(_hwnd, IntPtr.Zero, false);
        });
    }

    private void SaveAs()
    {
        CommitText();
        var suggested = System.IO.Path.GetFileName(EditDocument.EditedPath(_path, File.Exists));
        if (SaveDialog.Ask(_hwnd, System.IO.Path.GetDirectoryName(_path) ?? "", suggested) is { } chosen) Save(chosen);
    }

    // ───────────────────────────── input
    private (float X, float Y) ImagePoint(IntPtr l)
    {
        float vx = (short)((long)l & 0xFFFF), vy = (short)(((long)l >> 16) & 0xFFFF);
        var (ix, iy) = View().ToImage(vx, vy);
        var c = _doc.Crop; return (Math.Clamp(ix, c.X, c.Right), Math.Clamp(iy, c.Y, c.Bottom));
    }

    private void Click(string id)
    {
        if (id.StartsWith("color", StringComparison.Ordinal) && int.TryParse(id[5..], out int ci)) { _color = EditColor.Palette[ci]; Render(); return; }
        switch (id)
        {
            case "undo": CommitText(); _doc.Undo(); break;
            case "redo": CommitText(); _doc.Redo(); break;
            case "copy": Copy(); return;
            case "save": Save(null); return;
            case "saveas": SaveAs(); return;
            default:
                CommitText(); _tool = ToolOf(id);
                if (_tool == EditTool.Highlight && _color == EditColor.Red) _color = EditColor.Yellow;
                break;
        }

        Render();
    }

    private void CommitText()
    {
        if (_textAt is { } t) _doc.Add(new TextShape(_color, TextSize(), t.X, t.Y, _text));
        _textAt = null; _text = "";
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_SIZE when _swap is not null:
                    _ctx!.Target = null; _target?.Dispose(); _target = null;
                    _swap.ResizeBuffers(0, (uint)Math.Max(1, (long)l & 0xFFFF), (uint)Math.Max(1, ((long)l >> 16) & 0xFFFF), Format.Unknown, SwapChainFlags.None);
                    BindTarget(); Render(); return IntPtr.Zero;
                case WM_DPICHANGED: _scale = Math.Max(1, BarNative.GetDpiForWindow(_hwnd)) / 96f; _ui?.Dispose(); _ui = _dw!.CreateTextFormat("Segoe UI", 13 * _scale); break;
                case WM_PAINT: Render(); BarNative.ValidateRect(h, IntPtr.Zero); return IntPtr.Zero;
                case WM_LBUTTONDOWN:
                {
                    float vx = (short)((long)l & 0xFFFF), vy = (short)(((long)l >> 16) & 0xFFFF);
                    var hit = _buttons.FirstOrDefault(b => vx >= b.Rect.Left && vx < b.Rect.Right && vy >= b.Rect.Top && vy < b.Rect.Bottom);
                    if (hit.Id is not null) { Click(hit.Id); return IntPtr.Zero; }
                    if (vy < ToolbarH) return IntPtr.Zero;
                    var p = ImagePoint(l); _cur = p;
                    if (_tool == EditTool.Text) { CommitText(); _textAt = p; Render(); return IntPtr.Zero; }
                    CommitText();
                    _from = p; if (_tool == EditTool.Pen) _stroke = [p];
                    BarNative.SetCapture(_hwnd); return IntPtr.Zero;
                }
                case WM_MOUSEMOVE when _from is not null:
                    _cur = ImagePoint(l); _stroke?.Add(_cur); Render(); return IntPtr.Zero;
                case WM_LBUTTONUP when _from is { } f:
                {
                    BarNative.ReleaseCapture(); _cur = ImagePoint(l);
                    bool moved = Math.Abs(_cur.X - f.X) >= 2 || Math.Abs(_cur.Y - f.Y) >= 2;
                    switch (_tool)
                    {
                        case EditTool.Pen when _stroke is { Count: > 1 }: _doc.Add(new PenStroke(_color, _strokeWidth, _stroke)); break;
                        case EditTool.Arrow when moved: _doc.Add(new ArrowShape(_color, _strokeWidth, f.X, f.Y, _cur.X, _cur.Y)); break;
                        case EditTool.Rectangle when moved: _doc.Add(new RectShape(_color, _strokeWidth, f.X, f.Y, _cur.X, _cur.Y)); break;
                        case EditTool.Highlight when moved: _doc.Add(new HighlightShape(_color, f.X, f.Y, _cur.X, _cur.Y)); break;
                        case EditTool.Crop when moved:
                            var r = Norm(f.X, f.Y, _cur.X, _cur.Y);
                            if (_doc.SetCrop(new CaptureRect((int)r.Left, (int)r.Top, (int)(r.Right - r.Left), (int)(r.Bottom - r.Top)))) _log($"[editor] recorte {_doc.Crop.W}x{_doc.Crop.H}");
                            break;
                    }

                    _from = null; _stroke = null; Render(); return IntPtr.Zero;
                }
                case WM_CHAR when _textAt is not null:
                {
                    char ch = (char)(long)w;
                    if (ch == '\b') { if (_text.Length > 0) _text = _text[..^1]; }
                    else if (ch == '\r') CommitText();
                    else if (ch == 27) { _textAt = null; _text = ""; }
                    else if (!char.IsControl(ch) && _text.Length < 300) _text += ch;
                    Render(); return IntPtr.Zero;
                }
                case WM_KEYDOWN:
                {
                    bool ctrl = BarNative.GetKeyState(0x11) < 0, shift = BarNative.GetKeyState(0x10) < 0; int vk = (int)w;
                    if (_textAt is not null && !ctrl) break;   // typing: WM_CHAR handles it
                    if (ctrl && vk == 0x5A) { Click(shift ? "redo" : "undo"); return IntPtr.Zero; }
                    if (ctrl && vk == 0x59) { Click("redo"); return IntPtr.Zero; }
                    if (ctrl && vk == 0x43) { Click("copy"); return IntPtr.Zero; }
                    if (ctrl && vk == 0x53) { Click(shift ? "saveas" : "save"); return IntPtr.Zero; }
                    if (vk == 0x1B) { BarNative.PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return IntPtr.Zero; }
                    break;
                }
                case WM_CLOSE:
                    CommitText();
                    if (_doc.IsDirty && !_saved && BarNative.MessageBoxW(h, "Hay cambios sin guardar. ¿Cerrar el editor sin guardarlos?\n\nLa captura original no se ha modificado.", "Editar captura", 0x4 | 0x30) != 6) return IntPtr.Zero;
                    Dispose(); _closed(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[editor] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var f in _textFormats.Values) f.Dispose(); _textFormats.Clear();
        _image?.Dispose(); if (_ctx is not null) _ctx.Target = null; _target?.Dispose(); _swap?.Dispose(); _round?.Dispose(); _ui?.Dispose(); _dw?.Dispose(); _b?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); InstanceWindows.Forget(_hwnd); _hwnd = IntPtr.Zero; }
    }
}

/// <summary>The classic Save As dialog (comdlg32, documented) restricted to PNG/JPEG; asks before replacing an existing file.</summary>
internal static class SaveDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize; public IntPtr hwndOwner, hInstance; public string lpstrFilter; public IntPtr lpstrCustomFilter; public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile; public int nMaxFile; public IntPtr lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir, lpstrTitle; public int Flags; public short nFileOffset, nFileExtension;
        public string lpstrDefExt; public IntPtr lCustData, lpfnHook, lpTemplateName, pvReserved; public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")] private static extern bool GetSaveFileName(ref OPENFILENAME o);

    public static string? Ask(IntPtr owner, string folder, string suggested)
    {
        const int max = 1024; var buf = Marshal.AllocHGlobal(max * 2);
        try
        {
            var chars = (suggested + "\0").ToCharArray(); Marshal.Copy(chars, 0, buf, Math.Min(chars.Length, max));
            var o = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(), hwndOwner = owner, lpstrFilter = "Imagen PNG (*.png)\0*.png\0Imagen JPEG (*.jpg)\0*.jpg\0\0", nFilterIndex = 1,
                lpstrFile = buf, nMaxFile = max, lpstrInitialDir = folder, lpstrTitle = "Guardar captura editada como", lpstrDefExt = "png",
                Flags = 0x2 /* OFN_OVERWRITEPROMPT */ | 0x800 /* OFN_PATHMUSTEXIST */ | 0x80000 /* OFN_EXPLORER */ | 0x8 /* OFN_NOCHANGEDIR */,
            };
            return GetSaveFileName(ref o) ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
