using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// Diagnóstico DE SOLO LECTURA de Bluetooth y telefonía (<c>HatsuDesktop.Connect.exe --probe-calls [--out archivo]</c>; lo lanza Ajustes › Hatsu Connect › Diagnóstico).
/// No contesta, no cuelga, no marca, no conecta ni desconecta Bluetooth, no registra la app como cliente de la línea (<c>RegisterApp</c>), no conecta audio ni cambia nada del sistema
/// (los experimentos que sí cambian algo están aparte y piden confirmación: <see cref="HfpExperiments"/>). Identifica el transporte, las líneas, las llamadas activas por línea y los
/// endpoints de audio con identificadores ANONIMIZADOS pero correlacionables entre sí dentro del informe (docs/59).
/// </summary>
internal static class CallProbe
{
    public static async Task<(CallProbeReport Report, string Text)> RunAsync(string connectLogPath)
    {
        var F = new List<DiagFinding>(); var D = new List<string>(); void L(string s) => D.Add(s);
        string hatsu = typeof(CallProbe).Assembly.GetName().Version?.ToString(3) ?? "?";
        var header = new[] { $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm}", $"Windows: {Environment.OSVersion.VersionString}", $"Hatsu Desktop (Connect): {hatsu}", $"Identidad de paquete (Store/MSIX): {(HasPackageIdentity() ? "sí" : "no (ejecución sin empaquetar)")}" };

        bool pcm = false;
        try { pcm = true; F.Add(new("Estado de llamadas del sistema (PhoneCallManager)", DiagState.Available, $"responde; IsCallActive={PhoneCallManager.IsCallActive}, IsCallIncoming={PhoneCallManager.IsCallIncoming}")); }
        catch (Exception e) { F.Add(new("Estado de llamadas del sistema (PhoneCallManager)", Classify(e), Describe(e))); }

        // 1) Dispositivos Bluetooth emparejados (para poder relacionar transporte, línea y audio con UN dispositivo concreto)
        List<PhoneEnv.Paired> paired = [];
        try
        {
            paired = await PhoneEnv.PairedAsync();
            L("Dispositivos Bluetooth emparejados (identificadores anonimizados: el mismo código = el mismo dispositivo/contenedor):");
            foreach (var p in paired) L($"  #{p.N} id{p.IdTag} contenedor{p.ContainerTag} dir{p.AddrTag} · {(p.IsPhoneClass ? "clase teléfono" : "otra clase")} · {PhoneEnv.ConnLabel(p.Status)} · HFP-AG={p.Caps.HandsFreeAg} PBAP={p.Caps.Pbap} MAP={p.Caps.Map} A2DP={p.Caps.A2dpSource}{(p.Error is null ? "" : " · " + p.Error)}");
            int conn = paired.Count(p => p.Status == BluetoothConnectionStatus.Connected);
            F.Add(new("Dispositivos Bluetooth emparejados", paired.Count > 0 ? DiagState.Detected : DiagState.NotChecked, $"{paired.Count} emparejado(s), {conn} conectado(s) ahora; teléfonos con HFP-AG anunciado: {paired.Count(p => p.Caps.HandsFreeAg == CapabilityState.Advertised)}"));
        }
        catch (Exception e) { F.Add(new("Dispositivos Bluetooth emparejados", Classify(e), Describe(e))); }

        // 2) Líneas telefónicas: por el vigilante de líneas (no solo la «predeterminada», que daba 0x8007139F)
        int btLines = 0; bool lineApi = false;
        try
        {
            var store = await PhoneCallManager.RequestStoreAsync(); lineApi = true;
            try { var def = await store.GetDefaultLineAsync(); F.Add(new("Línea predeterminada del sistema", DiagState.Detected, def == Guid.Empty ? "ninguna" : "hay una")); }
            catch (Exception e) { F.Add(new("Línea predeterminada del sistema", Classify(e), Describe(e) + " — se interpreta como «no hay línea predeterminada en un estado utilizable» (inferido, no documentado)")); }
            var lines = await PhoneEnv.LinesAsync(paired, TimeSpan.FromSeconds(6));
            btLines = lines.Count(l => l.Transport == "Bluetooth");
            L(""); L("Líneas telefónicas que Windows expone a esta app (vigilante de líneas):");
            if (lines.Count == 0) L("  (ninguna)");
            foreach (var l in lines) L($"  línea{l.IdTag} · transporte={l.Transport} · dispositivo{l.DeviceIdTag}{(l.MatchesPaired is null ? "" : " = " + l.MatchesPaired)} · red={l.NetworkState} · puede marcar={l.CanDial} · llamadas activas: {l.CallsStatus}, {l.Calls.Count}");
            foreach (var l in lines) foreach (var c in l.Calls) L($"    llamada{c.IdTag} en línea{l.IdTag}: estado={c.Status} · audio={c.Audio} · silenciada={c.Muted} · dirección={c.Direction} · inicio={c.Started} · contacto={c.Name} · número={c.Number}");
            int calls = lines.Sum(l => l.Calls.Count);
            F.Add(new("Líneas telefónicas del sistema (vigilante)", lines.Count > 0 ? DiagState.Detected : DiagState.NotChecked, lines.Count == 0 ? "ninguna línea visible (no prueba incompatibilidad: depende de la sesión HFP y del registro)" : $"{lines.Count} línea(s), {btLines} con transporte Bluetooth"));
            F.Add(new("Llamadas activas por línea (PhoneLine.GetAllActivePhoneCalls)", calls > 0 ? DiagState.Detected : DiagState.NotChecked, calls > 0 ? $"{calls} llamada(s) visible(s): ver detalle" : "ninguna visible ahora (con una llamada real en el teléfono esto es lo que hay que mirar)"));
        }
        catch (Exception e) { F.Add(new("Líneas telefónicas del sistema (vigilante)", Classify(e), Describe(e))); }

        // 3) Transporte telefónico Bluetooth: qué es, a qué dispositivo corresponde
        bool granted = false; string? status = null;
        try
        {
            var tr = await PhoneEnv.TransportsAsync(paired, requestAccess: true);
            L(""); L("Transportes telefónicos Bluetooth (PhoneLineTransportDevice.GetDeviceSelector(Bluetooth)):");
            if (tr.Count == 0) L("  (ninguno)");
            foreach (var t in tr) L($"  transporte id{t.IdTag} contenedor{t.ContainerTag} · tipo={t.Kind} · habilitado={t.Enabled} · DeviceId{t.DeviceIdTag} · Transport={t.TransportKind} · registrado={(t.Registered?.ToString() ?? "?")} · AudioRoutingStatus={t.Routing} · InBandRinging={(t.InBand ?? "?")} · RequestAccessAsync={t.AccessOrError} · corresponde a: {t.MatchesPaired ?? "NINGÚN dispositivo emparejado (no se pudo relacionar)"}");
            granted = tr.Any(t => t.AccessOrError == nameof(DeviceAccessStatus.Allowed)); status = tr.FirstOrDefault()?.AccessOrError;
            if (tr.Count == 0) F.Add(new("Transporte telefónico Bluetooth", DiagState.NotChecked, "Windows no lista ninguno para esta app (no concluyente)"));
            else
            {
                var matched = tr.Count(t => t.MatchesPaired is not null);
                F.Add(new("Transporte telefónico Bluetooth", granted ? DiagState.Available : DiagState.AccessDenied,
                    $"{tr.Count} listado(s); RequestAccessAsync={string.Join("/", tr.Select(t => t.AccessOrError).Distinct())}; relacionados con un emparejado: {matched}. «Allowed» solo dice que Windows deja a esta app abrir el transporte; NO prueba que pueda registrarse, contestar ni mover audio"));
            }
        }
        catch (Exception e) { status = e.GetType().Name; F.Add(new("Transporte telefónico Bluetooth", Classify(e), Describe(e))); }

        // 4) Endpoints de audio: identidad y función
        int hf = 0;
        try
        {
            var eps = await PhoneEnv.EndpointsAsync(paired);
            hf = eps.Count(e => e.HandsFreeName);
            L(""); L("Endpoints de audio activos:");
            foreach (var e in eps) L($"  {e.Flow}: «{e.Name}» · contenedor{e.ContainerTag} · predeterminado={e.Default} · habilitado={e.Enabled} · bus={e.Enumerator}{(e.MatchesPaired is null ? " · NO pertenece a un dispositivo Bluetooth emparejado" : " · pertenece a " + e.MatchesPaired)}");
            int bt = eps.Count(e => e.MatchesPaired is not null);
            F.Add(new("Endpoints de audio", DiagState.Detected, $"{eps.Count(e => e.Flow == "salida")} de salida y {eps.Count(e => e.Flow == "entrada")} de entrada; de dispositivos Bluetooth emparejados: {bt}; con nombre «Hands-Free»: {hf}. Un endpoint no es audio telefónico confirmado"));
        }
        catch (Exception e) { F.Add(new("Endpoints de audio", Classify(e), Describe(e))); }

        F.Add(new("Observador de llamadas del sistema", DiagState.NotChecked, "se activa a mano desde Ajustes (temporal, solo lectura); líneas recientes del registro abajo"));
        F.Add(new("Contestar / rechazar / colgar desde Hatsu", DiagState.NotChecked, "API PhoneCall.AcceptIncoming/RejectIncoming/End existe en el SDK; sin probar con llamada real: no hay botones (docs/59)"));
        F.Add(new("Audio bidireccional PC ↔ teléfono", DiagState.NotChecked, "solo se confirma con una llamada real, a mano"));

        D.Add(""); D.Add("Registro reciente del observador (sin contenido):");
        try { if (File.Exists(connectLogPath)) foreach (var l in File.ReadLines(connectLogPath).Where(l => l.Contains("telefonía del sistema") || l.Contains("llamada")).TakeLast(30)) D.Add("  " + l); else D.Add("  (sin registro todavía)"); }
        catch (Exception e) { D.Add("  (no legible: " + e.GetType().Name + ")"); }

        var rep = new CallProbeReport(pcm, lineApi, btLines, granted, status, hf, BidirectionalAudioVerified: false);
        var text = DiagReport.Compose("Hatsu Connect · diagnóstico de Bluetooth y llamadas", header, F, D);
        return (rep, DiagRedactor.Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName));
    }

    internal static DiagState Classify(Exception e) => e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005) ? DiagState.AccessDenied : e is PlatformNotSupportedException or NotImplementedException or MissingMethodException ? DiagState.NotSupported : DiagState.Error;
    internal static string Describe(Exception e) => $"{e.GetType().Name} 0x{e.HResult:X8} — {DiagIds.HResultName(e.HResult)}";
    internal static bool HasPackageIdentity() { try { _ = Windows.ApplicationModel.Package.Current.Id; return true; } catch { return false; } }
}
