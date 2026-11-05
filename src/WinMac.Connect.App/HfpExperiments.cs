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
    public static async Task<string> RegisterAndConnectAsync()
    {
        var F = new List<DiagFinding>(); var D = new List<string>(); void L(string s) => D.Add(s);
        var paired = await PhoneEnv.PairedAsync();
        var before = await Snapshot(paired); L("Antes: " + before.Text);
        var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
        if (devs.Count == 0) F.Add(new("Transporte telefónico Bluetooth", DiagState.NotChecked, "Windows no lista ningún transporte: no hay nada que registrar"));
        foreach (var d in devs.Take(2))
        {
            var t = PhoneLineTransportDevice.FromId(d.Id); string tag = DiagIds.Tag(d.Id); bool weRegistered = false;
            try
            {
                bool was = false; try { was = t.IsRegistered(); } catch (Exception) { }
                L($"Transporte {tag}: registrado antes={was}");
                if (!was)
                {
                    try { t.RegisterApp(); weRegistered = true; F.Add(new($"RegisterApp {tag}", DiagState.Available, "Windows aceptó el registro de esta app")); }
                    catch (Exception e) { F.Add(new($"RegisterApp {tag}", CallProbe.Classify(e), CallProbe.Describe(e))); }
                }
                try { L($"  tras registrar: registrado={t.IsRegistered()}"); } catch (Exception) { }
                try { var r = await t.ConnectAsync(); F.Add(new($"ConnectAsync {tag}", DiagState.Detected, "resultado: " + r)); }
                catch (Exception e) { F.Add(new($"ConnectAsync {tag}", CallProbe.Classify(e), CallProbe.Describe(e))); }
                for (int i = 0; i < 8; i++)   // observa hasta ~8 s si aparece algo (líneas, enrutado, endpoints)
                {
                    await Task.Delay(1000);
                    string routing = "?"; try { routing = t.AudioRoutingStatus.ToString(); } catch (Exception) { }
                    L($"  +{i + 1}s AudioRoutingStatus={routing}");
                }
                var after = await Snapshot(paired); L("Después: " + after.Text);
                F.Add(new("Cambios observados tras registrar y conectar", before.Text == after.Text ? DiagState.NotChecked : DiagState.Detected,
                    before.Text == after.Text ? "sin cambios en líneas ni endpoints" : "líneas/endpoints cambiaron: ver «Antes» y «Después» en el detalle"));
                await AppendLines(paired, L);
            }
            finally
            {
                if (weRegistered)
                {
                    try { t.UnregisterApp(); F.Add(new($"UnregisterApp {tag}", DiagState.Available, "registro deshecho: Windows queda como estaba")); }
                    catch (Exception e) { F.Add(new($"UnregisterApp {tag}", DiagState.Error, CallProbe.Describe(e) + " — ejecuta «Deshacer registro» en Ajustes")); }
                }
            }
        }
        return Render("Hatsu Connect · experimento HFP: registrar + conectar (reversible)", F, D);
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

    public static string Restore()
    {
        var F = new List<DiagFinding>();
        try
        {
            var devs = DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth)).AsTask().GetAwaiter().GetResult();
            foreach (var d in devs.Take(4))
            {
                var t = PhoneLineTransportDevice.FromId(d.Id); string tag = DiagIds.Tag(d.Id);
                try { if (t.IsRegistered()) { t.UnregisterApp(); F.Add(new($"UnregisterApp {tag}", DiagState.Available, "registro deshecho")); } else F.Add(new($"Transporte {tag}", DiagState.Detected, "esta app no estaba registrada")); }
                catch (Exception e) { F.Add(new($"UnregisterApp {tag}", CallProbe.Classify(e), CallProbe.Describe(e))); }
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
