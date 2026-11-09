using Microsoft.Win32;

namespace WinMac.Host.App;

/// <summary>Inicio automático con UNA sola clave por usuario (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>), totalmente reversible. No toca nada más.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultName = "WinMac";

    /// <summary>Identificador de la tarea de inicio en AppxManifest.xml (MSIX).</summary>
    public const string StartupTaskId = "HatsuDesktopStartup";

    /// <summary>Con identidad de paquete, Windows ignora HKCU\Run: el inicio automático es una <c>StartupTask</c> del manifiesto (el usuario puede desactivarla en Configuración › Aplicaciones › Inicio y Hatsu lo respeta).</summary>
    private static Windows.ApplicationModel.StartupTask? PackagedTask()
    {
        try { return Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult(); } catch (Exception) { return null; }
    }

    private static string PackagedValue(string exePath) => $"\"{exePath}\" --background";

    public static void Install(string exePath, string name = DefaultName)
    {
        if (PackageIdentity.IsPackaged)
        {
            var t = PackagedTask() ?? throw new InvalidOperationException("El paquete no declara la tarea de inicio " + StartupTaskId);
            var st = t.State;
            if (st is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy) return;
            st = t.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            if (st is not (Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy))
                throw new InvalidOperationException("Windows no permitió activar el inicio automático (" + st + "). Puedes activarlo en Configuración › Aplicaciones › Inicio.");
            return;
        }
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        k.SetValue(name, $"\"{exePath}\" --background", RegistryValueKind.String);
    }

    public static bool Remove(string name = DefaultName)
    {
        if (PackageIdentity.IsPackaged)
        {
            var t = PackagedTask(); if (t is null || t.State is not Windows.ApplicationModel.StartupTaskState.Enabled) return false;
            t.Disable(); return true;
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k?.GetValue(name) is null) return false;
        k.DeleteValue(name, throwOnMissingValue: false); return true;
    }

    /// <summary>Hatsu Desktop: repara un inicio automático huérfano (apunta a una carpeta que ya no existe) sin tocar el de otra instalación viva. Ver <see cref="WinMac.Host.AutostartMigration"/>.</summary>
    public static string? Heal(string currentExe, string name = DefaultName)
    {
        if (PackageIdentity.IsPackaged) return null;   // la StartupTask siempre apunta al paquete instalado: nada que reparar
        try
        {
            var plan = WinMac.Host.AutostartMigration.Plan(Current(name), currentExe, File.Exists);
            if (plan is null) return null;
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true); k.SetValue(name, plan, RegistryValueKind.String); return plan;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Estado del inicio automático frente a ESTA instalación (ver <see cref="WinMac.Host.AutostartMigration"/>).</summary>
    public static WinMac.Host.AutostartAssessment Assess(string currentExe, string name = DefaultName) => WinMac.Host.AutostartMigration.Assess(Current(name), currentExe, File.Exists);

    /// <summary>Ruta de OTRA instalación viva que Windows inicia hoy (para ofrecer cambiarla), o null.</summary>
    public static string? ForeignPath(string currentExe, string name = DefaultName) => Assess(currentExe, name) is { State: WinMac.Host.AutostartState.OtherInstall } a ? a.StoredExe : null;

    /// <summary>Adopción explícita: hace que el inicio automático apunte a esta instalación, solo si ya existía un valor. Devuelve el valor escrito o null.</summary>
    public static string? Adopt(string currentExe, string name = DefaultName)
    {
        if (PackageIdentity.IsPackaged) return null;
        try
        {
            var plan = WinMac.Host.AutostartMigration.PlanAdopt(Current(name), currentExe, File.Exists); if (plan is null) return null;
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true); k.SetValue(name, plan, RegistryValueKind.String); return plan;
        }
        catch (Exception) { return null; }
    }

    public static string? Current(string name = DefaultName)
    {
        if (PackageIdentity.IsPackaged)
        {
            var t = PackagedTask(); var exe = Environment.ProcessPath ?? "HatsuDesktop.exe";
            return t is { State: Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy } ? PackagedValue(exe) : null;
        }
        using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(name) as string;
    }
}
