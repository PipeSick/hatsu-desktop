using System.Diagnostics;
using CallProbe.Logic;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace CallProbe;

/// <summary>
/// Instrumentación de SOLO LECTURA: muestrea el transporte con marcas de tiempo, sin registrar, conectar ni tocar audio.
/// Cada lectura lleva tiempo máximo; un valor ilegible se anota como «n/d», nunca como False.
/// </summary>
internal static class Observer
{
    private static async Task<T?> ReadAsync<T>(Func<T> read, string? errorSink = null) where T : class
    {
        var r = await StepGuard.RunAsync("lectura", () => Task.FromResult(read()), TimeSpan.FromSeconds(3));
        return r.Ok ? r.Value : null;
    }

    public static async Task<TimelineSample> SampleAsync(Transport t, string label, Stopwatch clock, int? lineCount = null, bool print = true)
    {
        var reg = await StepGuard.RunAsync("IsRegistered", () => Task.FromResult(t.Device.IsRegistered()), TimeSpan.FromSeconds(3));
        var audio = await StepGuard.RunAsync("AudioRoutingStatus", () => Task.FromResult(t.Device.AudioRoutingStatus.ToString()), TimeSpan.FromSeconds(3));
        bool? classic = await Calls.ClassicConnectedAsync(t.Address);
        string? err = reg.Ok && audio.Ok ? null : string.Join("; ", new[] { reg.Ok ? null : "IsRegistered: " + Hr(reg), audio.Ok ? null : "Audio: " + Hr(audio) }.Where(x => x is not null));
        var s = new TimelineSample(Math.Round(clock.Elapsed.TotalSeconds, 2), label, reg.Ok ? reg.Value : null, audio.Ok ? audio.Value : null, classic, lineCount, PhoneCallManager.IsCallActive, PhoneCallManager.IsCallIncoming, err);
        if (print) Report.Stamp($"[t={s.Seconds:0.00}s {label}] IsRegistered={s.IsRegistered?.ToString() ?? "n/d"} · AudioRoutingStatus={s.AudioRouting ?? "n/d"} · Classic={s.ClassicConnected?.ToString() ?? "n/d"} · llamada={(s.CallActive ? "activa" : s.CallIncoming ? "entrante" : "no")}{(err is null ? "" : " · ERROR " + err)}");
        return s;
    }

    private static string Hr(StepResult<bool> r) => r.Describe();
    private static string Hr(StepResult<string> r) => r.Describe();

    public static void PrintSummary(IReadOnlyList<TimelineSample> samples, double? registerAt)
    {
        var sum = TimelineAnalyzer.Summarize(samples, registerAt);
        Report.Title("Evolución temporal (solo lectura)");
        Report.Item("Cambios observados", sum.Changes.Count == 0 ? "ninguno" : "");
        foreach (var c in sum.Changes) Report.Line("    " + c);
        Report.Item("AudioRoutingStatus", $"{sum.Audio}: {sum.AudioReading}");
        foreach (var l in sum.Limits) Report.Line("  Límite de interpretación: " + l);
    }

