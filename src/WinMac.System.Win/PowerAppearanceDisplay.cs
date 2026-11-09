using Microsoft.Win32;

namespace WinMac.SystemControl.Win;

/// <summary>Modo de energía de Windows 11 (barra «Modo de energía»): <c>PowerGet/SetActiveOverlayScheme</c> de powrprof. La documentación de estas funciones es parcial: si faltan, el control se deshabilita con el motivo.</summary>
public sealed class PowerService : IPowerService
{
    private static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a"), Balanced = Guid.Empty,
        Performance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f"), Best = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private static Guid GuidOf(PowerMode m) => m switch { PowerMode.BestEfficiency => Efficiency, PowerMode.Performance => Performance, PowerMode.BestPerformance => Best, _ => Balanced };

    public PowerModeState ReadMode()
    {
        try
        {
            if (Native.PowerGetEffectiveOverlayScheme(out var g) != 0) return PowerModeState.None("Este equipo no expone el modo de energía");
            foreach (var m in Enum.GetValues<PowerMode>()) if (GuidOf(m) == g) return new PowerModeState(true, m);
            return new PowerModeState(true, PowerMode.Balanced);
        }
        catch (EntryPointNotFoundException) { return PowerModeState.None("Esta versión de Windows no ofrece modos de energía"); }
        catch (DllNotFoundException) { return PowerModeState.None("Esta versión de Windows no ofrece modos de energía"); }
        catch (Exception e) { return PowerModeState.None("Energía: " + e.GetType().Name); }
    }

    public ServiceResult SetMode(PowerMode mode) => ServiceResult.Guard(() =>
    {
        uint rc = Native.PowerSetActiveOverlayScheme(GuidOf(mode));
        if (rc == 0) return ServiceResult.Ok;
        return rc == 5 ? ServiceResult.Denied("Windows no permite cambiar el modo de energía a este usuario") : ServiceResult.Failed($"Modo de energía: error {rc}");
    }, "Modo de energía");
}

/// <summary>
/// Claro/oscuro por el registro del USUARIO actual (sin administrador): solo escribe <c>AppsUseLightTheme</c> y <c>SystemUsesLightTheme</c> en <c>HKCU\...\Themes\Personalize</c>, conserva
/// cualquier otro valor y recuerda los anteriores para restaurarlos. No es una API oficial, pero es el mecanismo estable que usa la propia Configuración. Avisa al sistema con <c>WM_SETTINGCHANGE</c>.
/// </summary>
public sealed class AppearanceService : IAppearanceService
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private (int? Apps, int? System)? _previous;

    public AppearanceState Read()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            if (k is null) return AppearanceState.None("Windows no tiene la clave de personalización");
            bool? system = k.GetValue("SystemUsesLightTheme") is int sv ? sv == 0 : null;
            return new AppearanceState(true, k.GetValue("AppsUseLightTheme") is int v && v == 0, SystemDark: system);
        }
        catch (Exception e) { return AppearanceState.None("Apariencia: " + e.GetType().Name); }
    }

    /// <summary>Valores que había antes del primer cambio hecho por WinMac (para <see cref="Restore"/>).</summary>
    public bool CanRestore => _previous is not null;

    public ServiceResult SetDark(bool dark) => ServiceResult.Guard(() =>
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        _previous ??= (k.GetValue("AppsUseLightTheme") as int?, k.GetValue("SystemUsesLightTheme") as int?);
        int v = dark ? 0 : 1;
        k.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord); k.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
        Native.SendMessageTimeoutW(new IntPtr(0xFFFF) /* HWND_BROADCAST */, 0x1A /* WM_SETTINGCHANGE */, UIntPtr.Zero, "ImmersiveColorSet", 0x2 /* SMTO_ABORTIFHUNG */, 200, out _);
        return ServiceResult.Ok;
    }, "Apariencia");

    public ServiceResult Restore() => ServiceResult.Guard(() =>
    {
        if (_previous is not { } p) return ServiceResult.Unavailable("No hay valores anteriores que restaurar");
        using var k = Registry.CurrentUser.CreateSubKey(Key, writable: true);
        if (p.Apps is int a) k.SetValue("AppsUseLightTheme", a, RegistryValueKind.DWord); else k.DeleteValue("AppsUseLightTheme", false);
        if (p.System is int s) k.SetValue("SystemUsesLightTheme", s, RegistryValueKind.DWord); else k.DeleteValue("SystemUsesLightTheme", false);
        Native.SendMessageTimeoutW(new IntPtr(0xFFFF), 0x1A, UIntPtr.Zero, "ImmersiveColorSet", 0x2, 200, out _);
        _previous = null; return ServiceResult.Ok;
    }, "Apariencia");
}

/// <summary>Modo de pantallas (duplicar / extender / solo una) con <c>SetDisplayConfig</c> (documentada), la misma función que usa Win+P.</summary>
public sealed class DisplayService : IDisplayService
{
    public ServiceResult SetTopology(DisplayTopology topology) => ServiceResult.Guard(() =>
    {
        uint flag = topology switch { DisplayTopology.InternalOnly => 0x1u, DisplayTopology.Duplicate => 0x2u, DisplayTopology.Extend => 0x4u, _ => 0x8u };
        int rc = Native.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x80 /* SDC_APPLY */ | flag);
        return rc switch { 0 => ServiceResult.Ok, 5 => ServiceResult.Denied("Windows denegó el cambio de pantallas"), 87 => ServiceResult.Unavailable("Este modo no está disponible con las pantallas conectadas"), _ => ServiceResult.Failed($"Pantallas: error {rc}") };
    }, "Pantallas");
}
