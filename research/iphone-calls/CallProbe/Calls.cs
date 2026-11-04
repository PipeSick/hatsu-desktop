using System.Diagnostics;
using CallProbe.Logic;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace CallProbe;

internal sealed record Transport(PhoneLineTransportDevice Device, ulong Address, string Key);

/// <summary>
/// ETAPAS 3–5. Registro, observación y control de llamadas.
/// Reglas de seguridad: (1) observar no toca nada; (2) registrar, conectar, revertir, contestar, rechazar y colgar exigen escribir la palabra exacta;
/// (3) NUNCA se marca ni se inicia una llamada; (4) nombres, números y direcciones salen enmascarados salvo --show-numbers;
/// (5) cada llamada de Windows lleva tiempo máximo; (6) la etapa 3 deja un diario y revierte por su cuenta salvo --keep.
/// </summary>
internal static class Calls
{
    private const string AddrKey = "System.DeviceInterface.Bluetooth.DeviceAddress";

    // ───────────────────────────── utilidades
    internal static async Task<Transport?> FindTransportAsync(string? nameFilter)
    {
        var devs = (await Steps.Run("FindAllAsync(transportes Bluetooth)", async () => (await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth), [AddrKey])).ToList())).Value ?? [];
        var m = devs.Where(x => nameFilter is null || x.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (m.Count == 0) { Report.Line("  No hay transporte Bluetooth de telefonía" + (nameFilter is null ? "." : $" que coincida con «{nameFilter}».")); return null; }
        if (m.Count > 1) { Report.Line($"  Hay {m.Count} transportes: indica cuál con --device NOMBRE (no se elige uno al azar)."); return null; }
        var d = m[0]; ulong.TryParse(d.Properties.GetValueOrDefault(AddrKey) as string, System.Globalization.NumberStyles.HexNumber, null, out var addr);
        var dev = (await Steps.Run("PhoneLineTransportDevice.FromId", () => Task.FromResult(PhoneLineTransportDevice.FromId(d.Id)))).Value;
        return dev is null ? null : new Transport(dev, addr, JournalDecision.KeyOf(d.Id));
    }

    private static bool Confirm(string expected, string what)
    {
        if (Console.IsInputRedirected) { Report.Stamp("sin entrada de teclado interactiva: se cancela (por seguridad no se asume ninguna confirmación). Usa Run-Probe.ps1, que abre una consola."); return false; }
        Console.Write($"\n  {what}\n  Escribe {expected} para continuar (cualquier otra cosa cancela): "); string? typed = Console.ReadLine();
        bool ok = ConfirmationGate.IsConfirmed(typed, expected); Report.Stamp(ok ? $"confirmado: {expected}" : $"cancelado (no se escribió {expected})"); return ok;
    }

    private static async Task<bool?> ReadRegisteredAsync(Transport t) { var r = await StepGuard.RunAsync("IsRegistered()", () => Task.FromResult(t.Device.IsRegistered()), TimeSpan.FromSeconds(5)); return r.Ok ? r.Value : null; }

    internal static async Task<bool?> ClassicConnectedAsync(ulong address)
    {
        if (address == 0) return null;
        var r = await Steps.Run("BluetoothDevice.FromBluetoothAddressAsync", async () => { using var d = await BluetoothDevice.FromBluetoothAddressAsync(address); return d?.ConnectionStatus == BluetoothConnectionStatus.Connected; }, 10);
        return r.Ok ? r.Value : null;
    }

    private static async Task<Stage3Snapshot> SnapshotAsync(Transport t, bool queryAccess)
    {
        string access = "(no consultado)";
        if (queryAccess) { var a = await Steps.Run("RequestAccessAsync()", async () => (await t.Device.RequestAccessAsync()).ToString()); access = a.Ok ? a.Value! : "SinRespuesta"; }
        return new Stage3Snapshot(Native.HasPackageIdentity(), Native.SignatureKind(), await ReadRegisteredAsync(t), PhoneCallManager.IsCallActive, PhoneCallManager.IsCallIncoming, Native.PhoneLinkRunning(),
            await ClassicConnectedAsync(t.Address), access, Native.Hash(MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Communications)), Native.Hash(MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Communications)));
    }

    private static void Print(Stage3Snapshot s, string title)
    {
        Report.Line($"  {title}:");
        Report.Item("    identidad de paquete / firma", $"{s.Packaged} / {s.SignatureKind ?? "(sin paquete)"}"); Report.Item("    RequestAccessAsync()", s.AccessStatus); Report.Item("    IsRegistered()", s.IsRegistered?.ToString() ?? "(ilegible)");
        Report.Item("    llamada activa / entrante", $"{s.CallActive} / {s.CallIncoming}"); Report.Item("    Phone Link en ejecución", s.PhoneLinkRunning); Report.Item("    iPhone conectado por Classic", s.ClassicConnected?.ToString() ?? "(n/d)");
        Report.Item("    audio de comunicaciones (resumen)", $"salida {s.CommRenderHash} · entrada {s.CommCaptureHash}");
    }

    internal static async Task<List<PhoneLine>> BluetoothLinesAsync()
    {
        var store = (await Steps.Run("PhoneCallManager.RequestStoreAsync", async () => await PhoneCallManager.RequestStoreAsync(), 10)).Value; if (store is null) return [];
        var w = (await Steps.Run("PhoneCallStore.RequestLineWatcher", () => Task.FromResult(store.RequestLineWatcher()), 8)).Value; if (w is null) return [];
        var ids = new List<Guid>(); var done = new TaskCompletionSource();
        w.LineAdded += (_, a) => { lock (ids) ids.Add(a.LineId); }; w.EnumerationCompleted += (_, _) => done.TrySetResult(); w.Stopped += (_, _) => done.TrySetResult();
        await Steps.Run("PhoneLineWatcher.Start", () => w.Start(), 8);
        await Task.WhenAny(done.Task, Task.Delay(5000));
        _ = Steps.Run("PhoneLineWatcher.Stop (sin esperar)", () => w.Stop(), 8);   // Stop() puede bloquearse: no se espera su resultado
        var lines = new List<PhoneLine>(); Guid[] copy; lock (ids) copy = [.. ids];
        foreach (var id in copy) { var l = (await Steps.Run("PhoneLine.FromIdAsync", async () => await PhoneLine.FromIdAsync(id), 8)).Value; if (l is not null && l.Transport == PhoneLineTransport.Bluetooth) lines.Add(l); }
        return lines;
    }

    // ───────────────────────────── ETAPA 3a: registro (TIENE EFECTO; se revierte solo salvo --keep)
    public static async Task<int> RegisterAsync(string? nameFilter, Stage3Options opt)
    {
        Report.Title("ETAPA 3a — Registro de la app como proveedor de llamadas (TIENE EFECTO; se revierte al terminar salvo --keep)");
        var t = await FindTransportAsync(nameFilter); if (t is null) return 3;

        var journal = Journal.Load(); var regNow = await ReadRegisteredAsync(t); var jd = JournalDecision.Evaluate(journal, regNow);
        if (jd == "pending-revert") { Report.Line("  ¡Una ejecución anterior dejó el registro puesto y sin revertir (diario)! Ejecuta primero: stage3 unregister"); return 3; }
        if (jd == "stale") Report.Line("  Nota: el diario de una ejecución anterior no se cerró, pero hoy IsRegistered() no es True: no queda nada que revertir.");

        var before = await SnapshotAsync(t, queryAccess: true); Print(before, "Estado ANTES (solo lectura)");
        var check = Stage3Preconditions.Evaluate(before, opt);
        foreach (var w in check.Warnings) Report.Line("  AVISO: " + w);
        if (!check.CanProceed) { foreach (var b in check.Blockers) Report.Line("  NO SE PUEDE PROBAR: " + b); Report.Line("  No se cambió nada."); return 3; }

        Report.Line();
        Report.Line("  Qué va a pasar: la app se registra en Windows como proveedor de llamadas Bluetooth del iPhone (PhoneLineTransportDevice.RegisterApp).");
        Report.Line("  Qué NO hace: no marca, contesta ni rechaza llamadas, no toca el emparejamiento, no cambia ajustes de Bluetooth, de audio ni de seguridad.");
        Report.Line($"  Mientras dura ({opt.HoldSeconds} s) NO llames al iPhone ni lo uses para llamar. Si entra una llamada, no se toca y se espera a que termine antes de revertir.");
        if (!Confirm(ConfirmationGate.Register, "Se va a REGISTRAR la app en el transporte telefónico del iPhone.")) return 0;

        journal = new JournalState(t.Key, DateTime.UtcNow, RegisterAttempted: false, RevertedOk: false); Journal.Save(journal);
        journal = journal with { RegisterAttempted = true }; Journal.Save(journal);   // se anota ANTES de registrar: si el proceso muere ahora, el siguiente arranque lo sabe

        bool registeredByUs = false, revertOk = true; Stage3Outcome? outcome = null;
        var clock = Stopwatch.StartNew(); var rec = new List<TimelineSample>(); Action unsub = () => { };
        try
        {
            Observer.PrintApiPresence(); await Observer.PrintTransportPropertiesAsync(t);
            Report.Title("Línea de tiempo (solo lectura entre pasos)");
            unsub = Observer.Subscribe(t, clock);
            rec.Add(await Observer.SampleAsync(t, "antes de RegisterApp", clock));
            double registerAt = clock.Elapsed.TotalSeconds;
            var reg = await Steps.Run("RegisterApp()", () => t.Device.RegisterApp(), 15);
            rec.Add(await Observer.SampleAsync(t, "justo después de RegisterApp", clock));
            string? threw = reg.Error?.GetType().Name; int? hr = reg.Error?.HResult; bool timedOut = reg.Status == StepStatus.TimedOut;
            if (reg.Status == StepStatus.Failed) Report.Item("RegisterApp() lanzó", $"{threw} 0x{hr:X8}");

            var (isReg, within) = await WaitRegisteredAsync(t, wanted: true, seconds: 10, onPoll: async () => rec.Add(await Observer.SampleAsync(t, "espera", clock))); registeredByUs = isReg;
            Report.Item("IsRegistered() tras RegisterApp()", isReg ? $"True (a los {within!.Value.TotalSeconds:0.0} s)" : "False (tras esperar 10 s)");
            Observer.PrintSummary(rec, registerAt);
            Report.Line("  Nota: RegisterApp() devolvió sin excepción" + (isReg ? "." : $" en {reg.Elapsed.TotalMilliseconds:0} ms y IsRegistered() no pasó a True: Windows no materializó el registro (causa NO determinada)."));

            bool linePresent = false; bool? canDial = null; bool? connect = null;
            if (isReg)
            {
                var lines = await BluetoothLinesAsync(); var mine = lines.FirstOrDefault(l => l.TransportDeviceId == t.Device.DeviceId); linePresent = mine is not null; canDial = mine?.CanDial;
                Report.Item("Línea telefónica del transporte", linePresent ? $"presente (CanDial={canDial})" : "no aparece");
                if (opt.Connect && Confirm("CONECTAR", "Se va a abrir la conexión HFP con el iPhone (ConnectAsync). Es un efecto adicional; se deshace al revertir.")) { var c = await Steps.Run("ConnectAsync()", async () => await t.Device.ConnectAsync(), 20); connect = c.Ok ? c.Value : null; Report.Item("ConnectAsync()", c.Ok ? c.Value : c.Status.ToString()); }
                await HoldAsync(opt.HoldSeconds, t);
            }

            outcome = Stage3Interpreter.Interpret(new RegisterObservation(threw, hr, isReg, within, connect, linePresent, canDial, timedOut));
            Report.Title("Resultado de la etapa 3a"); Report.Item("Veredicto", outcome.Verdict); Report.Line("  " + outcome.Meaning); Report.Line("  Siguiente: " + outcome.Next);
        }
        finally
        {
            unsub();
            if (!opt.Keep) revertOk = await RevertAsync(t, journal, onlyIfRegistered: true);
            else Report.Line("  --keep: el registro SE QUEDA puesto. Revierte con: stage3 unregister");
        }

        var after = await SnapshotAsync(t, queryAccess: false); Print(after, "Estado DESPUÉS (solo lectura)");
        var diffs = SnapshotComparer.Differences(before, after);
        Report.Item("Diferencias antes/después", diffs.Count == 0 ? "ninguna: el sistema quedó igual" : string.Join(" | ", diffs));
        if (outcome is not null)
        {
            var missing = Stage3Interpreter.MissingToAdvance(outcome, revertOk && !opt.Keep, diffs.Count == 0, userAuthorizedRealCall: false);
            Report.Item("Para avanzar a pruebas con llamadas reales falta", missing.Count == 0 ? "nada" : string.Join("; ", missing));
        }

        return revertOk ? 0 : 4;
    }

    private static async Task<(bool Reached, TimeSpan? Within)> WaitRegisteredAsync(Transport t, bool wanted, int seconds, Func<Task>? onPoll = null)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (onPoll is not null) await onPoll();
            var r = await ReadRegisteredAsync(t); if (r == wanted) return (wanted ? true : false, sw.Elapsed);
            if (sw.Elapsed > TimeSpan.FromSeconds(seconds) || Steps.Cancel.IsCancellationRequested) return (!wanted, null);
            await Steps.DelayAsync(500);
        }
    }

    /// <summary>Mantiene el registro unos segundos. Si entra una llamada real NO se toca: se espera a que termine (máx. 10 min) y recién entonces se revierte.</summary>
    private static async Task HoldAsync(int seconds, Transport t)
    {
        for (int i = 0; i < seconds && !Steps.Cancel.IsCancellationRequested; i++) { if (PhoneCallManager.IsCallIncoming || PhoneCallManager.IsCallActive) break; await Steps.DelayAsync(1000); }
        if (!(PhoneCallManager.IsCallIncoming || PhoneCallManager.IsCallActive)) return;
        Report.Stamp("HAY UNA LLAMADA REAL: no se toca. Se espera a que termine para revertir (máx. 10 min).");
        for (int i = 0; i < 600 && (PhoneCallManager.IsCallIncoming || PhoneCallManager.IsCallActive) && !Steps.Cancel.IsCancellationRequested; i++) await Steps.DelayAsync(1000);
    }

    /// <summary>Deshace el registro con UnregisterApp(), comprueba que IsRegistered() quedó en False y cierra el diario solo si es así.</summary>
    private static async Task<bool> RevertAsync(Transport t, JournalState? journal, bool onlyIfRegistered)
    {
        Report.Title("Reversión del registro");
        var now = await ReadRegisteredAsync(t);
        if (now != true && onlyIfRegistered) { Report.Line("  IsRegistered() no es True: no hay nada que revertir."); if (journal is not null) Journal.Save(journal with { RevertedOk = true }); return true; }
        var u = await Steps.Run("UnregisterApp()", () => t.Device.UnregisterApp(), 15);
        if (u.Status == StepStatus.Failed) Report.Item("UnregisterApp() lanzó", $"{u.Error?.GetType().Name} 0x{u.Error?.HResult:X8}");
        var (reached, _) = await WaitRegisteredAsync(t, wanted: false, seconds: 10);
        Report.Item("IsRegistered() tras UnregisterApp()", reached ? "False (revertido)" : "SIGUE EN True");
        if (reached) { if (journal is not null) Journal.Save(journal with { RevertedOk = true }); return true; }
        Report.Line("  NO SE PUDO REVERTIR. Repite: stage3 unregister. Último recurso (destructivo, pierdes el emparejamiento): quitar el iPhone en Configuración › Bluetooth y volver a emparejarlo. Quitar el paquete NO deshace el registro.");
        return false;
    }

    public static async Task<int> UnregisterAsync(string? nameFilter)
    {
        Report.Title("Deshacer — quitar el registro de la app (UnregisterApp)");
        var t = await FindTransportAsync(nameFilter); if (t is null) return 3;
        var journal = Journal.Load(); var now = await ReadRegisteredAsync(t); Report.Item("IsRegistered() antes", now?.ToString() ?? "(ilegible)");
        if (now != true) { Report.Line("  No hay registro que quitar."); if (journal is { RevertedOk: false }) Journal.Save(journal with { RevertedOk = true }); return 0; }
        bool pending = JournalDecision.Evaluate(journal, now) == "pending-revert";
        if (!pending && !Confirm("REVERTIR", "IsRegistered() es True pero el diario no dice que lo registrara esta herramienta. Quitar el registro solo afecta al de ESTA app.")) return 0;
        return await RevertAsync(t, journal, onlyIfRegistered: false) ? 0 : 4;
    }

    /// <summary>Solo lectura: estado del registro, del diario y del sistema.</summary>
    public static async Task<int> StatusAsync(string? nameFilter)
    {
        Report.Title("Estado del registro (solo lectura)");
        var t = await FindTransportAsync(nameFilter); if (t is null) return 3;
        var s = await SnapshotAsync(t, queryAccess: true); Print(s, "Estado"); var j = Journal.Load();
        Observer.PrintApiPresence(); await Observer.PrintTransportPropertiesAsync(t);
        var clk = Stopwatch.StartNew(); Report.Title("Muestras instantáneas (3 lecturas, 1 s)"); var smp = new List<TimelineSample>();
        for (int i = 0; i < 3; i++) { smp.Add(await Observer.SampleAsync(t, "status", clk)); await Steps.DelayAsync(1000); }
        Observer.PrintSummary(smp, null);
        Report.Item("Diario", j is null ? "(sin diario)" : $"{JournalDecision.Evaluate(j, s.IsRegistered)} (inicio {j.StartedUtc:u})");
        return 0;
    }

    // ───────────────────────────── ETAPA 3b: observar (NO contesta, NO rechaza, NO cuelga)
    private static async Task<List<PhoneCall>> ActiveCallsAsync(IEnumerable<PhoneLine> lines)
    {
        var calls = new List<PhoneCall>();
        foreach (var l in lines) { var r = (await Steps.Run("GetAllActivePhoneCallsAsync", async () => await l.GetAllActivePhoneCallsAsync(), 8)).Value; if (r is not null && r.OperationStatus == PhoneLineOperationStatus.Succeeded) calls.AddRange(r.AllActivePhoneCalls); }
        return calls;
    }

    private static async Task<string> DescribeCallAsync(PhoneCall c)
    {
        try { var info = await c.GetPhoneCallInfoAsync(); return $"id {Report.Id(c.CallId)} estado {c.Status} dirección {info.CallDirection} audio {c.AudioDevice} silenciada {c.IsMuted} contacto «{Report.Mask(info.DisplayName)}» número {Report.Mask(info.PhoneNumber)}"; }
        catch (Exception e) { return $"id {Report.Id(c.CallId)} estado {c.Status} ({e.GetType().Name} al pedir los datos)"; }
    }

    public static async Task MonitorAsync(int seconds)
    {
        Report.Title($"ETAPA 3b — Observación de llamadas durante {seconds} s (no se toca ninguna llamada; requiere un registro vigente: stage3 register --keep)");
        var lines = await BluetoothLinesAsync(); Report.Item("Líneas Bluetooth", lines.Count);
        if (lines.Count == 0) Report.Line("  Sin línea: el transporte no está registrado y conectado.");
        PhoneCallManager.CallStateChanged += (_, _) => Report.Stamp($"PhoneCallManager.CallStateChanged  (IsCallIncoming={PhoneCallManager.IsCallIncoming}, IsCallActive={PhoneCallManager.IsCallActive})");
        var seen = new HashSet<string>(); var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end && !Steps.Cancel.IsCancellationRequested)
        {
            foreach (var c in await ActiveCallsAsync(lines))
                if (seen.Add(c.CallId)) { Report.Stamp("LLAMADA " + await DescribeCallAsync(c)); c.StatusChanged += (s, _) => Report.Stamp($"  StatusChanged → {s.Status}"); c.AudioDeviceChanged += (s, _) => Report.Stamp($"  AudioDeviceChanged → {s.AudioDevice}"); }
            await Steps.DelayAsync(1000);
        }

        Report.Stamp("fin de la observación");
    }

    // ───────────────────────────── ETAPA 5: control (SOLO con llamada real, SOLO con palabra de confirmación; NO se ejecuta sin orden explícita)
    public static async Task ActAsync(string action, bool routeLocal)
    {
        Report.Title($"ETAPA 5 — Acción sobre la llamada actual: {action} (LLAMADA REAL)");
        var calls = await ActiveCallsAsync(await BluetoothLinesAsync());
        if (calls.Count == 0) { Report.Line("  No hay ninguna llamada activa o entrante."); return; }
        var c = calls[0]; Report.Stamp("llamada: " + await DescribeCallAsync(c));
        (string word, Func<Task<PhoneCallOperationStatus>> run) = action switch
        {
            "answer" => (ConfirmationGate.Answer, (Func<Task<PhoneCallOperationStatus>>)(async () => await c.AcceptIncomingAsync().AsTask())),
            "reject" => (ConfirmationGate.Reject, async () => await c.RejectIncomingAsync().AsTask()),
            "hangup" => (ConfirmationGate.HangUp, async () => await c.EndAsync().AsTask()),
            _ => ("", async () => PhoneCallOperationStatus.OtherFailure),
        };
        if (word.Length == 0) { Report.Line("  Acción desconocida."); return; }
        if (!Confirm(word, $"Esto ejecuta «{action}» sobre la llamada REAL de arriba.")) return;
        var r = await run(); Report.Stamp($"{action} → {r}");
        if (action == "answer" && r == PhoneCallOperationStatus.Succeeded)
        {
            await Steps.DelayAsync(1000); Report.Stamp("audio tras contestar: " + c.AudioDevice);
            if (routeLocal) { var a = await c.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); Report.Stamp($"ChangeAudioDevice(LocalDevice) → {a}; audio ahora: {c.AudioDevice}"); }
        }
    }

    public static async Task GuidedCallTestAsync()
    {
        Report.Title("ETAPA 5 — Llamada de prueba guiada (REQUIERE QUE TÚ LLAMES al iPhone desde otro teléfono)");
        Report.Line("  1) Registra antes el transporte (stage3 register --keep).  2) Ten audífonos/altavoces del PC listos.  3) Llama al iPhone desde otro teléfono cuando se te indique.");
        var lines = await BluetoothLinesAsync(); if (lines.Count == 0) { Report.Line("  Sin línea Bluetooth: no se puede continuar."); return; }
        Console.Write("\n  Llama ahora al iPhone. Esperando hasta 60 s… "); PhoneCall? call = null; var end = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < end && call is null && !Steps.Cancel.IsCancellationRequested) { call = (await ActiveCallsAsync(lines)).FirstOrDefault(); if (call is null) await Steps.DelayAsync(500); }
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
