namespace WinMac.Dock;

/// <summary>Un Dock por pantalla: qué monitores necesitan uno nuevo. Puro (se prueba en Linux). Un Dock cuyo monitor desaparece se cierra solo.</summary>
public static class DockFleet
{
    public static string Key((int Left, int Top, int Right, int Bottom) m) => $"{m.Left},{m.Top},{m.Right},{m.Bottom}";

    /// <summary>Monitores (sin repetir: el espejo de otro no lleva un Dock aparte) que todavía no tienen Dock, en el orden en que llegan.</summary>
    public static IReadOnlyList<(int Left, int Top, int Right, int Bottom)> ToStart(IEnumerable<(int Left, int Top, int Right, int Bottom)> monitors, IReadOnlyCollection<string> running)
    {
        var seen = new HashSet<string>(running); var list = new List<(int, int, int, int)>();
        foreach (var m in monitors)
        {
            if (m.Right <= m.Left || m.Bottom <= m.Top) continue;
            if (seen.Add(Key(m))) list.Add(m);
        }
        return list;
    }
}
