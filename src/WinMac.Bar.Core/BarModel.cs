namespace WinMac.Bar;

public enum BarItemKind { WinMacMenu, AppName, Network, Volume, Battery, Clock, Bluetooth, ControlCenter, AppMenu, Hub, Weather, TrayMenu, Phone }

/// <summary>
/// Un elemento de la barra. <see cref="Text"/> puede estar vacío (solo icono). <see cref="GlyphWidth"/> es el ancho fijo del icono (0 = sin icono).
/// El render mide el texto y el núcleo reparte el espacio (<see cref="BarLayout"/>).
/// </summary>
public sealed record BarItem(BarItemKind Kind, string Text, double GlyphWidth, bool Bold = false, string? Key = null)
{
    /// <summary>Identidad estable del elemento (el resalte la usa al repartir de nuevo). Los menús de app llevan su posición.</summary>
    public string Id => Key ?? Kind.ToString();
}

public enum BarActionKind { None, OpenSettings, ShowMenu, Quit, OpenPanel, ShowAppMenu }

/// <summary>
/// Lo que pasa al hacer clic. <see cref="Panel"/>: panel desplegable a abrir; <see cref="Uri"/>: página de Configuración (única acción en el modo antiguo y respaldo si el panel no puede abrirse).
/// <see cref="Index"/>: posición del menú de app (ShowAppMenu).
/// </summary>
public sealed record BarAction(BarActionKind Kind, string? Uri = null, PanelKind? Panel = null, int Index = 0);

/// <summary>Estado → elementos visibles (izquierda y derecha, en orden de pantalla). Sin lectura del sistema.</summary>
public static class BarModel
{
    public const double IconW = 16, BatteryW = 24;

    public static (IReadOnlyList<BarItem> Left, IReadOnlyList<BarItem> Right) Build(BarState s, BarConfig c)
    {
        var left = new List<BarItem> { new(BarItemKind.WinMacMenu, "", IconW) };
        if (!string.IsNullOrWhiteSpace(s.ActiveApp)) left.Add(new BarItem(BarItemKind.AppName, s.ActiveApp!, 0, Bold: true));
        if (c.AppMenus && s.AppMenus is { Count: > 0 } menus) for (int i = 0; i < menus.Count; i++) left.Add(new BarItem(BarItemKind.AppMenu, menus[i], 0, Key: "AppMenu" + i));

        var right = new List<BarItem>();
        if (c.ShowTrayIcons && s.Tray.Count > 0) right.Add(new BarItem(BarItemKind.TrayMenu, "", IconW));   // UN botón (flecha abajo; arriba con el desplegable abierto) con las apps en segundo plano, como la flechita de la bandeja de Windows
        if (c.ShowHub) right.Add(new BarItem(BarItemKind.Hub, "", IconW));   // UN solo icono de la aplicación: un panel con pestañas
        if (c.ShowWeather) right.Add(new BarItem(BarItemKind.Weather, s.Weather.Text, IconW));   // el Tiempo es un componente aparte, con su propio panel
        if (c.ShowBluetooth && s.Bt.Present) right.Add(new BarItem(BarItemKind.Bluetooth, "", IconW));
        if (c.ShowNetwork && s.Network.Kind != NetKind.Unknown) right.Add(new BarItem(BarItemKind.Network, "", IconW));
        if (c.ShowVolume && s.Volume.Available) right.Add(new BarItem(BarItemKind.Volume, "", IconW));
        // WinMac Connect: solo con el módulo activo y un iPhone conocido. El porcentaje, solo si es real y reciente (desconectado: el icono sin número).
        if (c.ShowPhone && s.Phone is { DeviceName: not null } ph && ph.Status is not (WinMac.Connect.PhoneLinkStatus.NoDevice or WinMac.Connect.PhoneLinkStatus.NoBluetooth) && ph.BatteryStatus != WinMac.Connect.PhoneFeatureStatus.Off)
            right.Add(new BarItem(BarItemKind.Phone, WinMac.Connect.PhoneStatusText.CurrentBattery(ph, s.Now.ToUniversalTime()) is { } pb ? $"{pb}%" : "", IconW));
        if (c.ShowBattery && s.Battery.Present) right.Add(new BarItem(BarItemKind.Battery, c.ShowBatteryPercent ? $"{s.Battery.Percent}%" : "", BatteryW));
        if (c.ShowControlCenter) right.Add(new BarItem(BarItemKind.ControlCenter, "", IconW));
        right.Add(new BarItem(BarItemKind.Clock, ClockFormat.Format(s.Now, c), 0));
        return (left, right);
    }

