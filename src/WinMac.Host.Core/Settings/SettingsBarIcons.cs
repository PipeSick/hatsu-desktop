using WinMac.Bar;

namespace WinMac.Host;

/// <summary>Un icono que la barra puede mostrar: su identificador en la barra, la opción que lo enciende y el texto. Un solo lugar para agregar, quitar y ordenar (propuesta «Personalizar los iconos de la barra»).</summary>
public sealed record BarIconDef(string Id, string Field, string Title, string Sub, string[]? AlsoSet = null);

public static partial class SettingsBuilder
{
    public static readonly BarIconDef[] BarIcons =
    [
        new("Volume", "showVolume", "Sonido", "Volumen y mezclador de aplicaciones"),
        new("HubSystem", "showHubSystem", "Rendimiento", "CPU, memoria y temperaturas", ["showHub"]),
        new("HubNetwork", "showHubNetwork", "Red", "Velocidad y conexiones", ["showHub"]),
        new("HubDisks", "showHubDisks", "Discos", "Espacio libre y estado", ["showHub"]),
        new("Weather", "showWeather", "Tiempo", "El clima de tus ciudades"),
        new("Network", "showNetwork", "Wi-Fi / red", "Estado de la conexión"),
        new("Bluetooth", "showBluetooth", "Bluetooth", "Estado y dispositivos conectados"),
        new("Battery", "showBattery", "Batería", "Porcentaje y carga"),
        new("Vpn", "showVpn", "VPN", "Conectar y desconectar tus VPN de Windows"),
        new("Cleanup", "showCleanup", "Limpieza", "Liberar espacio sin riesgos"),
        new("Peripherals", "showPeripherals", "Periféricos", "Batería de mouse, teclado y auriculares"),
        new("TaskManager", "showTaskManager", "Administrador de tareas", "Abre el de Windows"),
        new("TrayMenu", "showTrayIcons", "Apps en segundo plano", "Los iconos de la bandeja de Windows, dentro de una flecha"),
    ];

    public static bool BarIconOn(BarConfig b, BarIconDef d) => ToggleValue(b, d.Field) && (d.AlsoSet is null || b.ShowHub);

    /// <summary>Los iconos que están en la barra, en el orden elegido (el que guarda <c>BarOrder</c>; los que no tienen posición guardada siguen el orden de la tabla).</summary>
    public static IReadOnlyList<BarIconDef> BarIconsInOrder(BarConfig b)
    {
        var saved = WinMac.Bar.BarOrdering.Parse(b.BarOrder).ToList();
        int Key(BarIconDef d) { int i = saved.IndexOf(d.Id); return i >= 0 ? i : 1000 + Array.IndexOf(BarIcons, d); }
        return BarIcons.Where(d => BarIconOn(b, d)).OrderBy(Key).ToList();
    }

    internal static IEnumerable<SettingsGroup> BarIconGroups(BarConfig b)
    {
        var on = BarIconsInOrder(b);
        var rows = on.Select((d, i) => new SettingsRow(RowKind.Toggle, "baritem:" + d.Id, d.Title, d.Sub, true, CanUp: i > 0, CanDown: i < on.Count - 1)).ToList();
        rows.Insert(0, new SettingsRow(RowKind.Preview, "bar.preview", "Así queda tu barra", Options: on.Select(d => d.Title).ToArray()));
        yield return new("En la barra", rows, "Arrastra el asa ⋮⋮ de un icono para cambiar su orden. El reloj y el Centro de control quedan siempre al final.");
        var off = BarIcons.Where(d => !BarIconOn(b, d)).ToList();
        if (off.Count > 0) yield return new("Disponibles para agregar", off.Select(d => new SettingsRow(RowKind.Button, "baritem.add:" + d.Id, d.Title, d.Sub, Options: ["Agregar"])).ToList(),
            "Pulsa «Agregar» y aparece en la barra. «Personalizar iconos…» te lleva hasta aquí. Los iconos de las apps en segundo plano de Windows se eligen por separado desde su flecha.");
    }
}
