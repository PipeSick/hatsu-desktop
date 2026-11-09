using System.Globalization;

namespace WinMac.Bar.Notifications;

/// <summary>Agrupación, orden y textos de las tarjetas. Puro: se prueba sin Windows.</summary>
public static class NotificationGrouping
{
    /// <summary>
    /// Agrupa por app (<see cref="AppNotification.GroupKey"/>), sin las ocultas. Dentro de cada grupo, de la más reciente a la más antigua (a igual hora, la de Id mayor primero);
    /// los grupos, por su notificación más reciente.
    /// </summary>
    public static IReadOnlyList<NotificationGroup> Group(IEnumerable<AppNotification> items, IReadOnlySet<uint>? hidden = null)
    {
        return items.Where(n => hidden is null || !hidden.Contains(n.Id))
            .GroupBy(n => n.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var sorted = g.OrderByDescending(n => n.Time).ThenByDescending(n => n.Id).ToList();
                var named = sorted.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.AppName)) ?? sorted[0];
                return new NotificationGroup(g.Key, named.DisplayApp, sorted[0].AppId, sorted);
            })
            .OrderByDescending(g => g.Latest).ThenBy(g => g.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

public static class NotificationText
{
    /// <summary>
    /// Las dos líneas de la tarjeta. Sin título, el cuerpo sube a la primera línea; sin nada de texto, se dice que no lo tiene (nunca se inventa contenido).
    /// </summary>
    public static (string Primary, string? Secondary) Lines(AppNotification n)
    {
        string? t = Clean(n.Title), b = Clean(n.Body);
        if (t is not null) return (t, b);
        if (b is not null) return (b, null);
        return ("Notificación sin texto", null);
    }

    /// <summary>Hora relativa como en macOS: «ahora», «hace 5 min», «14:05», «ayer», o la fecha.</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now, bool use24h)
    {
        var local = time.ToLocalTime(); var nowLocal = now.ToLocalTime(); var diff = nowLocal - local;
        if (diff < TimeSpan.FromMinutes(1) && diff > TimeSpan.FromMinutes(-1)) return "ahora";
        if (diff > TimeSpan.Zero && diff < TimeSpan.FromHours(1)) return $"hace {(int)diff.TotalMinutes} min";
        string clock = use24h ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (local.Date == nowLocal.Date) return clock;
        if (local.Date == nowLocal.Date.AddDays(-1)) return "ayer";
        return local.ToString("dd/MM", CultureInfo.InvariantCulture);
    }

    /// <summary>Encabezado del panel: «Domingo» y «2 de noviembre».</summary>
    public static (string Day, string Date) Header(DateTime now, string lang)
    {
        string[] es = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"], en = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        string[] mes = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
        string[] men = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        int d = (int)now.DayOfWeek, m = now.Month - 1;
        return lang == "en" ? (en[d], $"{men[m]} {now.Day}") : (es[d], $"{now.Day} de {mes[m]}");
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = string.Join(" ", s.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
        return t.Length == 0 ? null : t;
    }
}

/// <summary>Geometría del panel lateral y de su animación (px de pantalla y DIP). Pura.</summary>
public static class NcGeometry
{
    public const double MinWidth = 300, MaxWidth = 372, Margin = 8, Gap = 6;

    /// <summary>Ancho del panel según el monitor: una cuarta parte del ancho, entre 300 y 372 DIP, y nunca más que el monitor menos los márgenes.</summary>
    public static double Width(double monitorWidthDip) => Math.Max(200, Math.Min(Math.Clamp(monitorWidthDip * 0.25, MinWidth, MaxWidth), monitorWidthDip - 2 * Margin));

    /// <summary>
    /// Rectángulo del panel (px): pegado al borde derecho del monitor de la barra pulsada, desde justo debajo de la barra hasta el final del área de trabajo
    /// (no tapa la barra ni la barra de tareas de Windows). Si el área de trabajo no está en este monitor o no es válida, se usa el monitor.
    /// </summary>
    public static PxRect Place(int barBottomPx, PxRect monitor, PxRect work, double scale, double widthDip)
    {
        int margin = (int)Math.Round(Margin * scale), gap = (int)Math.Round(Gap * scale), w = (int)Math.Ceiling(widthDip * scale);
        bool workOk = !work.IsEmpty && work.Bottom > monitor.Y && work.Bottom <= monitor.Bottom && work.X < monitor.Right && work.Right > monitor.X;
        int bottom = (workOk ? work.Bottom : monitor.Bottom) - margin, right = (workOk ? Math.Min(work.Right, monitor.Right) : monitor.Right) - margin;
        int top = Math.Max(monitor.Y, barBottomPx) + gap;
        w = Math.Min(w, Math.Max(1, monitor.W - 2 * margin));
        int h = Math.Max((int)Math.Round(200 * scale), bottom - top);
        if (top + h > monitor.Bottom) top = Math.Max(monitor.Y, monitor.Bottom - h);
        return new PxRect(Math.Max(monitor.X + margin, right - w), top, w, h);
    }

    /// <summary>Desplazamiento horizontal (DIP, positivo = hacia la derecha, fuera de la vista) de la animación de entrada y salida. Curva «ease-out» cúbica, sin rebotes.</summary>
    public static double SlideOffset(double elapsedMs, double durationMs, double distance, bool opening)
    {
        if (durationMs <= 0) return opening ? 0 : distance;
        double u = Math.Clamp(elapsedMs / durationMs, 0, 1), e = 1 - Math.Pow(1 - u, 3);
        return opening ? distance * (1 - e) : distance * e;
    }

    public const double OpenMs = 220, CloseMs = 160;
}
