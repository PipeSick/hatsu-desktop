namespace CallProbe.Logic;

/// <summary>Qué se puede concluir de un resultado. «Inconclusive» NO es «bloqueado»: significa que la prueba no se hizo en las condiciones del producto.</summary>
public enum Verdict { Inconclusive, Promising, BlockedUnderTheseConditions, RegistrationWorks }

public sealed record Interpretation(Verdict Verdict, string Explanation, string NextStep);

/// <summary>
/// Interpreta el resultado de <c>PhoneLineTransportDevice.RequestAccessAsync()</c> y de <c>IsRegistered()</c> teniendo en cuenta la identidad de paquete.
/// Existe por una razón concreta: una consola sin paquete obtiene <c>DeniedBySystem</c> aunque una app empaquetada funcione, así que ese resultado, solo, no prueba ningún bloqueo.
/// </summary>
public static class AccessInterpreter
{
    /// <param name="packaged">¿El proceso tiene identidad de paquete (MSIX o paquete disperso)?</param>
    /// <param name="accessStatus">Nombre de <c>DeviceAccessStatus</c>: Allowed, DeniedBySystem, DeniedByUser, Unspecified.</param>
    /// <param name="registeredAfterRegister">null = no se intentó <c>RegisterApp()</c>; true/false = valor de <c>IsRegistered()</c> después de intentarlo.</param>
    public static Interpretation Interpret(bool packaged, string accessStatus, bool? registeredAfterRegister, string? signatureKind = null)
    {
        bool sideload = packaged && signatureKind is "Developer" or "Enterprise" or "None";
        switch (accessStatus)
        {
            case "DeniedBySystem" when !packaged:
                return new(Verdict.Inconclusive,
                    "Sin identidad de paquete, Windows no puede conceder la capacidad restringida phoneLineTransportManagement: este resultado es el esperado y NO demuestra que una app empaquetada sea rechazada (MyPhone issue #7; Microsoft Q&A «In WPF … always throw denied by system»).",
                    "Repetir la misma prueba desde el paquete de investigación (Register-DevPackage.ps1) y comparar.");
            case "DeniedBySystem":
                return new(Verdict.BlockedUnderTheseConditions,
                    "Con identidad de paquete y la capacidad declarada, Windows sigue negando el acceso. Es el patrón de MyPhone issue #26 (Windows 11 22H2+), de Microsoft Q&A 1663702/1803696 y de Klangbruecke FINDINGS §15.1."
                    + (sideload ? " La firma del paquete es de DESARROLLO/carga lateral: esto prueba «denegado en carga lateral», NO que una app firmada por la Store sea rechazada." : ""),
                    "Descartar causas locales antes de concluir: «Phone calls» en Privacidad, otra app registrada (Phone Link), vínculo duplicado clásico/LE, reiniciar el iPhone y repetir. Después, prueba de control con una app de la Store (p. ej. Thy Phone) para separar «carga lateral» de «compilación/iPhone».");
            case "DeniedByUser":
                return new(Verdict.Inconclusive, "El usuario (o su configuración de privacidad) negó el acceso a llamadas.", "Revisar Configuración › Privacidad y seguridad › Llamadas telefónicas y repetir.");
            case "Allowed" when registeredAfterRegister is null:
                return new(Verdict.Promising, packaged ? "Acceso concedido con identidad de paquete. Falta probar el registro (RegisterApp), que es donde se ha visto fallar a otros." : "Acceso concedido SIN paquete: contradice los reportes; conviene repetirlo antes de fiarse.", "Con permiso explícito: RegisterApp() y comprobar IsRegistered().");
            case "Allowed" when registeredAfterRegister == false:
                return new(Verdict.BlockedUnderTheseConditions,
                    "Acceso concedido pero RegisterApp() no deja la app registrada: es el patrón de Microsoft Q&A 6041632 (app empaquetada, Windows 11 compilación 26300).",
                    "Comprobar si otra app tiene el transporte (desvincular y volver a emparejar), el valor de build y repetir tras actualizar Windows.");
            case "Allowed":
                return new(Verdict.RegistrationWorks, "Acceso concedido y app registrada. Esto NO demuestra contestar ni audio: solo que Windows acepta a la app como proveedor de llamadas.", "Probar ConnectAsync y observar una llamada real sin responderla; después, con permiso, responder.");
            default:
                return new(Verdict.Inconclusive, $"Estado de acceso no esperado: {accessStatus}.", "Repetir y adjuntar el informe completo.");
        }
    }
}

/// <summary>Resultado de una llamada real de prueba (etapa 5). Una afirmación «verificado» exige TODAS las casillas, cada una observada, no inferida.</summary>
public sealed record CallScorecard(bool Detected, bool Answered, bool IncomingAudioHeardOnPc, bool OutgoingAudioHeardByCaller, bool Ended, bool RecoveredFromError)
{
    public IReadOnlyList<string> Missing()
    {
        var m = new List<string>();
        if (!Detected) m.Add("detección de la llamada");
        if (!Answered) m.Add("contestar desde Windows");
        if (!IncomingAudioHeardOnPc) m.Add("audio entrante en el PC");
        if (!OutgoingAudioHeardByCaller) m.Add("audio saliente (el interlocutor oye al usuario)");
        if (!Ended) m.Add("colgar desde Windows");
        if (!RecoveredFromError) m.Add("recuperación ante errores");
        return m;
    }

    public string Summary()
    {
        var m = Missing();
        if (m.Count == 0) return "VERIFICADO EN HARDWARE: integración completa (detección, respuesta, audio en ambos sentidos, fin y recuperación).";
        if (IncomingAudioHeardOnPc ^ OutgoingAudioHeardByCaller) return "PARCIAL: audio en un solo sentido. Falta: " + string.Join(", ", m) + ".";
        if (Answered && !(IncomingAudioHeardOnPc && OutgoingAudioHeardByCaller)) return "PARCIAL: control sin audio bidireccional. Falta: " + string.Join(", ", m) + ".";
        return "SIN VERIFICAR. Falta: " + string.Join(", ", m) + ".";
    }
}

/// <summary>Las acciones que tocan una llamada real exigen escribir una palabra exacta: ni un «s» ni un Enter bastan.</summary>
public static class ConfirmationGate
{
    public const string Answer = "CONTESTAR", Reject = "RECHAZAR", HangUp = "COLGAR", Register = "REGISTRAR";
    public static bool IsConfirmed(string? typed, string expected) => string.Equals(typed?.Trim(), expected, StringComparison.Ordinal);
}
