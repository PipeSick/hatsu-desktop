using WinMac.Bar.Features;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Launcher;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Une el registro central (<see cref="HotkeyRegistry"/>: dueños y conflictos) con <c>RegisterHotKey</c> (<see cref="GlobalHotkey"/>, sin ganchos). Pide permiso al registro primero; si Windows lo rechaza,
/// el atajo queda «no aceptado» y se informa en Ajustes. Todo lo que se registra se libera con el <see cref="FeatureScope"/> del módulo. Seguro desde el hilo de la barra.
/// </summary>
internal sealed class HotkeyBinder
{
    private sealed class Bound(GlobalHotkey? key, string purpose, string owner, string module) { public GlobalHotkey? Key = key; public readonly string Purpose = purpose, Owner = owner, Module = module; public string Spec = ""; public bool Registered; public string? Problem; }
    private readonly HotkeyRegistry _reg; private readonly Action<Action> _post; private readonly Action<string> _log; private readonly Dictionary<(string, string), Bound> _bound = [];
    public HotkeyBinder(HotkeyRegistry reg, Action<Action> postToUi, Action<string> log) { _reg = reg; _post = postToUi; _log = log; }
    public Action? Changed { get; set; }
    public HotkeyRegistry Registry => _reg;

    /// <summary>Registra (o reasigna) un atajo. Spec vacío = sin atajo (se libera el anterior y no es un error).</summary>
    public ClaimResult Bind(FeatureScope? scope, string owner, string id, string purpose, string module, string? spec, Action run)
    {
        var k = (owner, id);
        // Misma combinación y ya registrada: no se toca (cada cambio de configuración volvía a registrar todos los atajos y a esperar a sus hilos)
        if (!string.IsNullOrWhiteSpace(spec) && HotkeySpec.TryParse(spec, out var same) && _bound.TryGetValue(k, out var cur) && cur.Registered && cur.Spec == same.Format()) return new ClaimResult(ClaimStatus.Ok);
        Release(owner, id);
        if (string.IsNullOrWhiteSpace(spec)) { Changed?.Invoke(); return new ClaimResult(ClaimStatus.Ok); }
        var r = _reg.TryClaim(owner, id, purpose, spec);
        var b = new Bound(null, purpose, owner, module) { Spec = spec! };
        if (!r.Ok) { b.Registered = false; b.Problem = r.Explain(); _bound[k] = b; _log($"[atajos] {purpose}: {spec} no se registró ({b.Problem})"); Changed?.Invoke(); return r; }
        HotkeySpec.TryParse(spec, out var hs); b.Spec = hs.Format();
        uint mods = (hs.Mods & HotkeySpec.Alt) != 0 ? GlobalHotkey.ModAlt : 0; if ((hs.Mods & HotkeySpec.Ctrl) != 0) mods |= GlobalHotkey.ModCtrl; if ((hs.Mods & HotkeySpec.Shift) != 0) mods |= GlobalHotkey.ModShift;
        var key = new GlobalHotkey(mods, hs.Vk, () => _post(run), _log, $"{hs.Display()} ({purpose})");
        b.Key = key; b.Registered = key.Registered;
        if (!key.Registered) { b.Problem = "Windows no lo aceptó: otro programa ya lo tiene registrado"; _reg.SetOsResult(owner, id, false); }
        _bound[k] = b;
        scope?.Add(() => Release(owner, id));
        Changed?.Invoke();
        return r;
    }

    public void Release(string owner, string id)
    {
        if (_bound.Remove((owner, id), out var b)) b.Key?.Dispose();
        _reg.Release(owner, id);
    }

    /// <summary>Los atajos de módulos para el informe a Ajustes (se suman a los de Spotlight y capturas).</summary>
    public IReadOnlyList<HotkeyInfo> Infos() => _bound.Values.Select(b => new HotkeyInfo(HotkeySpec.TryParse(b.Spec, out var s) ? s.Display() : b.Spec, b.Purpose, b.Module, b.Registered, b.Problem)).ToList();

    /// <summary>Estado de un atajo concreto: null = sin atajo asignado.</summary>
    public (bool Registered, string? Problem)? StateOf(string owner, string id) => _bound.TryGetValue((owner, id), out var b) ? (b.Registered, b.Problem) : null;
}
