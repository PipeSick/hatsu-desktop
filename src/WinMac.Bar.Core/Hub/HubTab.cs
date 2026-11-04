namespace WinMac.Bar.Hub;

public enum HubTab { Sound, System, Network, Disks }

public sealed record TabItem(string Id, string Glyph, string Label);

/// <summary>
/// Identidad visible de la aplicación (nombre y logotipo), DESACOPLADA del funcionamiento del panel: cambiarla es tocar este registro (y el dibujo del logotipo con esa clave), no la lógica.
/// El nombre y el logotipo definitivos todavía pueden cambiar.
/// </summary>
public sealed record AppIdentity(string Name, string LogoKey)
{
    public static AppIdentity Current { get; } = new("Hatsu Desktop", "planet");
}

public static class HubTabs
{
    /// <summary>Las pestañas de ahora; añadir otra es añadir un elemento aquí y su contenido en <see cref="PanelBuilder"/>.</summary>
    public static readonly IReadOnlyList<TabItem> All = [new("Sound", "sound", "Sonido"), new("System", "cpu", "Sistema"), new("Network", "globe", "Red"), new("Disks", "disk", "Discos")];
    public static int IndexOf(HubTab t) => Math.Max(0, All.ToList().FindIndex(x => x.Id == t.ToString()));
    public static bool TryParse(string? id, out HubTab tab) => Enum.TryParse(id, false, out tab) && Enum.IsDefined(tab);
}
