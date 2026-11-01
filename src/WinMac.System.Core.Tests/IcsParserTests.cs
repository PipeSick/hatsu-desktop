using WinMac.Calendar;
using Xunit;

public sealed class IcsParserTests
{
    private const string Sample = """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        SUMMARY:Daily del equipo
        DTSTART:20261030T100000
        DTEND:20261030T101500
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Almuerzo\, con Pipe
        DTSTART:20261030T140000
        DTEND:20261030T150000
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Feriado
        DTSTART;VALUE=DATE:20261031
        END:VEVENT
        BEGIN:VEVENT
        SUMMARY:Linea larga que se
          dobla
        DTSTART:20261029T090000
        DTEND:20261029T093000
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public void Parse_ReadsTitlesTimesAndAllDay()
    {
        var events = IcsParser.Parse(Sample);

        Assert.Equal(4, events.Count);
        var daily = events.Single(e => e.Title == "Daily del equipo");
        Assert.Equal(new DateTime(2026, 10, 30, 10, 0, 0), daily.Start.DateTime);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 15, 0), daily.End.DateTime);
        Assert.False(daily.AllDay);

        Assert.True(events.Single(e => e.Title == "Feriado").AllDay);
    }

    [Fact]
    public void Parse_UnescapesCommasAndUnfoldsLines()
    {
        var events = IcsParser.Parse(Sample);

        Assert.Contains(events, e => e.Title == "Almuerzo, con Pipe");
        Assert.Contains(events, e => e.Title == "Linea larga que se dobla");
    }

    [Fact]
    public void UpcomingToday_ExcludesFinishedAndOtherDays_Sorted()
    {
        var events = IcsParser.Parse(Sample);
        var now = new DateTimeOffset(2026, 10, 30, 10, 5, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 30)));

        var today = IcsParser.UpcomingToday(events, now);

        Assert.Equal(["Daily del equipo", "Almuerzo, con Pipe"], today.Select(e => e.Title));
    }

    [Fact]
    public void Parse_GarbageInput_ReturnsEmpty()
    {
        Assert.Empty(IcsParser.Parse("not a calendar"));
    }
}
