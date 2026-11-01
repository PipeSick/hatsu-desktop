using WinMac.Calendar;
using WinMac.SystemControl;
using Xunit;

public class CalendarGridTests
{
    [Fact]
    public void A_month_is_always_six_weeks_and_starts_on_the_chosen_weekday()
    {
        var g = CalendarGrid.Month(2026, 11, new DateOnly(2026, 11, 1));   // 1 nov 2026 = domingo
        Assert.Equal(42, g.Count); Assert.Equal(DayOfWeek.Monday, g[0].Date.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 10, 26), g[0].Date); Assert.False(g[0].InMonth); Assert.True(g[6].InMonth); Assert.True(g[6].IsToday);
        var sun = CalendarGrid.Month(2026, 11, new DateOnly(2026, 11, 1), firstDay: DayOfWeek.Sunday);
        Assert.Equal(new DateOnly(2026, 11, 1), sun[0].Date); Assert.True(sun[0].InMonth);
    }

    [Fact]
    public void Every_day_of_the_month_appears_exactly_once_in_month()
    {
        for (int m = 1; m <= 12; m++)
        {
            var g = CalendarGrid.Month(2028, m, new DateOnly(2028, 1, 1));
            Assert.Equal(DateTime.DaysInMonth(2028, m), g.Count(d => d.InMonth));
            Assert.Equal(g.Select(d => d.Date).Distinct().Count(), g.Count);   // sin repetidos (febrero bisiesto incluido)
        }
    }

    [Fact]
    public void Events_are_counted_per_day_including_multi_day_and_all_day_with_exclusive_end()
    {
        var off = TimeSpan.Zero;
        var evs = new List<CalendarEvent>
        {
            new("Daily", new DateTimeOffset(2026, 11, 5, 10, 0, 0, off), new DateTimeOffset(2026, 11, 5, 10, 15, 0, off), false),
            new("Feriado", new DateTimeOffset(2026, 11, 6, 0, 0, 0, off), new DateTimeOffset(2026, 11, 7, 0, 0, 0, off), true),   // fin exclusivo: solo el 6
            new("Viaje", new DateTimeOffset(2026, 11, 9, 8, 0, 0, off), new DateTimeOffset(2026, 11, 11, 20, 0, 0, off), false),
        };
        var g = CalendarGrid.Month(2026, 11, new DateOnly(2026, 11, 2), evs);
        int N(int day) => g.Single(d => d.Date == new DateOnly(2026, 11, day)).Events;
        Assert.Equal(1, N(5)); Assert.Equal(1, N(6)); Assert.Equal(0, N(7)); Assert.Equal(1, N(9)); Assert.Equal(1, N(10)); Assert.Equal(1, N(11)); Assert.Equal(0, N(12));
        Assert.Equal(["Feriado"], CalendarGrid.EventsOn(evs, new DateOnly(2026, 11, 6)).Select(e => e.Title));
    }

    [Fact]
    public void Titles_headers_and_month_shifts_work_across_years()
    {
        Assert.Equal("noviembre 2026", CalendarGrid.MonthTitle(2026, 11, "es")); Assert.Equal("November 2026", CalendarGrid.MonthTitle(2026, 11, "en"));
        Assert.Equal(["lun", "mar", "mié", "jue", "vie", "sáb", "dom"], CalendarGrid.WeekdayHeaders("es"));
        Assert.Equal((2027, 1), CalendarGrid.Shift(2026, 12, 1)); Assert.Equal((2025, 12), CalendarGrid.Shift(2026, 1, -1)); Assert.Equal((2026, 11), CalendarGrid.Shift(2026, 11, 0)); Assert.Equal((2025, 11), CalendarGrid.Shift(2026, 11, -12));
    }
}

public class ServiceResultAndActionsTests
{
    [Fact]
    public void Guard_turns_exceptions_into_failures_and_access_errors_into_denied()
    {
        Assert.True(ServiceResult.Guard(() => ServiceResult.Ok, "x").IsOk);
        Assert.Equal(ServiceStatus.Denied, ServiceResult.Guard(() => throw new UnauthorizedAccessException("no"), "x").Status);
        var f = ServiceResult.Guard(() => throw new InvalidOperationException("boom"), "Volumen"); Assert.Equal(ServiceStatus.Failed, f.Status); Assert.Contains("Volumen", f.Message);
    }

    [Fact]
    public void Destructive_actions_ask_for_confirmation_and_force_quit_always_does()
    {
        Assert.True(SystemActions.NeedsConfirmation(SystemActionKind.Shutdown, true)); Assert.False(SystemActions.NeedsConfirmation(SystemActionKind.Shutdown, false));
        Assert.True(SystemActions.NeedsConfirmation(SystemActionKind.ForceQuit, false));   // matar un proceso nunca sin confirmar
        Assert.False(SystemActions.NeedsConfirmation(SystemActionKind.Lock, true)); Assert.False(SystemActions.NeedsConfirmation(SystemActionKind.Sleep, true));
        Assert.All(SystemActions.All.Where(a => a.Destructive), a => { Assert.NotNull(a.ConfirmTitle); Assert.NotNull(a.ConfirmText); });
        Assert.Equal(Enum.GetValues<SystemActionKind>().Length, SystemActions.All.Count);   // ninguna acción sin entrada de menú
    }

    [Theory]
    [InlineData("explorer.exe", false)] [InlineData("WinMac.Bar.App", false)] [InlineData("NotchWin.App.exe", false)] [InlineData("csrss", false)] [InlineData("dwm.exe", false)]
    [InlineData("notepad.exe", true)] [InlineData("chrome", true)] [InlineData("", false)] [InlineData(null, false)]
    public void Force_quit_never_targets_the_shell_critical_processes_or_winmac_itself(string? name, bool allowed) => Assert.Equal(allowed, ForceQuitGuard.IsAllowed(name));

    [Fact]
    public void Models_map_levels_to_waves_bars_and_percentages()
    {
        Assert.Equal(0, new AudioState(true, 0, false, []).Waves); Assert.Equal(0, new AudioState(true, 50, true, []).Waves); Assert.Equal(1, new AudioState(true, 20, false, []).Waves); Assert.Equal(2, new AudioState(true, 50, false, []).Waves); Assert.Equal(3, new AudioState(true, 90, false, []).Waves);
        Assert.Equal(0, WifiState.BarsFor(0)); Assert.Equal(1, WifiState.BarsFor(10)); Assert.Equal(2, WifiState.BarsFor(30)); Assert.Equal(3, WifiState.BarsFor(60)); Assert.Equal(4, WifiState.BarsFor(100));
        Assert.Equal(0, new WifiState(true, false, null, 90, []).Bars);   // sin conexión: sin barras aunque quede un dato viejo
        Assert.Equal(0, BrightnessState.None("x").Percent); Assert.Equal(40, new BrightnessState(true, [new("a", "M", 40, "ddc")]).Percent);
        Assert.Equal(2, new BluetoothState(true, true, [new("1", "a", true), new("2", "b", false), new("3", "c", true)]).ConnectedCount);
    }
}
