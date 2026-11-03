using CallProbe.Logic;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace CallProbe;

/// <summary>
/// ETAPAS 3–5. Registro, observación y control de llamadas.
/// Reglas de seguridad: (1) observar no toca nada; (2) registrar, contestar, rechazar y colgar exigen escribir la palabra exacta;
/// (3) NUNCA se marca ni se inicia una llamada; (4) los nombres y números salen enmascarados salvo --show-numbers.
/// </summary>
internal static class Calls
{
    private static async Task<PhoneLineTransportDevice?> FindTransportAsync(string? nameFilter)
    {
        var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
        var d = devs.FirstOrDefault(x => nameFilter is null || x.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        if (d is null) { Report.Line("  No hay transporte Bluetooth de telefonía" + (nameFilter is null ? "." : $" que coincida con «{nameFilter}».")); return null; }
        return PhoneLineTransportDevice.FromId(d.Id);
    }

    private static bool Confirm(string expected, string what)
    {
        Console.Write($"\n  {what}\n  Escribe {expected} para continuar (cualquier otra cosa cancela): "); string? typed = Console.ReadLine();
        bool ok = ConfirmationGate.IsConfirmed(typed, expected); Report.Stamp(ok ? $"confirmado: {expected}" : $"cancelado (no se escribió {expected})"); return ok;
    }

    // ── ETAPA 3a: registrar (tiene efecto: el teléfono empieza a ofrecer el PC como dispositivo de llamadas)
    public static async Task RegisterAsync(string? nameFilter)
    {
        Report.Title("ETAPA 3a — Registro de la app como proveedor de llamadas (TIENE EFECTO, reversible con 'unregister')");
        var t = await FindTransportAsync(nameFilter); if (t is null) return; bool packaged = Native.HasPackageIdentity();
        Report.Item("Identidad de paquete", packaged);
        var status = await t.RequestAccessAsync(); Report.Item("RequestAccessAsync()", status);
        if (status != DeviceAccessStatus.Allowed) { var i0 = AccessInterpreter.Interpret(packaged, status.ToString(), null, Native.SignatureKind()); Report.Item("Interpretación", i0.Verdict); Report.Line("  " + i0.Explanation); return; }
        Report.Item("IsRegistered() antes", t.IsRegistered());
        if (!Confirm(ConfirmationGate.Register, "Esto registra la app para el rol manos libres del iPhone seleccionado. Si Phone Link lo tiene, puede cambiar de dueño.")) return;
        try { if (!t.IsRegistered()) t.RegisterApp(); } catch (Exception e) { Report.Item("RegisterApp() lanzó", $"{e.GetType().Name} 0x{e.HResult:X8}"); }
        bool reg = t.IsRegistered(); Report.Item("IsRegistered() después", reg);
        try { bool ok = await t.ConnectAsync(); Report.Item("ConnectAsync()", ok); } catch (Exception e) { Report.Item("ConnectAsync() lanzó", $"{e.GetType().Name} 0x{e.HResult:X8}"); }
        try { Report.Item("AudioRoutingStatus", t.AudioRoutingStatus); } catch (Exception e) { Report.Item("AudioRoutingStatus", e.GetType().Name); }
        var i = AccessInterpreter.Interpret(packaged, status.ToString(), reg, Native.SignatureKind()); Report.Item("Interpretación", i.Verdict); Report.Line("  " + i.Explanation); Report.Line("  Siguiente: " + i.NextStep);
    }

    public static async Task UnregisterAsync(string? nameFilter)
    {
        Report.Title("Deshacer — quitar el registro de la app");
        var t = await FindTransportAsync(nameFilter); if (t is null) return;
        try { Report.Item("IsRegistered() antes", t.IsRegistered()); t.UnregisterApp(); Report.Item("IsRegistered() después", t.IsRegistered()); } catch (Exception e) { Report.Item("UnregisterApp() lanzó", $"{e.GetType().Name} 0x{e.HResult:X8}"); }
    }

    // ── Utilidades de líneas y llamadas
    private static async Task<List<PhoneLine>> BluetoothLinesAsync()
    {
        var store = await PhoneCallManager.RequestStoreAsync(); var done = new TaskCompletionSource(); var ids = new List<Guid>(); var w = store.RequestLineWatcher();
        w.LineAdded += (_, a) => ids.Add(a.LineId); w.EnumerationCompleted += (_, _) => done.TrySetResult(); w.Stopped += (_, _) => done.TrySetResult(); w.Start();
        await Task.WhenAny(done.Task, Task.Delay(5000)); w.Stop();
        var lines = new List<PhoneLine>(); foreach (var id in ids) { var l = await PhoneLine.FromIdAsync(id); if (l is not null && l.Transport == PhoneLineTransport.Bluetooth) lines.Add(l); }
        return lines;
    }

    private static async Task<List<PhoneCall>> ActiveCallsAsync(IEnumerable<PhoneLine> lines)
    {
        var calls = new List<PhoneCall>();
        foreach (var l in lines) { var r = await l.GetAllActivePhoneCallsAsync(); if (r.OperationStatus == PhoneLineOperationStatus.Succeeded) calls.AddRange(r.AllActivePhoneCalls); }
        return calls;
    }

    private static async Task<string> DescribeCallAsync(PhoneCall c)
    {
        try { var info = await c.GetPhoneCallInfoAsync(); return $"id …{c.CallId[^Math.Min(6, c.CallId.Length)..]} estado {c.Status} dirección {info.CallDirection} audio {c.AudioDevice} silenciada {c.IsMuted} contacto «{Report.Mask(info.DisplayName)}» número {Report.Mask(info.PhoneNumber)}"; }
        catch (Exception e) { return $"id …{c.CallId[^Math.Min(6, c.CallId.Length)..]} estado {c.Status} ({e.GetType().Name} al pedir los datos)"; }
    }

    // ── ETAPA 3b: observar (NO contesta, NO rechaza, NO cuelga)
    public static async Task MonitorAsync(int seconds)
    {
        Report.Title($"ETAPA 3b — Observación de llamadas durante {seconds} s (no se toca ninguna llamada)");
        var lines = await BluetoothLinesAsync(); Report.Item("Líneas Bluetooth", lines.Count);
        if (lines.Count == 0) Report.Line("  Sin línea: ¿registraste el transporte (etapa 3a) y está conectado?");
        PhoneCallManager.CallStateChanged += (_, _) => Report.Stamp($"PhoneCallManager.CallStateChanged  (IsCallIncoming={PhoneCallManager.IsCallIncoming}, IsCallActive={PhoneCallManager.IsCallActive})");
        var seen = new HashSet<string>(); var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            foreach (var c in await ActiveCallsAsync(lines))
                if (seen.Add(c.CallId))
                {
                    Report.Stamp("LLAMADA " + await DescribeCallAsync(c));
                    c.StatusChanged += (s, _) => Report.Stamp($"  StatusChanged → {s.Status}"); c.AudioDeviceChanged += (s, _) => Report.Stamp($"  AudioDeviceChanged → {s.AudioDevice}");
                }

            await Task.Delay(1000);
        }

        Report.Stamp("fin de la observación");
    }

