using System.Text.Json;
using WinMac.Bar;

namespace WinMac.Host;

/// <summary>
/// Dueño ÚNICO de <c>winmac.json</c> en ejecución: todo cambio de configuración (menú de la bandeja, ventana de ajustes, petición de un módulo) pasa por aquí, se valida,
/// se guarda de forma atómica y se notifica. Si no se puede guardar (disco lleno, archivo bloqueado) el cambio sigue vigente en memoria y se registra.
/// </summary>
public sealed class ConfigService
{
    private readonly string _path; private readonly Action<string> _log; private readonly object _l = new(); private HostConfig _cfg;
    public ConfigService(string path, HostConfig initial, Action<string>? log = null) { _path = path; _cfg = initial; _log = log ?? (_ => { }); }

    public HostConfig Current { get { lock (_l) return _cfg; } }
    public NotchSettings Notch => Current.Notch ?? new NotchSettings();
    public bool IsEnabled(string module) => Current.Modules.TryGetValue(module, out var m) ? m.Enabled : true;
    /// <summary>Se dispara tras cada cambio efectivo (anterior, nuevo), fuera del cerrojo.</summary>
    public event Action<HostConfig, HostConfig>? Changed;

    public bool Update(Func<HostConfig, HostConfig> change)
    {
        HostConfig old, @new;
        lock (_l)
        {
            old = _cfg; @new = change(old); if (Equals(old, @new) && SameModules(old, @new)) return false;
            _cfg = @new;
            try { HostConfig.Save(_path, @new); } catch (Exception e) { _log("[config] no se pudo guardar winmac.json: " + e.Message + " (el cambio sigue vigente en memoria)"); }
        }
        try { Changed?.Invoke(old, @new); } catch (Exception e) { _log("[config] un suscriptor falló: " + e.Message); }
        return true;
    }
    private static bool SameModules(HostConfig a, HostConfig b) => a.Modules.Count == b.Modules.Count && a.Modules.All(kv => b.Modules.TryGetValue(kv.Key, out var m) && Equals(m, kv.Value));

    public bool UpdateNotch(Func<NotchSettings, NotchSettings> change) => Update(c =>
    {
        var cur = c.Notch ?? new NotchSettings(); var n = change(cur).Sanitize();
        return SameNotch(cur, n) && c.Notch is not null ? c : c with { Notch = n };
    });
    /// <summary>Comparación por valor (los records comparan la lista de pestañas por referencia).</summary>
    public static bool SameNotch(NotchSettings a, NotchSettings b) => a with { Sections = [] } == b with { Sections = [] } && a.Sections.SequenceEqual(b.Sections);

    public bool ApplyNotchPatch(JsonElement patch) => UpdateNotch(n => n.ApplyPatch(patch));

    public BarConfig Bar => Current.Bar ?? new BarConfig();
    public bool UpdateBar(Func<BarConfig, BarConfig> change) => Update(c =>
    {
        var cur = c.Bar ?? new BarConfig(); var n = change(cur).Sanitize();
        return n == cur ? c : c with { Bar = n };   // sin cambio efectivo no se crea la sección ni se escribe nada (con la sección ausente rigen los valores por defecto)
    });
    public bool ApplyBarPatch(JsonElement patch) => UpdateBar(b => b.ApplyPatch(patch));

    public bool SetModuleEnabled(string module, bool enabled) => Update(c =>
    {
        if (!c.Modules.TryGetValue(module, out var m)) return c;
        if (m.Enabled == enabled) return c;
        return c with { Modules = new Dictionary<string, ModuleConfig>(c.Modules) { [module] = m with { Enabled = enabled } } };
    });

    public bool SetAutostartFlag(bool on) => Update(c => c.Autostart == on ? c : c with { Autostart = on });
}

/// <summary>
/// Une la configuración central con el resto del host: publica los ajustes de la Isla (tópico retenido, el módulo los recibe al conectar y en cada cambio), aplica los parches que el
/// módulo pide, y activa o desactiva módulos EN VIVO cuando cambia su interruptor. El módulo nunca escribe <c>winmac.json</c>.
/// </summary>
public sealed class HostController
{
    private readonly ConfigService _cfg; private readonly Supervisor _sup; private readonly IHostBus _bus; private readonly Action<string> _log;
    public HostController(ConfigService cfg, Supervisor sup, IHostBus bus, Action<string>? log = null)
    {
        _cfg = cfg; _sup = sup; _bus = bus; _log = log ?? (_ => { });
        _bus.Event += OnEvent; _cfg.Changed += OnChanged; _bus.Disconnected += OnDisconnected; _sup.StateChanged += OnModuleState;
    }

