namespace WinMac.Bar.Peripherals;

public enum BatteryStatus
{
    /// <summary>Conectado y leído hace poco: el porcentaje es actual.</summary>
    Current,
    /// <summary>Conectado pero la última lectura es antigua: se muestra como tal, nunca como actual.</summary>
    Stale,
    /// <summary>Conectado, sin porcentaje válido (no se sabe).</summary>
    Unknown,
    /// <summary>El dispositivo/proveedor no ofrece batería.</summary>
    Unsupported,
    /// <summary>Desconectado: solo se conserva la última lectura (si la hubo).</summary>
    Disconnected,
}

/// <summary>Un dispositivo tal como lo muestra la interfaz.</summary>
public sealed record DeviceView(string Id, string Name, DeviceKind Kind, ConnectionKind Connection, bool Connected, BatteryStatus Battery, int? Percent, DateTime? ReadAtUtc, ChargeState Charge,
    string? Note, bool Hidden, bool ProviderTrouble)
{
    /// <summary>«Cargando» solo si el dispositivo lo informa AHORA (conectado, lectura actual); si no, null.</summary>
    public string? ChargeText => Battery == BatteryStatus.Current && Charge == ChargeState.Charging ? "Cargando" : null;
}

/// <summary>
/// Estado normalizado de los periféricos (en memoria; nada se guarda en disco). Separa adquisición (proveedores) de presentación (<see cref="DeviceView"/>).
/// Reglas de precisión: un porcentaje solo vale 0–100; la ausencia de dato NUNCA es 100 %; una lectura vieja o de un dispositivo desconectado se marca como tal; un proveedor que falla
/// no borra lo que ya se sabía (pero lo avisa); «cargando» solo si el dispositivo lo informa en una lectura actual.
/// </summary>
public sealed class PeripheralStore
{
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public string Id = "", Name = "", ProviderId = "";
        public DeviceKind Kind; public ConnectionKind Connection; public bool Connected; public int? Percent; public DateTime? ReadAtUtc; public ChargeState Charge; public BatteryCapability Capability; public string? Note;
        public DateTime LastSeenUtc;
    }

    private readonly Dictionary<string, Entry> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderStatus> _providers = new(StringComparer.Ordinal);

    public int Version { get; private set; }

    public static int? ValidPercent(int? p) => p is >= 0 and <= 100 ? p : null;

    public void Apply(ProviderResult r, DateTime nowUtc)
    {
        _providers[r.ProviderId] = r.Status; Version++;
        if (r.Status != ProviderStatus.Ok) return;   // un fallo no borra lo ya conocido
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in r.Readings)
        {
            string key = r.ProviderId + "|" + x.DeviceId; seen.Add(key);
            if (!_devices.TryGetValue(key, out var e)) _devices[key] = e = new Entry { Id = key, ProviderId = r.ProviderId };
            e.Name = x.Name; e.Kind = x.Kind; e.Connection = x.Connection; e.Connected = x.Connected; e.Capability = x.Capability; e.Note = x.Note; e.LastSeenUtc = nowUtc;
            var pct = ValidPercent(x.Percent);
            if (x.Connected && pct is not null) { e.Percent = pct; e.ReadAtUtc = nowUtc; e.Charge = x.Charge; }
            else if (!x.Connected) e.Charge = ChargeState.Unknown;   // desconectado: la carga no se sabe
            else if (pct is null) e.Charge = ChargeState.Unknown;
        }

        // lo que ese proveedor ya no lista: se da por desconectado (se conserva la última lectura)
        foreach (var e in _devices.Values.Where(v => v.ProviderId == r.ProviderId && !seen.Contains(v.Id))) { e.Connected = false; e.Charge = ChargeState.Unknown; }
    }

    public bool ProviderTrouble(string providerId) => _providers.TryGetValue(providerId, out var s) && s != ProviderStatus.Ok;
    public IReadOnlyDictionary<string, ProviderStatus> Providers => _providers;

    public IReadOnlyList<DeviceView> View(DateTime nowUtc, ISet<string>? hidden = null, bool includeHidden = true)
    {
        var res = new List<DeviceView>();
        foreach (var e in _devices.Values.OrderBy(v => !v.Connected).ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool hid = hidden?.Contains(e.Id) == true; if (hid && !includeHidden) continue;
            BatteryStatus st;
            if (!e.Connected) st = BatteryStatus.Disconnected;
            else if (e.Percent is null) st = e.Capability == BatteryCapability.Unsupported ? BatteryStatus.Unsupported : BatteryStatus.Unknown;
            else st = e.ReadAtUtc is { } at && nowUtc - at <= Fresh && at - nowUtc <= TimeSpan.FromMinutes(1) ? BatteryStatus.Current : BatteryStatus.Stale;
            res.Add(new DeviceView(e.Id, e.Name, e.Kind, e.Connection, e.Connected, st, e.Percent, e.ReadAtUtc, st == BatteryStatus.Current ? e.Charge : ChargeState.Unknown, e.Note, hid, ProviderTrouble(e.ProviderId)));
        }

        return res;
    }

    /// <summary>Apaga todo (módulo desactivado): nada queda en memoria.</summary>
    public void Clear() { _devices.Clear(); _providers.Clear(); Version++; }
}
