using WinMac.Bar.Hub;
using WinMac.Bar.Notifications;
using WinMac.Calendar;

namespace WinMac.Bar;

/// <summary>Lo que el Centro de Notificaciones necesita además del contexto común: el estado de las notificaciones, los grupos desplegados y el ancho para este monitor.</summary>
public sealed record NcView(NcState State, IReadOnlySet<string> Expanded, double Width = 356);

public static partial class PanelBuilder
{
    public const int NcMaxGroups = 16, NcMaxCards = 40, NcMaxCities = 3;

    /// <summary>Prefijo de las claves de icono de app (el anfitrión las resuelve con el icono que entregó Windows).</summary>
    public const string NcIconPrefix = "notif-app:";

    /// <summary>
    /// Centro de Notificaciones: encabezado con día y fecha (fijo arriba), notificaciones reales agrupadas por app, widgets y el acceso a la configuración de Windows (fijo abajo).
    /// Cada estado de Windows (sin activar, sin permiso, denegado, no disponible, error) tiene su explicación y solo los botones que de verdad funcionan.
    /// </summary>
    public static PanelModel NotificationCenter(PanelContext c)
    {
        var nc = c.Nc ?? new NcView(NcState.Off, new HashSet<string>());
        var (day, date) = NotificationText.Header(c.Now, c.Cfg.Language);
        var w = new List<Widget> { new(WidgetKind.NcHeader, "nc-head", day, date, Data: new NcHeaderData(day, date)) };
        NotificationsSection(w, c, nc);
        WidgetsSection(w, c);
        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:notifications", "Configuración de notificaciones…", Glyph: "gear"));
        return new PanelModel(PanelKind.Notifications, "Centro de Notificaciones", w, nc.Width, PinTop: 1, PinBottom: 1, FillHeight: true);
    }

    private static Widget Empty(string id, string title, string text, string glyph = "bell") => new(WidgetKind.NcEmpty, id, title, text, Glyph: glyph);

    private static void NotificationsSection(List<Widget> w, PanelContext c, NcView nc)
    {
        var st = nc.State; bool win = c.Cfg.NotifRemoveFromWindows;
        if (!c.Cfg.NotificationsFromWindows)
        {
            w.Add(Empty("nc-off", "Notificaciones de tus apps", "WinMac puede mostrar aquí las notificaciones que Windows guarda en su centro, con tu permiso. Se leen solo en este equipo y no se guardan en disco."));
            w.Add(new Widget(WidgetKind.Button, "nc:enable", "Mostrar notificaciones de Windows", Glyph: "bell"));
            return;
        }

        switch (st.Status)
        {
            case NcStatus.Off or NcStatus.Loading when st.Groups.Count == 0: w.Add(Empty("nc-loading", "Leyendo notificaciones…", "")); return;
            case NcStatus.NeedsPermission:
                w.Add(Empty("nc-perm", "Permiso necesario", "Windows pide tu permiso para que WinMac lea las notificaciones de otras apps. Puedes retirarlo cuando quieras en Configuración.", "lock"));
                w.Add(new Widget(WidgetKind.Button, "nc:request", "Permitir acceso…", Glyph: "lock")); return;
            case NcStatus.Requesting: w.Add(Empty("nc-asking", "Esperando a Windows…", "Responde al cuadro de permiso de Windows.", "lock")); return;
            case NcStatus.Denied:
                w.Add(Empty("nc-denied", "Acceso denegado", st.Message ?? "Windows no permite que WinMac lea tus notificaciones.", "lock"));
                w.Add(new Widget(WidgetKind.Button, "open:ms-settings:privacy-notifications", "Abrir permisos de notificaciones…", Glyph: "gear")); return;
            case NcStatus.Unavailable:
                w.Add(Empty("nc-unavailable", "No disponible en este equipo", st.Message ?? "El lector de notificaciones de Windows no está disponible para WinMac."));
                w.Add(new Widget(WidgetKind.Button, "notifications", "Abrir el centro de notificaciones de Windows", Glyph: "bell")); return;
            case NcStatus.Error when st.Groups.Count == 0:
                w.Add(Empty("nc-error", "No se pudieron leer", st.Message ?? "Windows no respondió."));
                w.Add(new Widget(WidgetKind.Button, "nc:retry", "Reintentar", Glyph: "refresh")); return;
        }

        if (st.Groups.Count == 0)
        {
            w.Add(Empty("nc-empty", "Sin notificaciones", "Las notificaciones nuevas de tus apps aparecerán aquí."));
            if (st.Message is not null) w.Add(new Widget(WidgetKind.Note, "nc-msg", st.Message));
            return;
        }

        w.Add(new Widget(WidgetKind.NcSection, "nc-sec", "Notificaciones", st.Count.ToString(), Data: new NcSectionData("nc:clearall", win ? "Borrar todo" : "Ocultar todo")));
        int cards = 0; var now = new DateTimeOffset(c.Now);
        foreach (var g in st.Groups.Take(NcMaxGroups))
        {
            if (cards >= NcMaxCards) break;
            string? icon = g.HasIcon ? NcIconPrefix + g.Key : null;
            Widget Card(AppNotification n, bool showApp, int stacked)
            {
                var (p, s) = NotificationText.Lines(n); cards++;
                return new Widget(WidgetKind.NotifCard, "n:" + n.Id, p, s, Data: new NotifCardData(n.Id, g.Key, icon, g.AppName, p, s, NotificationText.When(n.Time, now, c.Cfg.Use24Hour), true, stacked, showApp));
            }

            if (g.Items.Count == 1) { w.Add(Card(g.Items[0], true, 0)); continue; }
            bool open = nc.Expanded.Contains(g.Key);
            w.Add(new Widget(WidgetKind.NotifGroup, "ng:" + g.Key, g.AppName, g.Items.Count.ToString(), On: open, Data: new NotifGroupData(g.Key, icon, g.AppName, g.Items.Count, open, win ? "Borrar" : "Ocultar")));
            if (!open) { w.Add(Card(g.Items[0], false, g.Items.Count - 1)); continue; }
            foreach (var n in g.Items) { if (cards >= NcMaxCards) break; w.Add(Card(n, false, 0)); }
        }

        int shown = cards, total = st.Count;
        if (shown < total) w.Add(new Widget(WidgetKind.Note, "nc-more", $"Se muestran {shown} de {total}. Las demás siguen en el centro de notificaciones de Windows."));
        if (st.Message is not null) w.Add(new Widget(WidgetKind.Note, "nc-msg", st.Message));
        w.Add(new Widget(WidgetKind.Note, "nc-mode", (win
            ? "«×» y «Borrar» eliminan la notificación también del centro de notificaciones de Windows."
            : "«×» y «Ocultar» solo la ocultan en WinMac durante esta sesión: sigue en el centro de Windows.")
            + (st.Live ? "" : " Se actualiza cada pocos segundos mientras el panel está abierto.")));
    }

