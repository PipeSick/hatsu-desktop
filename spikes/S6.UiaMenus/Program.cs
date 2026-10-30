// S6 — ¿Cuánto del menú de una app ajena se puede leer con UI Automation?
// Uso: S6.UiaMenus [segundos_de_espera=5]
// Pasos: ejecútalo, pon en primer plano la app a probar antes de que acabe la cuenta atrás, y se anexa una línea a
// bench-results/S6-uia.jsonl. Repite con 10+ apps (Notepad, Explorer, Edge, Chrome, VS Code, Firefox, Word, 7-Zip, OBS, Steam…).
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

int wait = args.Length > 0 && int.TryParse(args[0], out var w) ? w : 5;
for (int i = wait; i > 0; i--) { Console.WriteLine($"Pon la app en primer plano… {i}"); Thread.Sleep(1000); }

IntPtr hwnd = GetForegroundWindow();
GetWindowThreadProcessId(hwnd, out uint pid);
string proc = Process.GetProcessById((int)pid).ProcessName;
var sw = Stopwatch.StartNew();
string? error = null; string[] top = []; int children = 0; string source = "none";
try
{
    var root = AutomationElement.FromHandle(hwnd);
    var bar = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));
    if (bar != null)
    {
        source = "MenuBar";
        var items = bar.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        top = items.Cast<AutomationElement>().Select(e => e.Current.Name).ToArray();
        // Expandir el primer menú para medir si los submenús son legibles sin abrirlos visualmente.
        if (items.Count > 0 && items[0].TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p))
        {
            ((ExpandCollapsePattern)p).Expand();
            Thread.Sleep(150);
            children = items[0].FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)).Count;
            ((ExpandCollapsePattern)p).Collapse();
        }
    }
}
catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }

var row = new { When = DateTime.Now, Process = proc, Found = top.Length > 0, Source = source, TopLevelItems = top, FirstMenuChildren = children, Ms = sw.Elapsed.TotalMilliseconds, Error = error };
Directory.CreateDirectory("bench-results");
File.AppendAllText("bench-results/S6-uia.jsonl", JsonSerializer.Serialize(row) + "\n");
Console.WriteLine(JsonSerializer.Serialize(row, new JsonSerializerOptions { WriteIndented = true }));

[DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
