using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace WinMac.Connect.App;

/// <summary>Lectura (SOLO LECTURA) del entorno telefónico y de audio de Windows con identificadores correlacionables pero anonimizados (<see cref="DiagIds"/>).</summary>
internal static class PhoneEnv
{
    private static readonly string[] ContainerProps = ["System.Devices.ContainerId", "System.Devices.Aep.ContainerId", "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired"];

    public sealed record Paired(int N, string IdTag, string ContainerTag, string AddrTag, bool IsPhoneClass, BluetoothConnectionStatus Status, PhoneCapabilities Caps, string? Error);
    public sealed record Transport(string IdTag, string ContainerTag, string Kind, bool Enabled, string DeviceIdTag, string TransportKind, bool? Registered, string Routing, string? InBand, string AccessOrError, string? MatchesPaired);
    public sealed record Line(string IdTag, string Transport, string DeviceIdTag, string NetworkState, bool CanDial, string? MatchesPaired, string CallsStatus, IReadOnlyList<CallView> Calls);
    public sealed record CallView(string IdTag, string Status, string Audio, bool Muted, string Direction, string Name, string Number, string Started);
    public sealed record Endpoint(string Flow, string Name, string ContainerTag, bool Default, bool Enabled, string Enumerator, string? MatchesPaired, bool HandsFreeName);

    private static string? Container(DeviceInformation d) =>
        d.Properties.TryGetValue("System.Devices.ContainerId", out var a) && a is not null ? a.ToString() :
        d.Properties.TryGetValue("System.Devices.Aep.ContainerId", out var b) && b is not null ? b.ToString() : null;

