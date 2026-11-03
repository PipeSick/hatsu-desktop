namespace WinMac.Host;

/// <summary>
/// Migración única de la beta (docs/36, WM-020): el Global Menu duplicaba Archivo/Editar/Ver/Ayuda de la app activa en la barra y abría sus desplegables dentro de la
/// ventana original. Desde la versión 3 de <c>winmac.json</c> queda desactivado aunque una configuración anterior lo tuviera activado; sigue en Ajustes como opción
/// avanzada para quien lo quiera. Se aplica una sola vez (sube la versión); no toca nada más.
/// </summary>
public static class BetaMigration
{
    public const int Version = 3;

    public static (HostConfig Config, bool Changed) Apply(HostConfig c)
    {
        if (c.Version >= Version) return (c, false);
        var bar = c.Bar is { } b ? b with { AppMenus = false } : null;
        return (c with { Bar = bar, Version = Version }, true);
    }
}
