using WinMac.Bar;

namespace WinMac.Host;

/// <summary>
/// Importación ÚNICA y no destructiva de los ajustes de la Barra: la primera vez que WinMac arranca con la configuración central, los valores de <c>bar.json</c> (el archivo propio de la Barra)
/// se copian a <c>winmac.json</c>. El archivo original NO se modifica ni se borra, y si el central ya tiene ajustes de la Barra no se pisan. Un <c>bar.json</c> corrupto cae a los valores por defecto.
/// </summary>
public static class BarMigration
{
    /// <returns>La configuración resultante (la misma instancia si no hubo nada que importar).</returns>
    public static HostConfig ImportOnce(HostConfig cfg, string barJsonPath, string configPath, Action<string> log)
    {
        if (cfg.Bar is not null || !File.Exists(barJsonPath)) return cfg;
        try
        {
            var imported = BarConfig.LoadOrDefault(barJsonPath).Sanitize();
            var next = cfg with { Bar = imported };
            HostConfig.Save(configPath, next); log("[host] ajustes de la Barra importados de " + barJsonPath + " (el archivo original no se modificó)"); return next;
        }
        catch (Exception e) { log("[host] no se pudo importar los ajustes de la Barra: " + e.Message); return cfg; }
    }
}
