namespace CallProbe.Logic;

/// <summary>Estado leído (solo lectura) justo antes de la etapa 3 y justo después de revertirla: sirve para decidir si se puede probar y para demostrar que se dejó todo igual.</summary>
public sealed record Stage3Snapshot(bool Packaged, string? SignatureKind, bool? IsRegistered, bool CallActive, bool CallIncoming, bool PhoneLinkRunning, bool? ClassicConnected,
    string AccessStatus, string? CommRenderHash, string? CommCaptureHash);

public static class SnapshotComparer
{
    /// <summary>Qué cambió entre el estado inicial y el final. Lista vacía = se dejó todo como estaba.</summary>
    public static IReadOnlyList<string> Differences(Stage3Snapshot before, Stage3Snapshot after)
    {
        var d = new List<string>();
        if (before.IsRegistered != after.IsRegistered) d.Add($"IsRegistered(): {before.IsRegistered} → {after.IsRegistered}");
        if (before.CallActive != after.CallActive) d.Add($"llamada activa: {before.CallActive} → {after.CallActive}");
        if (before.CallIncoming != after.CallIncoming) d.Add($"llamada entrante: {before.CallIncoming} → {after.CallIncoming}");
        if (before.ClassicConnected != after.ClassicConnected) d.Add($"conexión Bluetooth Classic: {before.ClassicConnected} → {after.ClassicConnected}");
        if (before.CommRenderHash != after.CommRenderHash) d.Add("salida de audio de comunicaciones predeterminada: cambió");
        if (before.CommCaptureHash != after.CommCaptureHash) d.Add("micrófono de comunicaciones predeterminado: cambió");
        if (before.PhoneLinkRunning != after.PhoneLinkRunning) d.Add($"Phone Link en ejecución: {before.PhoneLinkRunning} → {after.PhoneLinkRunning}");
        return d;
    }
}

public sealed record Stage3Options(bool Connect = false, bool Keep = false, bool AllowPhoneLink = false, bool AllowDisconnected = false, int HoldSeconds = 10);

public sealed record Stage3Check(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings) { public bool CanProceed => Blockers.Count == 0; }

/// <summary>Precondiciones de la prueba de registro. Cada bloqueo existe por una razón concreta (ver el informe, §Etapa 3).</summary>
public static class Stage3Preconditions
{
    public static Stage3Check Evaluate(Stage3Snapshot s, Stage3Options o)
    {
        var b = new List<string>(); var w = new List<string>();
        if (!s.Packaged) b.Add("El proceso no tiene identidad de paquete: RegisterApp no puede funcionar y la prueba no diría nada. Ejecuta con Run-Probe.ps1 (con paquete).");
        if (s.AccessStatus != "Allowed") b.Add($"RequestAccessAsync devolvió {s.AccessStatus}, no Allowed: no se intenta el registro.");
        if (s.CallActive || s.CallIncoming) b.Add("Hay una llamada activa o entrante: no se toca el estado telefónico durante una llamada real.");
        if (s.PhoneLinkRunning && !o.AllowPhoneLink) b.Add("Phone Link está en ejecución y podría tener el rol de llamadas (hay reportes de que solo una app lo conserva). Ciérralo; --allow-phone-link lo permite bajo tu responsabilidad.");
        if (s.IsRegistered == true) b.Add("IsRegistered() ya es True antes de empezar: alguien (esta u otra app) ya lo registró. No se toca; usa 'stage3 status' y decide.");
        if (s.IsRegistered is null) b.Add("No se pudo leer IsRegistered(): no se puede garantizar la reversión.");
        if (s.ClassicConnected != true && !o.AllowDisconnected) b.Add("El iPhone no figura conectado por Bluetooth Classic. Conéctalo (desbloqueado y cerca) para que un resultado False tenga significado; --allow-disconnected lo omite.");
        if (o.Connect) w.Add("--connect abre además la conexión HFP con el iPhone (efecto adicional): exigirá una segunda confirmación.");
        if (o.Keep) w.Add("--keep deja el registro puesto al terminar: tendrás que revertirlo tú con 'stage3 unregister'.");
        if (o.HoldSeconds is < 0 or > 60) b.Add("--hold debe estar entre 0 y 60 segundos.");
        if (string.IsNullOrEmpty(s.SignatureKind) || s.SignatureKind == "None") w.Add("El paquete es de desarrollo (firma «None»): si el registro falla, solo prueba «denegado para carga lateral», no para una app de la Store.");
        return new(b, w);
    }
}

/// <summary>Lo observado al intentar registrar. <c>RegisteredWithin</c> = tiempo hasta que IsRegistered() pasó a True (null si nunca).</summary>
public sealed record RegisterObservation(string? ThrewException, int? HResult, bool RegisteredAfter, TimeSpan? RegisteredWithin, bool? ConnectResult, bool LinePresent, bool? LineCanDial, bool TimedOut);

