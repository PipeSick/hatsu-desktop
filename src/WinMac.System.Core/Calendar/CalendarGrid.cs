namespace WinMac.Calendar;

/// <summary>Una casilla del mes. <see cref="InMonth"/> = false para los días de relleno del mes anterior o siguiente.</summary>
public sealed record CalendarDay(DateOnly Date, bool InMonth, bool IsToday, int Events);

/// <summary>Cuadrícula de un mes (siempre 6 semanas × 7 días, como el calendario de macOS) y reparto de eventos por día. Lógica pura.</summary>
public static class CalendarGrid
{
    public static IReadOnlyList<CalendarDay> Month(int year, int month, DateOnly today, IEnumerable<CalendarEvent>? events = null, DayOfWeek firstDay = DayOfWeek.Monday)
    {
        var first = new DateOnly(year, month, 1);
        int lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-lead);
        var perDay = new Dictionary<DateOnly, int>();
        foreach (var e in events ?? [])
        {
            var d = DateOnly.FromDateTime(e.Start.DateTime);
            // un evento de varios días ocupa cada día hasta el último (el fin de un evento de día completo es exclusivo)
            var last = DateOnly.FromDateTime(e.AllDay ? e.End.DateTime.AddTicks(-1) : e.End.DateTime);
            if (last < d) last = d;
            for (int guard = 0; d <= last && guard < 62; guard++, d = d.AddDays(1)) perDay[d] = perDay.GetValueOrDefault(d) + 1;
        }

        var days = new List<CalendarDay>(42);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            days.Add(new CalendarDay(d, d.Month == month && d.Year == year, d == today, perDay.GetValueOrDefault(d)));
        }

        return days;
    }

    /// <summary>Nombres cortos de los días en el orden de la cuadrícula.</summary>
    public static IReadOnlyList<string> WeekdayHeaders(string language, DayOfWeek firstDay = DayOfWeek.Monday)
    {
        string[] es = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"], en = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        var names = language == "en" ? en : es;
        return Enumerable.Range(0, 7).Select(i => names[((int)firstDay + i) % 7]).ToList();
    }

    public static string MonthTitle(int year, int month, string language)
    {
        string[] es = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
                 en = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
        return $"{(language == "en" ? en : es)[month - 1]} {year}";
    }

    /// <summary>Eventos de un día, ordenados (los de día completo primero).</summary>
    public static IReadOnlyList<CalendarEvent> EventsOn(IEnumerable<CalendarEvent> events, DateOnly day) =>
        events.Where(e =>
        {
            var s = DateOnly.FromDateTime(e.Start.DateTime);
            var l = DateOnly.FromDateTime(e.AllDay ? e.End.DateTime.AddTicks(-1) : e.End.DateTime);
            return s <= day && day <= (l < s ? s : l);
        }).OrderByDescending(e => e.AllDay).ThenBy(e => e.Start).ToList();

    public static (int Year, int Month) Shift(int year, int month, int delta)
    {
        int idx = year * 12 + (month - 1) + delta;
        return (idx / 12, idx % 12 + 1);
    }
}
