namespace WinMac.Bar;

/// <summary>Texto del reloj. Tablas de nombres propias (es/en): resultado idéntico en cualquier equipo, sin depender de ICU ni de la configuración regional.</summary>
public static class ClockFormat
{
    private static readonly string[] EsDays = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"], EnDays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] EsMonths = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"], EnMonths = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string Date(DateTime t, string language) => language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
        ? $"{EnDays[(int)t.DayOfWeek]} {EnMonths[t.Month - 1]} {t.Day}"
        : $"{EsDays[(int)t.DayOfWeek]} {t.Day} {EsMonths[t.Month - 1]}";

    public static string Time(DateTime t, BarConfig c)
    {
        bool en = c.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase); string sec = c.ShowSeconds ? $":{t.Second:00}" : "";
        if (c.Use24Hour) return $"{t.Hour:00}:{t.Minute:00}{sec}";
        int h = t.Hour % 12 == 0 ? 12 : t.Hour % 12; string suffix = t.Hour < 12 ? (en ? "AM" : "a. m.") : (en ? "PM" : "p. m.");
        return $"{h}:{t.Minute:00}{sec} {suffix}";
    }

    /// <summary>Texto completo de la barra: «vie 31 oct 08:12» (o solo la hora si la fecha está desactivada).</summary>
    public static string Format(DateTime t, BarConfig c) => c.ShowDate ? $"{Date(t, c.Language)}  {Time(t, c)}" : Time(t, c);

    /// <summary>Tiempo hasta que cambie el texto (para dormir hasta el próximo cambio en vez de sondear).</summary>
    public static TimeSpan UntilNextChange(DateTime now, BarConfig c)
    {
        var next = c.ShowSeconds ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second).AddSeconds(1) : new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(1);
        var d = next - now; return d < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : d;
    }
}
