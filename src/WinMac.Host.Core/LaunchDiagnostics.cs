using System.ComponentModel;

namespace WinMac.Host;

/// <summary>
/// WM-036: explica por qué no arrancó un módulo, con el código real de Windows (sin suponer la causa). Puro: se prueba sin Windows.
/// Smart App Control y Control de aplicaciones (WDAC) bloquean archivos sin firma o sin reputación con códigos concretos; Defender, con otros. Un bloqueo al cargar una
/// DLL del paquete aparece como código de salida del proceso, no como error al lanzarlo.
/// </summary>
public static class LaunchDiagnostics
{
    public const int ErrorFileNotFound = 2, ErrorAccessDenied = 5, ErrorVirusInfected = 225, ErrorVirusDeleted = 226, ErrorInvalidImageHash = 577,
        ErrorAccessDisabledByPolicy = 1260, ErrorAppControlBlocked = 4551, ErrorAppControlAudit = 4552;

    /// <summary>¿Un bloqueo de seguridad o un fallo pasajero? Esos se reintentan con espera creciente (antes, el módulo quedaba «Fallado» hasta reiniciar WinMac).
    /// Un ejecutable que no existe no se reintenta.</summary>
    public static bool Retryable(Exception e) => e is not FileNotFoundException && !(e is Win32Exception w && w.NativeErrorCode == ErrorFileNotFound);

    public static string Explain(Exception e)
    {
        if (e is FileNotFoundException) return "no existe el ejecutable (" + e.Message + ")";
        if (e is not Win32Exception w) return e.GetType().Name + ": " + e.Message;
        string why = w.NativeErrorCode switch
        {
            ErrorAppControlBlocked or ErrorAppControlAudit => "una directiva de Control de aplicaciones bloqueó el archivo (Smart App Control o WDAC: suele pasar con ejecutables sin firma o sin reputación todavía)",
            ErrorInvalidImageHash => "Windows no pudo validar la integridad o firma del archivo (Integridad de código)",
            ErrorAccessDisabledByPolicy => "una directiva del sistema impide ejecutar este programa (AppLocker, Control de aplicaciones o directiva de grupo)",
            ErrorVirusInfected or ErrorVirusDeleted => "el antivirus bloqueó o eliminó el archivo (revisa el historial de protección de Seguridad de Windows)",
            ErrorAccessDenied => "acceso denegado (permisos de la carpeta o el antivirus)",
            ErrorBadExeFormat => "el archivo no es un programa válido para esta versión de Windows",
            ErrorFileNotFound => "no existe el ejecutable",
            _ => w.Message,
        };
        return $"{why} [error {w.NativeErrorCode}]";
    }

    public const int ErrorBadExeFormat = 193, ErrorElevationRequired = 740, ErrorPathNotFound = 3, ErrorAccessDisabledNoSaferUi = 786;

    /// <summary>Clasifica un error AL LANZAR un proceso por su código de Windows (sin suponer qué protección concreta lo causó).</summary>
    public static ModuleFailure Classify(Exception e, int attempts = 1)
    {
        string tech = Explain(e);
        if (e is FileNotFoundException) return new(FailureKind.Missing, tech, ErrorFileNotFound, null, attempts);
        if (e is not Win32Exception w) return new(FailureKind.Unknown, tech, null, null, attempts);
        var kind = w.NativeErrorCode switch
        {
            ErrorFileNotFound or ErrorPathNotFound => FailureKind.Missing,
            ErrorAppControlBlocked or ErrorAppControlAudit or ErrorAccessDisabledByPolicy or ErrorAccessDisabledNoSaferUi or ErrorInvalidImageHash => FailureKind.PolicyBlocked,
            ErrorVirusInfected or ErrorVirusDeleted => FailureKind.AntivirusBlocked,
            ErrorAccessDenied or ErrorElevationRequired => FailureKind.AccessDenied,
            ErrorBadExeFormat => FailureKind.BadImage,
            _ => FailureKind.Unknown,
        };
        return new(kind, tech, w.NativeErrorCode, null, attempts);
    }

    /// <summary>Clasifica un módulo que ARRANCÓ y terminó solo, por su código de salida.</summary>
    public static ModuleFailure ClassifyExit(int code, int attempts = 1) =>
        new(unchecked((uint)code) == 0xC0000428 ? FailureKind.PolicyBlocked : FailureKind.ExitedUnexpectedly, ExitText(code), null, code, attempts);

    /// <summary>Texto del código de salida de un módulo que terminó sin avisar. Los NTSTATUS de carga (DLL bloqueada o ausente) se nombran.</summary>
    public static string ExitText(int code) => unchecked((uint)code) switch
    {
        0xC0000428 => $"código 0x{(uint)code:X8}: Windows bloqueó una biblioteca del paquete (firma o Control de aplicaciones)",
        0xC0000135 => $"código 0x{(uint)code:X8}: falta una biblioteca del paquete",
        0xC0000142 => $"código 0x{(uint)code:X8}: una biblioteca no pudo iniciarse",
        0xC0000005 => $"código 0x{(uint)code:X8}: fallo interno del módulo (acceso a memoria)",
        0xC0000409 => $"código 0x{(uint)code:X8}: fallo interno del módulo (excepción no controlada)",
        0xE0434352 => $"código 0x{(uint)code:X8}: excepción .NET no controlada",
        _ => code < 0 || code > 0xFFFF ? $"código 0x{(uint)code:X8}" : $"código {code}",
    };
}