    /// <summary>Si la Isla se va (cierre, caída, desactivación), el espacio que tenía reservado en la Barra no puede quedarse retenido: se publica vacío.</summary>
    private void OnDisconnected(string module) { if (module == "notch") ReleaseNotchSpace(); }

    /// <summary>Segunda vía, por estado del supervisor: aunque el canal no avisara del corte, una Isla caída, fallida o parada no puede dejar espacio retenido en las Barras.</summary>
    private void OnModuleState(string module, ModuleState state)
    {
        if (module == "notch" && state is ModuleState.Crashed or ModuleState.Failed or ModuleState.Stopped or ModuleState.Stopping or ModuleState.Disabled or ModuleState.NotInstalled) ReleaseNotchSpace();
    }

    private void ReleaseNotchSpace() => _bus.Publish(NotchSettings.RectsTopic, new { rects = Array.Empty<object>() });

    /// <summary>Publica el estado inicial (el módulo lo recibe al suscribirse, aunque se conecte más tarde).</summary>
    public const string BarTopic = "config.bar", BarSetTopic = "config.bar.set", CommandTopic = "host.cmd";
    /// <summary>Secciones de Ajustes que se pueden pedir desde el panel de la Barra (cualquier otra se ignora).</summary>
    public static readonly string[] SettingsSections = ["sound", "system", "network", "disks", "weather"];
    /// <summary>Cierre ordenado de WinMac completo (lo ejecuta el anfitrión en su hilo de interfaz).</summary>
    public Action? QuitRequested { get; set; }
    /// <summary>Abrir la ventana de Ajustes, en una sección o null.</summary>
    public Action<string?>? SettingsRequested { get; set; }

    public void Start() { _bus.Publish(NotchSettings.Topic, _cfg.Notch.ToWire()); _bus.Publish(BarTopic, BarWire(_cfg.Bar)); }

    /// <summary>La configuración de la Barra tal como viaja (camelCase, enums como texto).</summary>
    private static object BarWire(BarConfig b) => JsonDocument.Parse(b.ToWireJson()).RootElement.Clone();

    /// <summary>Pide al módulo que abra su ventana de rutas y datos locales (lo único que no es central).</summary>
    public void OpenNotchLocalSettings() => _bus.Emit(NotchSettings.CommandTopic, new { cmd = "open-local-settings" });

    private void OnEvent(string module, string topic, JsonElement? data)
    {
        if (data is not { } d) return;
        // cada módulo cambia SOLO sus propios ajustes y solo por su tópico
        if (module == "notch" && topic == NotchSettings.SetTopic) { try { _cfg.ApplyNotchPatch(d); } catch (Exception e) { _log("[config] parche de la Isla descartado: " + e.Message); } }
        else if (module == "bar" && topic == CommandTopic) { try { RunCommand(d); } catch (Exception e) { _log("[host] orden de la Barra descartada: " + e.Message); } }
        else if (module == "bar" && topic == BarSetTopic) { try { _cfg.ApplyBarPatch(d); } catch (Exception e) { _log("[config] parche de la Barra descartado: " + e.Message); } }
    }

    /// <summary>Órdenes de la Barra (solo «quit» y «settings»; nada más). Solo las acepta de la Barra: el canal no es una puerta para otros módulos.</summary>
    private void RunCommand(JsonElement d)
    {
        string? cmd = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        string? section = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("section", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        switch (cmd)
        {
            case "quit": _log("[host] la Barra pidió cerrar WinMac"); QuitRequested?.Invoke(); break;
            case "settings": SettingsRequested?.Invoke(section is not null && SettingsSections.Contains(section) ? section : null); break;
        }
    }

    private void OnChanged(HostConfig old, HostConfig @new)
    {
        if (!ConfigService.SameNotch(old.Notch ?? new NotchSettings(), @new.Notch ?? new NotchSettings()) || old.Notch is null != @new.Notch is null) _bus.Publish(NotchSettings.Topic, (@new.Notch ?? new NotchSettings()).ToWire());
        if ((old.Bar ?? new BarConfig()) != (@new.Bar ?? new BarConfig()) || old.Bar is null != @new.Bar is null) _bus.Publish(BarTopic, BarWire(@new.Bar ?? new BarConfig()));
        foreach (var (id, m) in @new.Modules)
            if (!old.Modules.TryGetValue(id, out var o) || o.Enabled != m.Enabled)
            {
                var (mid, en) = (id, m.Enabled);
                _ = Task.Run(async () => { try { await _sup.SetEnabledAsync(mid, en).ConfigureAwait(false); } catch (Exception e) { _log($"[config] no se pudo {(en ? "activar" : "desactivar")} {mid}: {e.Message}"); } });
            }
    }
}