    // ── ETAPA 5: control (SOLO con llamada real, SOLO con palabra de confirmación)
    public static async Task ActAsync(string action, bool routeLocal)
    {
        Report.Title($"ETAPA 5 — Acción sobre la llamada actual: {action} (LLAMADA REAL)");
        var lines = await BluetoothLinesAsync(); var calls = await ActiveCallsAsync(lines);
        if (calls.Count == 0) { Report.Line("  No hay ninguna llamada activa o entrante."); return; }
        var c = calls[0]; Report.Stamp("llamada: " + await DescribeCallAsync(c));
        (string word, Func<Task<PhoneCallOperationStatus>> run) = action switch
        {
            "answer" => (ConfirmationGate.Answer, (Func<Task<PhoneCallOperationStatus>>)(async () => (await c.AcceptIncomingAsync().AsTask()))),
            "reject" => (ConfirmationGate.Reject, async () => await c.RejectIncomingAsync().AsTask()),
            "hangup" => (ConfirmationGate.HangUp, async () => await c.EndAsync().AsTask()),
            _ => ("", async () => PhoneCallOperationStatus.OtherFailure),
        };
        if (word.Length == 0) { Report.Line("  Acción desconocida."); return; }
        if (!Confirm(word, $"Esto ejecuta «{action}» sobre la llamada REAL de arriba.")) return;
        var r = await run(); Report.Stamp($"{action} → {r}");
        if (action == "answer" && r == PhoneCallOperationStatus.Succeeded)
        {
            await Task.Delay(1000); Report.Stamp("audio tras contestar: " + c.AudioDevice);
            if (routeLocal) { var a = await c.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); Report.Stamp($"ChangeAudioDevice(LocalDevice) → {a}; audio ahora: {c.AudioDevice}"); }
        }
    }

    /// <summary>Guía de una llamada de prueba completa con preguntas al tester; el resultado se resume con <see cref="CallScorecard"/>.</summary>
    public static async Task GuidedCallTestAsync()
    {
        Report.Title("ETAPA 5 — Llamada de prueba guiada (REQUIERE QUE TÚ LLAMES al iPhone desde otro teléfono)");
        Report.Line("  1) Registra antes el transporte (stage3 register).  2) Ten audífonos/altavoces del PC listos.  3) Llama al iPhone desde otro teléfono cuando se te indique.");
        var lines = await BluetoothLinesAsync(); if (lines.Count == 0) { Report.Line("  Sin línea Bluetooth: no se puede continuar."); return; }
        Console.Write("\n  Llama ahora al iPhone. Esperando hasta 60 s… "); PhoneCall? call = null; var end = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < end && call is null) { call = (await ActiveCallsAsync(lines)).FirstOrDefault(); if (call is null) await Task.Delay(500); }
        if (call is null) { Report.Line("sin llamada detectada"); Report.Line("  " + new CallScorecard(false, false, false, false, false, false).Summary()); return; }
        Report.Stamp("DETECTADA: " + await DescribeCallAsync(call));
        bool answered = false, inAudio = false, outAudio = false, ended = false, recovered = false;
        if (Confirm(ConfirmationGate.Answer, "Se va a CONTESTAR la llamada real y mover el audio al PC."))
        {
            var r = await call.AcceptIncomingAsync(); Report.Stamp("AcceptIncoming → " + r); answered = r == PhoneCallOperationStatus.Succeeded;
            if (answered)
            {
                var a = await call.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); Report.Stamp($"ChangeAudioDevice(LocalDevice) → {a}; audio: {call.AudioDevice}");
                inAudio = Ask("¿Oyes al interlocutor por los audífonos/altavoces del PC?"); outAudio = Ask("¿El interlocutor te oye a ti por el micrófono del PC (pídele que confirme)?");
                if (Confirm(ConfirmationGate.HangUp, "Se va a COLGAR desde Windows.")) { var e = await call.EndAsync(); Report.Stamp("End → " + e); ended = e == PhoneCallOperationStatus.Succeeded; }
                recovered = Ask("¿Tras colgar, el PC y el iPhone quedaron conectados y listos para otra llamada (sin reiniciar nada)?");
            }
        }

        var card = new CallScorecard(true, answered, inAudio, outAudio, ended, recovered); Report.Title("Resultado"); Report.Line("  " + card.Summary());
    }

    private static bool Ask(string q) { Console.Write($"\n  {q} (s/n): "); bool yes = (Console.ReadLine() ?? "").Trim().StartsWith("s", StringComparison.OrdinalIgnoreCase); Report.Stamp($"{q} → {(yes ? "sí" : "no")}"); return yes; }
}