    private static void WidgetsSection(List<Widget> w, PanelContext c)
    {
        var list = NcWidgets.Parse(c.Cfg.NcWidgets).Where(x => x != NcWidget.Battery || c.Battery.Present).ToList();   // sin batería (sobremesa) no hay nada que mostrar: no se inventa un porcentaje
        if (list.Count == 0) return;
        w.Add(new Widget(WidgetKind.NcSection, "nc-wsec", "Widgets", Data: new NcSectionData("nc:settings", "Editar")));
        foreach (var kind in list)
        {
            switch (kind)
            {
                case NcWidget.Calendar: CalendarWidget(w, c); break;
                case NcWidget.Weather: WeatherWidget(w, c); break;
                case NcWidget.Battery: BatteryWidget(w, c); break;
            }
        }
    }

    private static void CalendarWidget(List<Widget> w, PanelContext c)
    {
        string lang = c.Cfg.Language; var today = DateOnly.FromDateTime(c.Now);
        w.Add(new Widget(WidgetKind.Nav, "nav", CalendarGrid.MonthTitle(c.ViewYear, c.ViewMonth, lang)));
        w.Add(new Widget(WidgetKind.Month, "month", Data: new MonthData(CalendarGrid.Month(c.ViewYear, c.ViewMonth, today, c.Events), CalendarGrid.WeekdayHeaders(lang), c.SelectedDay)));
        var sel = c.SelectedDay ?? today; var evs = CalendarGrid.EventsOn(c.Events, sel);
        foreach (var e in evs.Take(3)) w.Add(new Widget(WidgetKind.Info, "ev:" + e.Title + e.Start.Ticks, e.Title, e.AllDay ? "Todo el día" : e.Start.ToString("HH:mm") + (e.End > e.Start ? "–" + e.End.ToString("HH:mm") : "")));
    }

    private static void WeatherWidget(List<Widget> w, PanelContext c)
    {
        var st = c.Hub?.Weather ?? WeatherState.NoCity; bool f = c.Cfg.WeatherFahrenheit;
        if (st.Status == WeatherStatus.NoCity)
        {
            w.Add(new Widget(WidgetKind.Note, "wx-nocity", "Tiempo: elige una ciudad en los ajustes de WinMac."));
            w.Add(new Widget(WidgetKind.Button, "hub:settings-weather", "Elegir ciudad…", Glyph: "gear")); return;
        }

        if (st.Snapshot is null) { w.Add(new Widget(WidgetKind.Note, "wx-msg", "Tiempo: " + (st.Message ?? "cargando…"))); return; }
        var cw = st.Snapshot.Main;
        string? hl = cw.HighC is { } hi && cw.LowC is { } lo ? $"Máx. {TempFormat.Degrees(hi, f)} Mín. {TempFormat.Degrees(lo, f)}" : null;
        w.Add(new Widget(WidgetKind.WeatherHead, "wx-head", Data: new WeatherHeadData(cw.Place.Name, c.Cfg.WeatherAutoLocation, TempFormat.Degrees(cw.TempC, f), WeatherCodes.Icon(cw.Code, cw.IsDay), WeatherCodes.Describe(cw.Code), hl)));
        foreach (var o in st.Snapshot.Others.Take(NcMaxCities)) w.Add(new Widget(WidgetKind.CityRow, "city:" + o.Place.Key, o.Place.Name, TempFormat.Degrees(o.TempC, f), Glyph: WeatherCodes.Icon(o.Code, o.IsDay)));
        if (st.Status is WeatherStatus.Offline or WeatherStatus.Stale or WeatherStatus.Error) w.Add(new Widget(WidgetKind.Note, "wx-stale", st.Status == WeatherStatus.Offline ? "Tiempo sin conexión: se muestran los últimos datos." : "Datos del tiempo desactualizados."));
    }

    private static void BatteryWidget(List<Widget> w, PanelContext c)
    {
        var b = c.Battery; if (!b.Present) return;
        double frac = b.Fill;
        w.Add(new Widget(WidgetKind.MeterRow, "nc-battery", b.Charging ? "Cargando" : "Batería", $"{b.Percent} %", Value: frac, Data: new MeterData(b.Charging || frac >= 0.5 ? "green" : frac >= 0.2 ? "yellow" : "red")));
    }
}
