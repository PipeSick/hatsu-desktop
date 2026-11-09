namespace WinMac.Bar.Peripherals;

public static class PeripheralSettings
{
    public const int MinThreshold = 5, MaxThreshold = 50, DefaultThreshold = 20;

    public static int ClampThreshold(int t) => Math.Clamp(t, MinThreshold, MaxThreshold);

    /// <summary>Ids ocultos guardados como «a|b» (los ids llevan «|» dentro: se separan por «\n»).</summary>
    public static HashSet<string> ParseHidden(string? s) =>
        new((s ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length is > 0 and <= 200).Take(100), StringComparer.Ordinal);

    public static string SerializeHidden(IEnumerable<string> ids) => string.Join('\n', ids.Distinct(StringComparer.Ordinal).Take(100));

    public static string Hide(string? current, string id) { var h = ParseHidden(current); h.Add(id); return SerializeHidden(h); }
    public static string Unhide(string? current, string id) { var h = ParseHidden(current); h.Remove(id); return SerializeHidden(h); }
}
