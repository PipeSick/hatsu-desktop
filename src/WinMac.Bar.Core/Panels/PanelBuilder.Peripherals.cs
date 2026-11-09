using WinMac.Bar.Peripherals;
using WinMac.Bar.Cleanup;

namespace WinMac.Bar;

/// <summary>Lo que el panel de Periféricos necesita (ya normalizado por <see cref="PeripheralStore"/>).</summary>
public sealed record PeripheralsView(IReadOnlyList<DeviceView> Devices, int HiddenCount, bool Busy, bool AlertsOn, int Threshold);

public static partial class PanelBuilder
{
    public const string PeriphRefresh = "periph:refresh", PeriphUnhide = "periph:unhide-all", PeriphSettings = "periph:settings", PeriphHidePrefix = "periph:hide:";

    public static string KindText(DeviceKind k) => k switch { DeviceKind.Mouse => "Ratón", DeviceKind.Keyboard => "Teclado", DeviceKind.Headset => "Auriculares", DeviceKind.Gamepad => "Mando", _ => "Dispositivo" };
    public static string ConnectionText(ConnectionKind k) => k switch { ConnectionKind.Bluetooth => "Bluetooth", ConnectionKind.Usb => "USB", ConnectionKind.Dongle => "Receptor 2,4 GHz", _ => "Conexión desconocida" };

    public static string BatteryLine(DeviceView d, DateTime nowUtc) => d.Battery switch
    {
        BatteryStatus.Current => $"{d.Percent} %",
        BatteryStatus.Stale => $"{d.Percent} % · lectura antigua ({PhoneAgo(d.ReadAtUtc, nowUtc)})",
        BatteryStatus.Unknown => "Batería desconocida",
        BatteryStatus.Unsupported => "Sin datos de batería",
        _ => d.Percent is { } p ? $"Última lectura: {p} % · {PhoneAgo(d.ReadAtUtc, nowUtc)}" : "Sin lectura",
    };

    private static string PhoneAgo(DateTime? at, DateTime now) => WinMac.Connect.PhoneStatusText.Ago(at, now);

    public static PanelModel PeripheralsPanel(PanelContext c)
    {
        var w = new List<Widget>(); var v = c.Periph; var now = c.Now.ToUniversalTime();
        w.Add(new Widget(WidgetKind.Header, "periph-title", "Periféricos"));
        if (v is null || v.Devices.Count == 0)
        {
            w.Add(new Widget(WidgetKind.Note, "periph-empty", v is { Busy: true } ? "Buscando periféricos…" : v is { HiddenCount: > 0 } ? "Todos los dispositivos están ocultos." : "No se encontró ningún periférico. Hatsu Desktop solo muestra lo que Windows y el propio dispositivo informan (Bluetooth estándar y HID); los receptores 2,4 GHz y el software de cada fabricante no se consultan."));
        }
        else
        {
            // Solo lo que está conectado y tiene datos: los dispositivos desconectados no se muestran (no hay nada que informar), y los conectados sin lectura de batería van juntos en una línea.
            var connected = v.Devices.Where(d => !d.Hidden && d.Connected).ToList();
            var withData = connected.Where(d => d.Battery is BatteryStatus.Current or BatteryStatus.Stale).ToList();
            foreach (var d in withData)
            {
                string tone = d.Battery == BatteryStatus.Current ? (d.Percent <= 10 ? "bad" : d.Percent <= 20 ? "warn" : "good") : "off";
                string status = d.ProviderTrouble ? "El proveedor no responde" : d.Battery == BatteryStatus.Stale ? "Lectura antigua" : "";   // «Conectado» sobra: todo lo que se ve lo está
                w.Add(new Widget(WidgetKind.DeviceCard, "dev:" + d.Id, d.Name, Data: new DeviceCardData(d.Id, KindText(d.Kind), ConnectionText(d.Connection), BatteryLine(d, now),
                    d.Battery == BatteryStatus.Current && d.Percent is { } p ? p / 100.0 : null, status, d.ChargeText, tone, d.Note, PeriphHidePrefix + d.Id, "Ocultar")));
            }

            var noData = connected.Except(withData).ToList();
            var receivers = noData.Where(d => d.Connection == ConnectionKind.Dongle && d.Battery == BatteryStatus.Unsupported).ToList();   // receptor reconocido, pero Hatsu aún no sabe leer su batería (puede que el dispositivo ni esté encendido)
            var other = noData.Except(receivers).ToList();
            if (other.Count > 0) w.Add(new Widget(WidgetKind.Note, "periph-nodata", "Conectado, sin dato de batería: " + string.Join(", ", other.Select(d => d.Name)) + "."));
            if (receivers.Count > 0) w.Add(new Widget(WidgetKind.Note, "periph-receivers", "Receptor detectado, batería aún no compatible: " + string.Join(", ", receivers.Select(d => d.Name)) + "."));
            if (connected.Count == 0) w.Add(new Widget(WidgetKind.Note, "periph-none", "No hay periféricos conectados ahora."));
        }

        if (v is { Busy: true } && v.Devices.Count > 0) w.Add(new Widget(WidgetKind.Note, "periph-busy", "Actualizando…"));
        w.Add(new Widget(WidgetKind.Button, PeriphRefresh, v is { Busy: true } ? "Actualizando…" : "Actualizar", Enabled: v is not { Busy: true }));
        if (v is { HiddenCount: > 0 }) w.Add(new Widget(WidgetKind.Button, PeriphUnhide, v.HiddenCount == 1 ? "Mostrar 1 dispositivo oculto" : $"Mostrar {v.HiddenCount} dispositivos ocultos"));
        w.Add(new Widget(WidgetKind.Button, PeriphSettings, "Ajustes de periféricos…", Glyph: "gear"));
        if (v is { AlertsOn: true }) w.Add(new Widget(WidgetKind.Note, "periph-alerts", $"Aviso de batería baja al {v.Threshold} %."));
        return new PanelModel(PanelKind.Peripherals, "Periféricos", w);
    }
}