    /// <summary>Qué contratos y miembros de la API existen en ESTA build (ApiInformation: consulta pura).</summary>
    public static void PrintApiPresence()
    {
        Report.Title("Presencia de API en esta build (ApiInformation, solo lectura)");
        const string ns = "Windows.ApplicationModel.Calls";
        try
        {
            var contracts = Enumerable.Range(1, 8).Select(v => $"v{v}={ApiInformation.IsApiContractPresent(ns + ".CallsPhoneContract", (ushort)v)}");
            Report.Item("CallsPhoneContract", string.Join(" ", contracts));
            Report.Item("tipo PhoneCall", ApiInformation.IsTypePresent(ns + ".PhoneCall"));
            Report.Item("PhoneLineTransportDevice.AudioRoutingStatus", ApiInformation.IsPropertyPresent(ns + ".PhoneLineTransportDevice", "AudioRoutingStatus"));
            Report.Item("PhoneLineTransportDevice.RegisterApp", ApiInformation.IsMethodPresent(ns + ".PhoneLineTransportDevice", "RegisterApp"));
            Report.Item("PhoneLineTransportDevice.RegisterAppForUser", ApiInformation.IsMethodPresent(ns + ".PhoneLineTransportDevice", "RegisterAppForUser"));
        }
        catch (Exception e) { Report.Item("ApiInformation", e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>
    /// Propiedades de la interfaz de dispositivo del transporte. Solo se imprimen valores simples (bool/número/enum); lo demás (ids, direcciones) se omite.
    /// «System.Devices.PhoneLineTransportDevice.Connected» NO está documentada: la mencionó un tercero (MyPhone #26); aquí solo se lee.
    /// </summary>
    public static async Task PrintTransportPropertiesAsync(Transport t)
    {
        Report.Title("Propiedades del transporte (solo lectura; «Connected» no está documentada)");
        string[] keys = ["System.Devices.PhoneLineTransportDevice.Connected", "System.Devices.InterfaceEnabled", "System.Devices.Connected", "System.Devices.Present"];
        var r = await Steps.Run("DeviceInformation.CreateFromIdAsync(transporte)", async () => await DeviceInformation.CreateFromIdAsync(t.Device.DeviceId, keys), 10);
        if (!r.Ok) { Report.Line("  " + r.Describe()); return; }
        foreach (var k in keys)
        {
            var v = r.Value!.Properties.TryGetValue(k, out var o) ? o : null;
            Report.Item("    " + k.Replace("System.Devices.", ""), v is null ? "(sin valor)" : v is bool or int or uint or long or ulong ? v : $"({v.GetType().Name}, omitido)");
        }
    }

    /// <summary>Se suscribe a los eventos de solo lectura del transporte para tener marcas de tiempo exactas de cada cambio (no cambia nada). Devuelve la función que anula la suscripción.</summary>
    public static Action Subscribe(Transport t, Stopwatch clock)
    {
        Windows.Foundation.TypedEventHandler<PhoneLineTransportDevice, object> a = (d, _) => { try { Report.Stamp($"[t={clock.Elapsed.TotalSeconds:0.00}s EVENTO] AudioRoutingStatusChanged → {d.AudioRoutingStatus}"); } catch (Exception e) { Report.Stamp("EVENTO AudioRoutingStatusChanged (" + e.GetType().Name + ")"); } };
        Windows.Foundation.TypedEventHandler<PhoneLineTransportDevice, object> b = (d, _) => { try { Report.Stamp($"[t={clock.Elapsed.TotalSeconds:0.00}s EVENTO] InBandRingingEnabledChanged → {d.InBandRingingEnabled}"); } catch (Exception e) { Report.Stamp("EVENTO InBandRingingEnabledChanged (" + e.GetType().Name + ")"); } };
        try { t.Device.AudioRoutingStatusChanged += a; t.Device.InBandRingingEnabledChanged += b; }
        catch (Exception e) { Report.Item("Suscripción a eventos del transporte", e.GetType().Name + " 0x" + e.HResult.ToString("X8")); return () => { }; }
        return () => { try { t.Device.AudioRoutingStatusChanged -= a; t.Device.InBandRingingEnabledChanged -= b; } catch { } };
    }

    /// <summary>Observación pura durante N segundos. NO registra ni conecta nada.</summary>
    public static async Task<int> ObserveAsync(string? nameFilter, int seconds, int intervalMs, bool lines = false)
    {
        Report.Title($"Observación de solo lectura durante {seconds} s (cada {intervalMs} ms). No registra, no conecta, no toca audio.");
        var t = await Calls.FindTransportAsync(nameFilter); if (t is null) return 3;
        Report.Item("Identidad de paquete / firma", $"{Native.HasPackageIdentity()} / {Native.SignatureKind() ?? "(sin paquete)"}");
        Report.Item("Windows", Native.WindowsBuild()); Report.Item("Phone Link en ejecución", Native.PhoneLinkRunning());
        PrintApiPresence(); await PrintTransportPropertiesAsync(t);
        if (lines) { var l = await Calls.BluetoothLinesAsync(); Report.Item("Líneas Bluetooth visibles (sin registrar; la documentación dice que el registro es necesario para verlas)", l.Count); }
        Report.Title("Muestras");
        var clock = Stopwatch.StartNew(); var all = new List<TimelineSample>(); var unsub = Subscribe(t, clock);
        while (clock.Elapsed.TotalSeconds < seconds && !Steps.Cancel.IsCancellationRequested) { all.Add(await SampleAsync(t, "obs", clock)); await Steps.DelayAsync(intervalMs); }
        unsub(); PrintSummary(all, null);
        return 0;
    }
}