    public static async Task<List<Paired>> PairedAsync()
    {
        var res = new List<Paired>();
        var devs = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), ContainerProps);
        int i = 0;
        foreach (var d in devs.Take(12))
        {
            i++; BluetoothDevice? bt = null;
            try
            {
                bt = await BluetoothDevice.FromIdAsync(d.Id);
                if (bt is null) { res.Add(new(i, DiagIds.Tag(d.Id), DiagIds.Tag(Container(d)), "-", false, BluetoothConnectionStatus.Disconnected, PhoneCapabilities.From([], false), "no se pudo abrir")); continue; }
                var r = await bt.GetRfcommServicesAsync(BluetoothCacheMode.Cached);   // caché: no abre conexiones nuevas
                var caps = PhoneCapabilities.From(r.Services.Select(s => s.ServiceId.Uuid), r.Error == BluetoothError.Success);
                res.Add(new(i, DiagIds.Tag(d.Id), DiagIds.Tag(Container(d)), DiagIds.Tag(bt.BluetoothAddress.ToString("X")), bt.ClassOfDevice.MajorClass == BluetoothMajorClass.Phone, bt.ConnectionStatus, caps, null));
            }
            catch (Exception e) { res.Add(new(i, DiagIds.Tag(d.Id), DiagIds.Tag(Container(d)), "-", false, BluetoothConnectionStatus.Disconnected, PhoneCapabilities.From([], false), e.GetType().Name + " 0x" + e.HResult.ToString("X8"))); }
            finally { bt?.Dispose(); }
        }
        return res;
    }

    private static string? Match(IEnumerable<Paired> paired, string? deviceIdTag, string? containerTag)
    {
        var p = paired.FirstOrDefault(x => (deviceIdTag is not null && deviceIdTag != "-" && (x.IdTag == deviceIdTag || x.AddrTag == deviceIdTag)) || (containerTag is not null && containerTag != "-" && x.ContainerTag == containerTag));
        return p is null ? null : $"emparejado #{p.N} ({(p.Status == BluetoothConnectionStatus.Connected ? "conectado" : "desconectado")}, {(p.IsPhoneClass ? "clase teléfono" : "otra clase")})";
    }

    public static async Task<List<Transport>> TransportsAsync(IReadOnlyList<Paired> paired, bool requestAccess)
    {
        var res = new List<Transport>();
        var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth), ContainerProps);
        foreach (var d in devs.Take(6))
        {
            string access = "no pedido"; PhoneLineTransportDevice? t = null; string devIdTag = "-"; string tk = "?"; bool? reg = null; string routing = "?"; string? inband = null;
            try
            {
                t = PhoneLineTransportDevice.FromId(d.Id);
                devIdTag = DiagIds.Tag(t.DeviceId); tk = t.Transport.ToString();
                try { reg = t.IsRegistered(); } catch (Exception) { }
                try { routing = t.AudioRoutingStatus.ToString(); } catch (Exception e) { routing = "error " + e.GetType().Name; }
                try { inband = t.InBandRingingEnabled.ToString(); } catch (Exception) { }
                if (requestAccess) access = (await t.RequestAccessAsync()).ToString();   // pide acceso al dispositivo: no registra la app ni conecta nada
            }
            catch (Exception e) { access = "error " + e.GetType().Name + " 0x" + e.HResult.ToString("X8"); }
            string ct = DiagIds.Tag(Container(d));
            res.Add(new(DiagIds.Tag(d.Id), ct, d.Kind.ToString(), d.IsEnabled, devIdTag, tk, reg, routing, inband, access, Match(paired, devIdTag, ct)));
        }
        return res;
    }

    public static async Task<List<Line>> LinesAsync(IReadOnlyList<Paired> paired, TimeSpan wait)
    {
        var res = new List<Line>(); var ids = new List<Guid>();
        var store = await PhoneCallManager.RequestStoreAsync();
        var w = store.RequestLineWatcher(); var done = new TaskCompletionSource();
        w.LineAdded += (_, e) => { lock (ids) ids.Add(e.LineId); };
        w.EnumerationCompleted += (_, _) => done.TrySetResult();
        w.Start();
        try { await Task.WhenAny(done.Task, Task.Delay(wait)); } finally { try { w.Stop(); } catch (Exception) { } }
        Guid[] snap; lock (ids) snap = ids.Distinct().ToArray();
        foreach (var id in snap.Take(8))
        {
            try
            {
                var l = await PhoneLine.FromIdAsync(id); string tdev = "-"; try { tdev = DiagIds.Tag(l.TransportDeviceId); } catch (Exception) { }
                var calls = new List<CallView>(); string cs;
                try
                {
                    var r = await l.GetAllActivePhoneCallsAsync(); cs = r.OperationStatus.ToString();
                    foreach (var c in r.AllActivePhoneCalls)
                    {
                        string dir = "?", name = "-", num = "-", st = "?";
                        try { var inf = await c.GetPhoneCallInfoAsync(); dir = inf.CallDirection.ToString(); name = DiagIds.MaskName(inf.DisplayName); num = DiagIds.MaskNumber(inf.PhoneNumber); st = inf.StartTime.ToString("HH:mm:ss"); } catch (Exception) { }
                        calls.Add(new(DiagIds.Tag(c.CallId.ToString()), c.Status.ToString(), c.AudioDevice.ToString(), c.IsMuted, dir, name, num, st));
                    }
                }
                catch (Exception e) { cs = "error " + e.GetType().Name + " 0x" + e.HResult.ToString("X8"); }
                res.Add(new(DiagIds.Tag(id.ToString()), l.Transport.ToString(), tdev, l.NetworkState.ToString(), l.CanDial, Match(paired, tdev, null), cs, calls));
            }
            catch (Exception e) { res.Add(new(DiagIds.Tag(id.ToString()), "?", "-", "?", false, null, "error " + e.GetType().Name + " 0x" + e.HResult.ToString("X8"), [])); }
        }
        return res;
    }

    public static async Task<List<Endpoint>> EndpointsAsync(IReadOnlyList<Paired> paired)
    {
        var res = new List<Endpoint>();
        foreach (var (sel, flow, defId) in new[] { (MediaDevice.GetAudioRenderSelector(), "salida", MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default)), (MediaDevice.GetAudioCaptureSelector(), "entrada", MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default)) })
        {
            var all = await DeviceInformation.FindAllAsync(sel, ContainerProps);
            foreach (var d in all.Take(16))
            {
                string ct = DiagIds.Tag(Container(d)); var m = Match(paired, null, ct);
                string inst = "?"; try { if (d.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var v) && v is string str) inst = str.Split('\\').FirstOrDefault() ?? "?"; } catch (Exception) { }
                bool hf = d.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Manos libres", StringComparison.OrdinalIgnoreCase);
                res.Add(new(flow, m is null ? d.Name : "<dispositivo Bluetooth emparejado>", ct, d.Id == defId, d.IsEnabled, inst, m, hf));
            }
        }
        return res;
    }

    public static string ConnLabel(BluetoothConnectionStatus s) => s == BluetoothConnectionStatus.Connected ? "conectado" : "desconectado";
}