public sealed record Stage3Outcome(string Verdict, string Meaning, string Next, bool MayAdvanceToCallObservation);

public static class Stage3Interpreter
{
    public static Stage3Outcome Interpret(RegisterObservation o)
    {
        if (o.TimedOut) return new("SIN RESPUESTA", "RegisterApp() o IsRegistered() no respondieron a tiempo. No se puede afirmar nada; el registro pudo quedar a medias.", "Ejecutar 'stage3 status' y, si IsRegistered es True, 'stage3 unregister'. Repetir con más tiempo.", false);
        if (o.ThrewException is not null)
            return new("REGISTRO RECHAZADO", $"RegisterApp() lanzó {o.ThrewException} (HRESULT 0x{o.HResult:X8}). Patrón de MyPhone #26 y de la pregunta de Stack Overflow (E_ACCESSDENIED, 0x80070005): Windows concede el permiso de consulta pero no el registro.", "Descartar causas locales (Phone Link, vínculo duplicado clásico/LE) y repetir; si persiste: bloqueado para este tipo de firma.", false);
        if (!o.RegisteredAfter)
            return new("REGISTRO SIN EFECTO", "RegisterApp() no lanzó nada pero IsRegistered() sigue en False tras esperar: Windows aceptó la llamada y no registró a la app. Es el patrón del reporte de Microsoft Q&A 6041632 (app empaquetada, compilación 26300).",
                "Hipótesis, de más a menos probable: (1) Windows 11 reserva el registro a otro tipo de firma o a la Store; (2) el rol lo tiene otra app (Phone Link); (3) hace falta que el iPhone esté conectado; (4) error de la compilación 26300. Comprobar (2) y (3) y repetir en otra compilación estable antes de concluir.", false);
        if (o.RegisteredWithin is { } t && t > TimeSpan.FromSeconds(1) && o.ConnectResult is null)
            return new("REGISTRADA (con retraso)", $"IsRegistered() pasó a True a los {t.TotalSeconds:0.#} s: el registro es asíncrono. Los scripts deben esperar, no leerlo de inmediato.", "Seguir con la observación de llamadas (etapa 3b) solo si la línea aparece.", o.LinePresent);
        if (o.ConnectResult == false)
            return new("REGISTRADA, SIN CONEXIÓN HFP", "IsRegistered() es True pero ConnectAsync() devolvió False. Klangbruecke (Windows 10) vio lo mismo y las llamadas funcionaron igual (FINDINGS §12): no es concluyente.", "Comprobar si aparece la línea telefónica y observar una llamada entrante sin contestarla (etapa 3b).", o.LinePresent);
        return o.LinePresent && o.LineCanDial == true
            ? new("REGISTRADA Y CON LÍNEA", "IsRegistered() es True y Windows creó la línea telefónica del transporte. Eso demuestra el registro; NO demuestra contestar ni audio.", "Etapa 3b (observar una llamada sin contestarla).", true)
            : new("REGISTRADA", "IsRegistered() es True; falta ver la línea telefónica. Eso demuestra el registro; NO demuestra contestar ni audio.", "Observar si aparece la línea (puede tardar) y repetir 'stage3 status'.", o.LinePresent);
    }

    /// <summary>Condiciones para pasar a pruebas con llamadas reales. Todas deben cumplirse; ninguna es automática.</summary>
    public static IReadOnlyList<string> MissingToAdvance(Stage3Outcome outcome, bool revertVerified, bool snapshotsEqual, bool userAuthorizedRealCall)
    {
        var m = new List<string>();
        if (!outcome.MayAdvanceToCallObservation) m.Add("el registro debe estar demostrado (IsRegistered True y línea presente)");
        if (!revertVerified) m.Add("la reversión debe haberse verificado (IsRegistered False tras UnregisterApp)");
        if (!snapshotsEqual) m.Add("el estado antes y después debe ser idéntico (audio predeterminado, llamada, conexión)");
        if (!userAuthorizedRealCall) m.Add("autorización explícita del usuario para una llamada real");
        return m;
    }
}

/// <summary>Diario de la etapa 3: se escribe ANTES de registrar. Si el proceso muere a mitad, el siguiente arranque sabe que quedó un registro sin revertir.</summary>
public sealed record JournalState(string TransportKey, DateTime StartedUtc, bool RegisterAttempted, bool RevertedOk);

public static class JournalDecision
{
    public static string Evaluate(JournalState? j, bool? isRegisteredNow)
    {
        if (j is null) return "none";
        if (j.RevertedOk) return "clean";
        if (!j.RegisterAttempted) return "clean";
        return isRegisteredNow == true ? "pending-revert" : "stale";   // stale: intentó registrar, pero hoy IsRegistered es False o ilegible
    }

    public static string KeyOf(string transportId) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(transportId)))[..12];
}
