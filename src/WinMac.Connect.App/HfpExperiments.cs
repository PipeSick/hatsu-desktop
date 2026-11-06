using System.Text;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// EXPERIMENTOS que SÍ cambian el estado de Windows de forma acotada y reversible (docs/59). Solo los lanza el usuario desde Ajustes tras una confirmación que lo explica:
/// <list type="bullet">
/// <item><c>--probe-hfp-experimental</c>: registra esta app en el transporte telefónico Bluetooth (<c>RegisterApp</c>), pide a Windows que lo conecte (<c>ConnectAsync</c>), observa líneas, enrutado y endpoints durante unos segundos y SIEMPRE deshace el registro (<c>UnregisterApp</c>) si lo hizo él.</item>
/// <item><c>--probe-audio-route</c>: necesita una llamada ACTIVA (Talking) en una línea Bluetooth; mueve su audio al PC (<c>ChangeAudioDeviceAsync(LocalDevice)</c>) unos segundos para comprobar si hay audio bidireccional y lo devuelve al teléfono (<c>RemoteDevice</c>) pase lo que pase. NO contesta ni cuelga.</item>
/// <item><c>--restore-hfp</c>: solo deshace cualquier registro de esta app (por si un experimento se interrumpió).</item>
/// </list>
/// Si Windows lo niega, el informe lo dice con el código; nada de eso es un fallo del programa: es la respuesta que se busca.
/// </summary>
internal static class HfpExperiments
{
    /// <summary>
    /// Registro + conexión con la MISMA secuencia por etapas del módulo (<see cref="TransportSequence"/>): cada etapa con su tope y su HRESULT, presupuesto total de 40 s (el host espera 90 s) y el informe
    /// se reescribe tras CADA etapa: aunque algo cuelgue, el archivo dice en qué etapa se quedó. Solo un proceso de Hatsu toca el transporte a la vez.
    /// </summary>
    public static async Task<string> RegisterAndConnectAsync(string outPath)
    {
        var F = new List<DiagFinding>(); var D = new List<string>(); void L(string s) => D.Add(s);
        void Flush(string title) { try { File.WriteAllText(outPath, Render(title, F, D)); } catch (Exception) { } }
        const string Title = "Hatsu Connect · registro + conexión HFP (por etapas, reversible)";
        using var gate = HfpLock.TryTake();
        if (gate is null) { F.Add(new("Transporte telefónico", DiagState.NotChecked, "el módulo de llamadas de Hatsu ya está usando el transporte (registro activo): no hace falta este experimento; su estado está en el registro de Connect")); return Render(Title, F, D); }
        var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
        if (devs.Count == 0) { F.Add(new("Transporte telefónico Bluetooth", DiagState.NotChecked, "Windows no lista ningún transporte: no hay nada que registrar")); return Render(Title, F, D); }
        F.Add(new("Etapa en curso", DiagState.NotChecked, "iniciando")); Flush(Title);
        foreach (var d in devs.Take(2))
        {
            string tag = DiagIds.Tag(d.Id);
            using var seq = new TransportSequence(WinRtTransportOps.FromId(d.Id));
            var res = await seq.RegisterAndConnectAsync(progress: st =>
            {
                F.RemoveAll(x => x.Area == "Etapa en curso");
                F.Add(new($"{st.Stage} · transporte {tag}", st.Outcome == StageOutcome.Ok ? DiagState.Available : st.Outcome == StageOutcome.Threw && st.HResult == unchecked((int)0x80070005) ? DiagState.AccessDenied : st.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled ? DiagState.Error : DiagState.Error,
                    $"{st.Outcome} en {st.Ms} ms{(st.HResult != 0 ? $" · HRESULT 0x{unchecked((uint)st.HResult):X8} ({DiagIds.HResultName(st.HResult)})" : "")}{(string.IsNullOrEmpty(st.Detail) ? "" : " · " + st.Detail)}"));
                F.Add(new("Etapa en curso", DiagState.NotChecked, "esperando a Windows tras «" + st.Stage + "»")); Flush(Title);
            });
            F.RemoveAll(x => x.Area == "Etapa en curso");
            F.Add(new($"Resultado · transporte {tag}", res.Ready ? DiagState.Available : DiagState.Error, res.Status.Reason + (res.Wedged ? $" (BLOQUEO en «{res.WedgedStage}»)" : "")));
            F.Add(new($"IsRegistered tras el experimento · {tag}", res.RegisteredAfter ? DiagState.Available : DiagState.NotChecked, res.RegisteredAfter ? "verdadero" : "falso"));
            if (res.Ready)
            {   // ¿aparecen líneas Bluetooth ahora que está registrado? (la línea solo existe con el registro)
                var paired = await PhoneEnv.PairedAsync();
                var lines = await PhoneEnv.LinesAsync(paired, TimeSpan.FromSeconds(6));
                foreach (var l in lines) L($"  línea{l.IdTag} {l.Transport} dispositivo{l.DeviceIdTag} = {l.MatchesPaired ?? "?"} · llamadas activas: {l.Calls.Count}");
                F.Add(new("Líneas Bluetooth tras registrar", lines.Any(l => l.Transport == "Bluetooth") ? DiagState.Detected : DiagState.NotChecked, $"{lines.Count(l => l.Transport == "Bluetooth")} línea(s) Bluetooth (sin teléfono conectado por HFP no aparece ninguna)"));
            }
            var c = await seq.CleanupAsync(); F.Add(new($"Deshacer registro · {tag}", c.Ok ? DiagState.Available : DiagState.Error, c.ToString()));
            if (res.Wedged) { Flush(Title); return Render(Title, F, D); }
        }
        return Render(Title, F, D);
    }

