using System.Text.Json;
using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Notch;

/// <summary>Un periférico tal como lo ve la ventana de Ajustes (solo lectura). <see cref="Percent"/> null = el dispositivo no informa de batería (nunca se inventa un número).</summary>
public sealed record DeviceInfo(string Name, string Type, string Connection, bool Connected, int? Percent, bool Charging);

/// <summary>
/// Settings V2: la Barra le dice al host qué periféricos hay (los que YA lee para su icono y el panel; nada nuevo se lee ni se guarda). Tópico <see cref="Topic"/>, de la Barra al host.
/// Sin periféricos activados la lista va vacía y Ajustes lo explica.
/// </summary>
public static class DevicesFeed
{
    public const string Topic = "settings.devices";
    public const int Max = 24;

    public static object Payload(IReadOnlyList<DeviceView> all) => new
    {
        kind = "devices",
        items = all.Where(d => !d.Hidden).Take(Max).Select(d => new
        {
            name = d.Name, type = d.Kind.ToString().ToLowerInvariant(), connection = d.Connection.ToString().ToLowerInvariant(), connected = d.Connected,
            percent = d.Battery == BatteryStatus.Current && d.Percent is >= 0 and <= 100 ? d.Percent : null, charging = d.ChargeText is not null,
        }).ToList(),
    };

    public static string Key(IReadOnlyList<DeviceView> all) => string.Join(",", all.Where(d => !d.Hidden).Take(Max).Select(d => d.Name + ":" + d.Connected + ":" + (d.Battery == BatteryStatus.Current ? d.Percent : null) + ":" + (d.ChargeText is not null)));

    /// <summary>Lee una carga útil (no fiable): ignora lo que no tenga forma y recorta nombres.</summary>
    public static IReadOnlyList<DeviceInfo> Parse(JsonElement? data)
    {
        var list = new List<DeviceInfo>();
        try
        {
            if (data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
            foreach (var it in items.EnumerateArray())
            {
                if (list.Count >= Max || it.ValueKind != JsonValueKind.Object) continue;
                string name = it.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : ""; if (name.Length == 0) continue;
                if (name.Length > 80) name = name[..80];
                string Str(string k) => it.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
                int? pct = it.TryGetProperty("percent", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int pi) && pi is >= 0 and <= 100 ? pi : null;
                bool B(string k) => it.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
                list.Add(new DeviceInfo(name, Str("type"), Str("connection"), B("connected"), pct, B("charging")));
            }
        }
        catch (Exception) { }
        return list;
    }
}
