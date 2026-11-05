using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMac.Connect;

namespace WinMac.Host.App;

/// <summary>
/// Diagnóstico de Bluetooth y llamadas desde Ajustes (docs/58). Lanza <c>HatsuDesktop.Connect.exe --probe-calls --out …</c> (de la propia instalación, sin terminal), espera con tiempo límite,
/// lee el informe y lo ofrece para abrir, copiar o guardar. SOLO LECTURA: no cambia conexiones Bluetooth ni envía nada. El informe ya sale anonimizado del sondeo; aquí se vuelve a pasar
/// por <see cref="DiagRedactor"/> antes de copiarlo o guardarlo (defensa en profundidad).
/// </summary>
internal sealed class ConnectDiagnosticsService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly string _exeDir; private readonly Action<string> _log; private readonly Action<string, object?> _emit; private readonly Action _refresh;
    private readonly object _gate = new(); private bool _running, _observing; private string _status = "Sin ejecutar"; private string? _report; private IReadOnlyList<string> _summary = [];
    private Timer? _obsTimer; private DateTime _obsUntil;
    private string ReportPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "connect-probe-calls.txt");

    public ConnectDiagnosticsService(string exeDir, Action<string> log, Action<string, object?> emit, Action refresh) { _exeDir = exeDir; _log = log; _emit = emit; _refresh = refresh; }

    public ConnectDiagView View()
    {
        lock (_gate) return new(_status, _summary, _running, _report is not null, _observing,
            _observing ? $"Activa hasta las {_obsUntil:HH:mm}" : "Detenida");
    }

    public void Run()
    {
        lock (_gate) { if (_running) return; _running = true; _status = "Ejecutando…"; _summary = []; }
        _refresh();
        _ = Task.Run(() =>
        {
            var (status, report, summary) = Execute();
            _log("[connect] diagnóstico de telefonía: " + status);
            lock (_gate) { _running = false; _status = status; _report = report; _summary = summary; }
            _refresh();
        });
    }

    private (string Status, string? Report, IReadOnlyList<string> Summary) Execute()
    {
        try
        {
            var exe = Path.Combine(_exeDir, "HatsuDesktop.Connect.exe");
            if (!File.Exists(exe)) return ("No se encontró HatsuDesktop.Connect.exe en esta instalación", null, []);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
            try { File.Delete(ReportPath); } catch (Exception) { }
            using var p = Process.Start(new ProcessStartInfo(exe) { Arguments = "--probe-calls --out \"" + ReportPath + "\"", UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _exeDir })!;
            if (!p.WaitForExit((int)Timeout.TotalMilliseconds)) { try { p.Kill(true); } catch (Exception) { } return ("Error de ejecución: el diagnóstico no terminó en 60 s (se canceló)", null, []); }
            if (!File.Exists(ReportPath)) return ($"Error de ejecución: no se generó el informe (código {p.ExitCode})", null, []);
            var report = DiagRedactor.Redact(File.ReadAllText(ReportPath), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);
            return ((p.ExitCode == 0 ? "Terminado " : "Terminado con errores ") + DateTime.Now.ToString("HH:mm"), report, DiagReport.SummaryOf(report));
        }
        catch (Exception e) { return ("Error de ejecución: " + e.GetType().Name, null, []); }
    }

    private string? Report() { lock (_gate) return _report; }

    /// <summary>Abre una copia temporal en el Bloc de notas (el informe real vive en la carpeta de datos de la app, que el MSIX virtualiza: otra app podría no verla).</summary>
    public void Open()
    {
        if (Report() is not { } r) return;
        try
        {
            var f = Path.Combine(Path.GetTempPath(), "HatsuDesktop-diagnostico-llamadas.txt"); File.WriteAllText(f, r);
            using var _ = Process.Start(new ProcessStartInfo("notepad.exe", "\"" + f + "\"") { UseShellExecute = false });
        }
        catch (Exception e) { _log("[connect] no se pudo abrir el informe: " + e.GetType().Name + " (usa «Guardar una copia…»)"); lock (_gate) _status = "No se pudo abrir el informe; usa «Guardar una copia…»"; _refresh(); }
    }

    public void Copy()
    {
        if (Report() is not { } r) return;
        bool ok = Clipboard.SetText(r);
        lock (_gate) _status = ok ? "Informe copiado al portapapeles" : "No se pudo copiar (otra app usa el portapapeles); inténtalo de nuevo";
        _refresh();
    }

    public void Save()
    {
        if (Report() is not { } r) return;
        _ = Task.Run(() =>
        {
            try
            {
                var path = Clipboard.AskSavePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"HatsuDesktop-diagnostico-{DateTime.Now:yyyyMMdd-HHmm}.txt");
                if (path is null) return;
                File.WriteAllText(path, r);
                lock (_gate) _status = "Copia guardada (" + Path.GetFileName(path) + ")";
            }
            catch (Exception e) { lock (_gate) _status = "No se pudo guardar: " + e.GetType().Name; }
            _refresh();
        });
    }

    /// <summary>Interruptor TEMPORAL del observador de telefonía: orden puntual al módulo Connect (no se guarda en la configuración ni toca variables de entorno). Caduca solo.</summary>
    public void Observe(bool on)
    {
        lock (_gate)
        {
            _obsTimer?.Dispose(); _obsTimer = null; _observing = on;
            if (on) { _obsUntil = DateTime.Now.AddMinutes(SystemMinutes); _obsTimer = new Timer(_ => Observe(false), null, TimeSpan.FromMinutes(SystemMinutes), System.Threading.Timeout.InfiniteTimeSpan); }
        }
        try { _emit(ConnectTopics.Diag, new { observe = on }); } catch (Exception e) { _log("[connect] no se pudo avisar al módulo: " + e.GetType().Name); }
        _log("[connect] observación de telefonía del sistema pedida desde Ajustes: " + (on ? "activar" : "detener"));
        _refresh();
    }

    /// <summary>Al cerrar Hatsu: se detiene la observación (el módulo también la apaga solo).</summary>
    public void Shutdown() { try { if (_observing) _emit(ConnectTopics.Diag, new { observe = false }); } catch (Exception) { } _obsTimer?.Dispose(); }

    private const int SystemMinutes = 15;
}