    public static async Task<string> AudioRouteAsync()
    {
        var F = new List<DiagFinding>(); var D = new List<string>(); void L(string s) => D.Add(s);
        var paired = await PhoneEnv.PairedAsync();
        var lines = await PhoneEnv.LinesAsync(paired, TimeSpan.FromSeconds(6));
        var store = await PhoneCallManager.RequestStoreAsync();
        PhoneCall? target = null; PhoneLine? line = null;
        foreach (var l in lines.Where(x => x.Transport == "Bluetooth"))
        {
            var real = await PhoneLine.FromIdAsync(await FindId(store, l.IdTag));
            var res = await real.GetAllActivePhoneCallsAsync();
            target = res.AllActivePhoneCalls.FirstOrDefault(c => c.Status == PhoneCallStatus.Talking); line = real;
            if (target is not null) break;
        }
        if (target is null || line is null) { F.Add(new("Llamada activa en línea Bluetooth", DiagState.NotChecked, "no hay ninguna llamada en estado «Talking» en una línea Bluetooth: contesta una llamada en el teléfono y vuelve a intentarlo")); return Render("Hatsu Connect · experimento: audio de la llamada al PC", F, D); }
        string callTag = DiagIds.Tag(target.CallId.ToString());
        L($"Llamada{callTag}: audio antes={target.AudioDevice}, silenciada={target.IsMuted}");
        bool moved = false;
        try
        {
            var st = await target.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice); moved = true;
            F.Add(new("Mover el audio al PC (ChangeAudioDevice LocalDevice)", st == PhoneCallOperationStatus.Succeeded ? DiagState.Available : DiagState.Error, "resultado: " + st));
            for (int i = 0; i < 10; i++) { await Task.Delay(1000); L($"  +{i + 1}s audio={target.AudioDevice} estado={target.Status}"); }
            var eps = await PhoneEnv.EndpointsAsync(paired);
            L("Endpoints durante la prueba: " + string.Join(" | ", eps.Select(e => $"{e.Flow}:{e.Name}{(e.Default ? "*" : "")}{(e.HandsFreeName ? "[HF]" : "")}")));
            F.Add(new("Audio bidireccional en el PC", DiagState.NotChecked, "lo confirma la persona: ¿se oyó al interlocutor por los altavoces del PC y se le oyó a ella por el micrófono del PC? (este informe no puede oírlo)"));
        }
        catch (Exception e) { F.Add(new("Mover el audio al PC (ChangeAudioDevice LocalDevice)", CallProbe.Classify(e), CallProbe.Describe(e))); }
        finally
        {
            try { var back = await target.ChangeAudioDeviceAsync(PhoneCallAudioDevice.RemoteDevice); F.Add(new("Devolver el audio al teléfono", back == PhoneCallOperationStatus.Succeeded ? DiagState.Available : DiagState.Error, "resultado: " + back + (moved ? "" : " (no se había movido)"))); L($"Llamada{callTag}: audio después={target.AudioDevice}"); }
            catch (Exception e) { F.Add(new("Devolver el audio al teléfono", DiagState.Error, CallProbe.Describe(e) + " — si el audio sigue en el PC, cambia la salida en el teléfono o cuelga")); }
        }
        return Render("Hatsu Connect · experimento: audio de la llamada al PC", F, D);
    }

    public static async Task<string> RestoreAsync()
    {
        var F = new List<DiagFinding>();
        using var gate = HfpLock.TryTake();
        if (gate is null) { F.Add(new("Deshacer registro", DiagState.NotChecked, "el módulo de llamadas de Hatsu está usando el transporte: desactiva «Llamadas completas desde el PC» en Ajustes y Hatsu deshará su registro")); return Render("Hatsu Connect · deshacer registro de la app", F, []); }
        try
        {
            var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
            foreach (var d in devs.Take(4))
            {
                using var seq = new TransportSequence(WinRtTransportOps.FromId(d.Id)); string tag = DiagIds.Tag(d.Id);
                var c = await seq.CleanupAsync(); F.Add(new($"Transporte {tag}", c.Ok ? DiagState.Detected : DiagState.Error, c.ToString()));
            }
            if (devs.Count == 0) F.Add(new("Transporte telefónico Bluetooth", DiagState.NotChecked, "ninguno listado"));
        }
        catch (Exception e) { F.Add(new("Deshacer registro", CallProbe.Classify(e), CallProbe.Describe(e))); }
        return Render("Hatsu Connect · deshacer registro de la app", F, []);
    }

    private static async Task<(string Text, int Lines, int Eps)> Snapshot(IReadOnlyList<PhoneEnv.Paired> paired)
    {
        string lt = "líneas: ?"; int ln = -1, ne = -1; string et = "endpoints: ?";
        try { var l = await PhoneEnv.LinesAsync(paired, TimeSpan.FromSeconds(3)); ln = l.Count; lt = "líneas: " + (l.Count == 0 ? "ninguna" : string.Join(", ", l.Select(x => $"{x.Transport}/{x.CallsStatus}/{x.Calls.Count} llamadas"))); } catch (Exception e) { lt = "líneas: error " + e.GetType().Name; }
        try { var e = await PhoneEnv.EndpointsAsync(paired); ne = e.Count; et = "endpoints: " + string.Join(", ", e.Select(x => $"{x.Flow}:{x.ContainerTag}{(x.HandsFreeName ? "[HF]" : "")}")); } catch (Exception e) { et = "endpoints: error " + e.GetType().Name; }
        return ($"{lt}; {et}", ln, ne);
    }

    private static async Task AppendLines(IReadOnlyList<PhoneEnv.Paired> paired, Action<string> L)
    {
        try { var lines = await PhoneEnv.LinesAsync(paired, TimeSpan.FromSeconds(3)); foreach (var l in lines) L($"  línea{l.IdTag} {l.Transport} dispositivo{l.DeviceIdTag} = {l.MatchesPaired ?? "?"}"); } catch (Exception) { }
    }

    private static async Task<Guid> FindId(PhoneCallStore store, string tag)
    {
        var w = store.RequestLineWatcher(); var ids = new List<Guid>(); var done = new TaskCompletionSource();
        w.LineAdded += (_, e) => { lock (ids) ids.Add(e.LineId); }; w.EnumerationCompleted += (_, _) => done.TrySetResult(); w.Start();
        try { await Task.WhenAny(done.Task, Task.Delay(4000)); } finally { try { w.Stop(); } catch (Exception) { } }
        lock (ids) return ids.FirstOrDefault(i => DiagIds.Tag(i.ToString()) == tag);
    }

    private static string Render(string title, List<DiagFinding> f, List<string> d)
    {
        var header = new[] { $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm}", $"Windows: {Environment.OSVersion.VersionString}", $"Identidad de paquete (Store/MSIX): {(CallProbe.HasPackageIdentity() ? "sí" : "no")}" };
        return DiagRedactor.Redact(DiagReport.Compose(title, header, f, d), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);
    }
}
