

namespace WinMac.Bar.Menus;

/// <summary>De dónde salen los menús de la app activa.</summary>
public enum AppMenuSource { None, Win32, Automation }

/// <summary>
/// Una entrada de un menú de otra app tal como se leyó (nunca inventada). <see cref="Command"/>: identificador de la orden Win32 (se ejecuta con WM_COMMAND), 0 si es un submenú.
/// <see cref="Children"/>: submenú (null si es una orden). Texto y atajo vienen del propio menú («&amp;Abrir\tCtrl+A»).
/// </summary>
public sealed record AppMenuEntry(string Text, int Command, bool Enabled = true, bool Checked = false, bool Separator = false, IReadOnlyList<AppMenuEntry>? Children = null, string? Shortcut = null)
{
    public static AppMenuEntry Sep { get; } = new("", 0, Separator: true);

    /// <summary>«&amp;Abrir…\tCtrl+A» → («Abrir…», «Ctrl+A»). «&amp;&amp;» es un «&amp;» literal.</summary>
    public static (string Label, string? Shortcut) ParseLabel(string raw)
    {
        int tab = raw.IndexOf('\t');
        string label = tab >= 0 ? raw[..tab] : raw, sc = tab >= 0 ? raw[(tab + 1)..].Trim() : "";
        label = label.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
        return (label, sc.Length == 0 ? null : sc);
    }
}

/// <summary>
/// Qué barra de menús accesible (UI Automation) es la de la APP. Windows expone también el menú de sistema de la ventana (el de la barra de título: Restaurar, Mover,
/// Cerrar…) como una <c>MenuBar</c> con el identificador estándar <c>SystemMenuBar</c>; no es un menú de la app y no debe aparecer en la barra (se veía «Terminal  System»).
/// Solo se descarta ese identificador: cualquier otra barra de menús se conserva.
/// </summary>
public static class AppMenuFilter
{
    public const string SystemMenuBarId = "SystemMenuBar";

    public static bool IsAppMenuBar(string? automationId) => !string.Equals(automationId?.Trim(), SystemMenuBarId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Menús de la app activa: su nombre, de dónde salen y los de nivel superior (con su árbol si se pudo leer).</summary>
public sealed record AppMenus(AppMenuSource Source, IReadOnlyList<AppMenuEntry> Tops)
{
    public static AppMenus Empty { get; } = new(AppMenuSource.None, []);
    public IReadOnlyList<string>? Labels => Tops.Count == 0 ? null : Tops.Select(t => t.Text).ToList();
}

/// <summary>
/// Desplegable de un menú de la app con el aspecto de los menús de WinMac (el de la manzana): opciones con su atajo a la derecha, ✓ si están marcadas, atenuadas si están
/// deshabilitadas, separadores y submenús con «›» que se abren dentro del mismo panel (con «‹» para volver). Puro.
/// </summary>
public static class AppMenuPanel
{
    public const double Width = 280;

    /// <param name="path">Índices desde el menú de nivel superior hasta el submenú abierto.</param>
    public static PanelModel Build(AppMenuEntry top, IReadOnlyList<int> path)
    {
        var (node, title) = Resolve(top, path);
        var w = new List<Widget>();
        if (path.Count > 0) w.Add(new Widget(WidgetKind.Header, "back", title));
        var items = node.Children ?? [];
        for (int i = 0; i < items.Count; i++)
        {
            var e = items[i];
            if (e.Separator) { if (w.Count > 0 && w[^1].Kind != WidgetKind.Separator && i < items.Count - 1) w.Add(new Widget(WidgetKind.Separator, "sep" + i)); continue; }
            string? sub = e.Children is not null ? "›" : e.Shortcut;
            w.Add(new Widget(WidgetKind.MenuItem, "am:" + i, e.Text, sub, On: e.Checked, Enabled: e.Enabled && (e.Children is null || e.Children.Count > 0)));
        }

        if (items.Count == 0) w.Add(new Widget(WidgetKind.MenuItem, "am:none", "(vacío)", Enabled: false));
        return new PanelModel(PanelKind.AppMenu, title, w, Width, Pad: 5, Spacing: 0);
    }

    public static (AppMenuEntry Node, string Title) Resolve(AppMenuEntry top, IReadOnlyList<int> path)
    {
        var node = top;
        foreach (int i in path) { if (node.Children is null || i < 0 || i >= node.Children.Count || node.Children[i].Children is null) break; node = node.Children[i]; }
        return (node, node.Text);
    }

    /// <summary>Clic en «am:i»: orden a ejecutar (Command &gt; 0), submenú que abrir (nueva ruta) o nada (deshabilitada).</summary>
    public static (int Command, IReadOnlyList<int>? OpenPath) Click(AppMenuEntry top, IReadOnlyList<int> path, string id)
    {
        if (!id.StartsWith("am:", StringComparison.Ordinal) || !int.TryParse(id[3..], out int i)) return (0, null);
        var (node, _) = Resolve(top, path);
        if (node.Children is null || i < 0 || i >= node.Children.Count) return (0, null);
        var e = node.Children[i];
        if (!e.Enabled || e.Separator) return (0, null);
        if (e.Children is not null) return (0, path.Append(i).ToList());
        return (e.Command, null);
    }
}