    /// <summary>Qué hace cada elemento al hacer clic. Las páginas son URI documentadas de Configuración (ms-settings:).</summary>
    public static BarAction ActionFor(BarItemKind kind) => kind switch
    {
        BarItemKind.WinMacMenu => new BarAction(BarActionKind.ShowMenu),
        BarItemKind.Network => new BarAction(BarActionKind.OpenSettings, "ms-settings:network"),
        BarItemKind.Volume => new BarAction(BarActionKind.OpenSettings, "ms-settings:sound"),
        BarItemKind.Battery => new BarAction(BarActionKind.OpenSettings, "ms-settings:batterysaver"),
        BarItemKind.Clock => new BarAction(BarActionKind.OpenSettings, "ms-settings:dateandtime"),
        BarItemKind.Bluetooth => new BarAction(BarActionKind.OpenSettings, "ms-settings:bluetooth"),
        BarItemKind.ControlCenter => new BarAction(BarActionKind.OpenSettings, "ms-settings:"),
        BarItemKind.AppMenu => new BarAction(BarActionKind.ShowAppMenu),
        BarItemKind.Hub => new BarAction(BarActionKind.OpenPanel, Panel: PanelKind.Hub),
        BarItemKind.Weather => new BarAction(BarActionKind.OpenPanel, Panel: PanelKind.Weather),
        BarItemKind.TrayMenu => new BarAction(BarActionKind.OpenPanel, Panel: PanelKind.Tray),
        BarItemKind.Phone => new BarAction(BarActionKind.OpenPanel, Panel: PanelKind.Phone),
        _ => new BarAction(BarActionKind.None),
    };

    /// <summary>Acción real del clic sobre un elemento concreto: abre su panel (con la página de Configuración como respaldo) o el menú de la app.</summary>
    /// <param name="notificationCenter">El reloj abre el Centro de Notificaciones (ajuste <see cref="BarConfig.NotificationCenter"/>) en vez del calendario.</param>
    public static BarAction ClickAction(BarItem item, bool notificationCenter = false)
    {
        var basic = ActionFor(item.Kind);
        return item.Kind switch
        {
            BarItemKind.Network => basic with { Kind = BarActionKind.OpenPanel, Panel = PanelKind.Wifi },
            BarItemKind.Bluetooth => basic with { Kind = BarActionKind.OpenPanel, Panel = PanelKind.Bluetooth },
            BarItemKind.Volume => basic with { Kind = BarActionKind.OpenPanel, Panel = PanelKind.Sound },
            BarItemKind.Battery => basic with { Kind = BarActionKind.OpenPanel, Panel = PanelKind.Battery },
            BarItemKind.ControlCenter => basic with { Kind = BarActionKind.OpenPanel, Panel = PanelKind.ControlCenter },
            BarItemKind.Clock => basic with { Kind = BarActionKind.OpenPanel, Panel = notificationCenter ? PanelKind.Notifications : PanelKind.Calendar },
            BarItemKind.AppMenu => new BarAction(BarActionKind.ShowAppMenu, Index: int.TryParse(item.Id.AsSpan(7), out int i) ? i : 0),
            _ => basic,
        };
    }

    public static bool IsClickable(BarItemKind kind) => ActionFor(kind).Kind != BarActionKind.None;

    /// <summary>Clic derecho en la barra: el menú de WinMac.</summary>
    public static BarAction RightClickAction(BarItem? item) => new(BarActionKind.ShowMenu);
}