/// <summary>Portapapeles de texto y diálogo «Guardar como» (API de Windows documentadas).</summary>
internal static class Clipboard
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);

    public static bool SetText(string text)
    {
        for (int i = 0; i < 5; i++)   // el portapapeles puede estar ocupado un instante
        {
            if (!OpenClipboard(IntPtr.Zero)) { Thread.Sleep(40); continue; }
            try
            {
                EmptyClipboard();
                var h = GlobalAlloc(0x0002, (UIntPtr)((text.Length + 1) * 2)); if (h == IntPtr.Zero) return false;
                var p = GlobalLock(h); Marshal.Copy(text.ToCharArray(), 0, p, text.Length); Marshal.WriteInt16(p, text.Length * 2, 0); GlobalUnlock(h);
                if (SetClipboardData(13, h) == IntPtr.Zero) { GlobalFree(h); return false; }
                return true;
            }
            finally { CloseClipboard(); }
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize; public IntPtr hwndOwner, hInstance; public string lpstrFilter; public IntPtr lpstrCustomFilter; public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile; public int nMaxFile; public IntPtr lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir, lpstrTitle; public int Flags; public short nFileOffset, nFileExtension;
        public string lpstrDefExt; public IntPtr lCustData, lpfnHook, lpTemplateName, pvReserved; public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")] private static extern bool GetSaveFileName(ref OPENFILENAME o);

    public static string? AskSavePath(string folder, string suggested)
    {
        const int max = 1024; var buf = Marshal.AllocHGlobal(max * 2);
        try
        {
            var chars = (suggested + "\0").ToCharArray(); Marshal.Copy(chars, 0, buf, Math.Min(chars.Length, max));
            var o = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(), lpstrFilter = "Texto (*.txt)\0*.txt\0\0", nFilterIndex = 1, lpstrFile = buf, nMaxFile = max, lpstrInitialDir = folder,
                lpstrTitle = "Guardar el informe de diagnóstico", lpstrDefExt = "txt", Flags = 0x2 | 0x800 | 0x80000 | 0x8,
            };
            return GetSaveFileName(ref o) ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
