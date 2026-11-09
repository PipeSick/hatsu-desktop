using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Launcher;
using WinMac.SystemControl;

namespace WinMac.Bar.Win.Rendering;

/// <summary>Registro central de acciones de Spotlight (Spotlight Actions): las acciones que ya existían se registran aquí sin cambiar su comportamiento y los módulos añaden las suyas.</summary>
public sealed partial class BarHost
{
    private readonly ActionRegistry _actionRegistry = new();
    internal ActionRegistry Actions => _actionRegistry;
    /// <summary>true mientras el módulo «Spotlight Actions» está activo: Spotlight busca y ejecuta con el registro; apagado, vuelve a su lista clásica (nada se pierde).</summary>
    internal bool UseActionRegistry { get; set; }

    /// <summary>Acciones propias de Hatsu (las 13 de siempre) + acciones del sistema. Devuelve los ids para retirarlos al apagar el módulo.</summary>
    internal List<string> RegisterCoreActions()
    {
        var runs = new Dictionary<string, Action>
        {
            ["winmac:capture:region"] = () => BeginCapture(CaptureMode.Region, "Spotlight"), ["winmac:capture:region:5"] = () => BeginCapture(CaptureMode.Region, "Spotlight", 5),
            ["winmac:capture:screen:5"] = () => BeginCapture(CaptureMode.Monitor, "Spotlight", 5), ["winmac:capture:window"] = () => BeginCapture(CaptureMode.Window, "Spotlight"),
            ["winmac:capture:screen"] = () => BeginCapture(CaptureMode.Monitor, "Spotlight"), ["winmac:capture:all"] = () => BeginCapture(CaptureMode.AllMonitors, "Spotlight"),
            ["winmac:record"] = () => ToggleRecording(CaptureMode.Region, "Spotlight"), ["winmac:record:stop"] = () => StopRecording("Spotlight"),
            ["winmac:launchpad"] = () => ToggleLaunchpad("Spotlight"), ["winmac:settings"] = () => { if (HostCommand is { } hc) hc("settings", null); },
            ["winmac:ws:save"] = () => SaveWorkspace(null), ["winmac:ws:side"] = () => ArrangeWindows(grid: false), ["winmac:ws:grid"] = () => ArrangeWindows(grid: true),
        };
        var ids = new List<string>();
        foreach (var (title, target, kw) in SpotPro.Actions)
        {
            if (!runs.TryGetValue(target, out var run)) continue;
            Func<bool>? avail = target switch { "winmac:record:stop" => () => IsRecording, "winmac:record" => () => !IsRecording, _ => null };
            if (_actionRegistry.Register(new SpotAction(target, title, "Acción de Hatsu Desktop", kw, null, ActionRisk.Normal, [], avail, () => { run(); return ActionResult.Success; }))) ids.Add(target);
        }

        // Shelf+ vive en la Isla (otro proceso): la acción solo se ofrece si la pestaña «Estantería» está activa
        if (_actionRegistry.Register(new SpotAction("winmac:shelf:open", "Estantería: abrir en la Isla", "Abre la estantería de la Isla (Shelf+)", "estanteria shelf bandeja archivos temporal isla", "shelf", ActionRisk.Normal, [], () => _notchOpts.Shelf,
                () => { ModuleEvent?.Invoke("notch.cmd", new { cmd = "shelf-open" }); return ActionResult.Success; }))) ids.Add("winmac:shelf:open");

        void Sys(string id, string title, string kw, SystemActionKind k, ActionRisk risk)
        {
            if (_actionRegistry.Register(new SpotAction(id, title, "Acción del sistema" + (risk == ActionRisk.Confirm ? " · pide un segundo Intro" : ""), kw, null, risk, [], null, () => { RunSystemAction(k, null); return ActionResult.Success; }))) ids.Add(id);
        }

        Sys("winmac:sys:lock", "Bloquear la pantalla", "bloquear pantalla lock sesion", SystemActionKind.Lock, ActionRisk.Normal);
        Sys("winmac:sys:sleep", "Suspender el equipo", "suspender dormir sleep", SystemActionKind.Sleep, ActionRisk.Confirm);
        Sys("winmac:sys:logoff", "Cerrar sesión", "cerrar sesion logoff salir", SystemActionKind.LogOff, ActionRisk.Confirm);
        Sys("winmac:sys:restart", "Reiniciar el equipo", "reiniciar restart", SystemActionKind.Restart, ActionRisk.Confirm);
        Sys("winmac:sys:shutdown", "Apagar el equipo", "apagar shutdown", SystemActionKind.Shutdown, ActionRisk.Confirm);
        return ids;
    }

    /// <summary>Un módulo registra una acción de Spotlight; se retira sola cuando el módulo se apaga.</summary>
    internal void RegisterModuleAction(FeatureScope scope, SpotAction a) { if (_actionRegistry.Register(a)) scope.Add(() => _actionRegistry.Unregister(a.Id)); }

    internal void UnregisterActions(IEnumerable<string> ids) { foreach (var id in ids) _actionRegistry.Unregister(id); }
}
