namespace WinMac.Host;

/// <summary>Por qué un módulo no está en marcha, con la causa que se pudo ESTABLECER (no la que se sospecha). Se distingue «desactivado» (no es un fallo) de lo demás.</summary>
public enum FailureKind
{
    /// <summary>No se pudo determinar la causa.</summary>
    Unknown,
    /// <summary>El ejecutable no existe.</summary>
    Missing,
    /// <summary>Windows rechazó ejecutar el archivo por una directiva de seguridad (códigos 1260, 4551, 786, 577 o una DLL bloqueada). NO dice cuál: puede ser Smart App Control, Control de aplicaciones, AppLocker o una directiva de empresa.</summary>
    PolicyBlocked,
    AntivirusBlocked,
    AccessDenied,
    BadImage,
    ExitedUnexpectedly,
    NotResponding,
}

/// <param name="Technical">Detalle técnico (código de Windows, ruta): para «Detalles técnicos», el registro y el informe de diagnóstico.</param>
public sealed record ModuleFailure(FailureKind Kind, string Technical, int? Win32Code = null, int? ExitCode = null, int Attempts = 1)
{
    /// <summary>Causa en una frase corta y neutra; nunca nombra una protección concreta (Smart App Control) porque el código de error no lo demuestra.</summary>
    public string Reason => Kind switch
    {
        FailureKind.Missing => "No se encontró el programa",
        FailureKind.PolicyBlocked => "Windows lo bloqueó mediante una directiva de seguridad",
        FailureKind.AntivirusBlocked => "El antivirus lo bloqueó",
        FailureKind.AccessDenied => "Sin permiso para ejecutarlo",
        FailureKind.BadImage => "El archivo no es un programa válido",
        FailureKind.ExitedUnexpectedly => "Se cerró inesperadamente",
        FailureKind.NotResponding => "No respondió al iniciar",
        _ => "No se pudo iniciar",
    };

    /// <summary>Versión muy corta para la fila de la bandeja.</summary>
    public string Short => Kind switch
    {
        FailureKind.Missing => "No se encontró el programa", FailureKind.PolicyBlocked => "Bloqueado por una directiva de Windows", FailureKind.AntivirusBlocked => "Bloqueado por el antivirus",
        FailureKind.AccessDenied => "Sin permiso para ejecutarlo", FailureKind.BadImage => "Archivo no válido", FailureKind.ExitedUnexpectedly => "Se cerraba solo", FailureKind.NotResponding => "No respondía", _ => "No se pudo iniciar",
    };

    /// <summary>Qué significa y qué NO hace Hatsu Desktop al respecto.</summary>
    public string Advice => Kind switch
    {
        FailureKind.PolicyBlocked => "Windows rechazó ejecutar este archivo por una directiva de seguridad (por ejemplo Smart App Control, Control de aplicaciones, AppLocker o una directiva de tu organización; el código de error no dice cuál). Hatsu Desktop no cambia ni evita esa protección. Para saber qué archivo se bloqueó, genera el informe de diagnóstico.",
        FailureKind.AntivirusBlocked => "El antivirus bloqueó o eliminó el archivo. Revisa el historial de protección de Seguridad de Windows; Hatsu Desktop no añade exclusiones.",
        FailureKind.AccessDenied => "Windows negó el permiso para ejecutar el archivo (permisos de la carpeta o el antivirus).",
        FailureKind.Missing => "El archivo no está en la carpeta de instalación. Reinstala Hatsu Desktop o repara la instalación.",
        FailureKind.BadImage => "El archivo está dañado o no es un ejecutable de esta versión de Windows. Reinstala Hatsu Desktop.",
        FailureKind.ExitedUnexpectedly => "El programa arrancó pero terminó solo. Se reintentó con espera creciente y, tras varios intentos, se dejó de insistir.",
        FailureKind.NotResponding => "El programa arrancó pero no llegó a responder a Hatsu Desktop. Se reintentó y, tras varios intentos, se dejó de insistir.",
        _ => "No se pudo determinar la causa. El informe de diagnóstico incluye el detalle técnico.",
    };

    public override string ToString() => $"{Kind}: {Technical}";
}
