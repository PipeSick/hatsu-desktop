using WinMac.Bar;
using WinMac.Bar.Hub;
using WinMac.SystemControl;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// ───────────── Servicios simulados: registran las llamadas y devuelven lo que la prueba decida (o lanzan).

internal abstract class FakeBase
{
    public List<string> Calls { get; } = [];
    public ServiceResult Next { get; set; } = ServiceResult.Ok;
    public Exception? Throw { get; set; }
    protected ServiceResult Result(string call) { Calls.Add(call); if (Throw is not null) throw Throw; return Next; }
    protected void Record(string call) { Calls.Add(call); if (Throw is not null) throw Throw; }
}

internal sealed class FakeAudio : FakeBase, IAudioService, IOutputRestore
{
    public AudioState State { get; set; } = new(true, 40, false, [new("spk", "Altavoces", true), new("hp", "Auriculares", false)]);
    public string? PreviousDefaultId { get; set; }
    public AudioState Read() { Record("Read"); return State; }
    public ServiceResult SetVolume(int percent) => Result($"SetVolume {percent}");
    public ServiceResult SetMuted(bool muted) => Result($"SetMuted {muted}");
    public ServiceResult SetDefaultOutput(string deviceId) => Result($"SetDefaultOutput {deviceId}");
    public ServiceResult CanSwitchOutput() => ServiceResult.Ok;
    public AudioInputState Input { get; set; } = new(true, 50, false, true, [new("mic", "Micrófono", true)]);
    public AudioSessionsState Sessions { get; set; } = new(true, [new("pid:10", 10, "Spotify", @"C:\Spotify.exe", 80, false, false, true), new("system", 0, "Sonidos del sistema", null, 100, false, true, false)]);
    public AudioInputState ReadInput() { Record("ReadInput"); return Input; }
    public ServiceResult SetInputVolume(int percent) => Result($"SetInputVolume {percent}");
    public ServiceResult SetInputMuted(bool muted) => Result($"SetInputMuted {muted}");
    public ServiceResult SetDefaultInput(string deviceId) => Result($"SetDefaultInput {deviceId}");
    public AudioSessionsState ReadSessions() { Record("ReadSessions"); return Sessions; }
    public ServiceResult SetSessionVolume(string sessionId, int percent) => Result($"SetSessionVolume {sessionId} {percent}");
    public ServiceResult SetSessionMuted(string sessionId, bool muted) => Result($"SetSessionMuted {sessionId} {muted}");
}

internal sealed class FakeWifi : FakeBase, IWifiService
{
    public WifiState State { get; set; } = new(true, true, "Casa", 90, [new("Casa", 90, true, true, true, "Casa"), new("Vecino", 60, true, false, true, "PerfilVecino"), new("Nueva", 40, true, false, false, null)], RadioOn: true);
    public WifiState Read(bool scan) { Record($"Read scan={scan}"); return State; }
    public ServiceResult Connect(string profileName, string ssid) => Result($"Connect {profileName}/{ssid}");
    public ServiceResult Disconnect() => Result("Disconnect");
    public ServiceResult SetRadio(bool on) => Result($"SetRadio {on}");
}

internal sealed class FakeBluetooth : FakeBase, IBluetoothService
{
    public BluetoothState State { get; set; } = new(true, true, [new("1", "Audífonos", true)]);
    public BluetoothState Read(bool withDevices = true) { Record($"Read devices={withDevices}"); return State; }
    public ServiceResult SetRadio(bool on) => Result($"SetRadio {on}");
}

internal sealed class FakeBrightness : FakeBase, IBrightnessService
{
    public BrightnessState State { get; set; } = new(true, [new("m1", "Monitor 1", 50, "DDC/CI"), new("m2", "Monitor 2", 50, "DDC/CI")]);
    public BrightnessState Read() { Record("Read"); return State; }
    public ServiceResult Set(string monitorId, int percent) => Result($"Set {monitorId} {percent}");
}

internal sealed class FakePower : FakeBase, IPowerService
{
    public PowerModeState State { get; set; } = new(true, PowerMode.BestPerformance);
    public PowerModeState ReadMode() { Record("Read"); return State; }
    public ServiceResult SetMode(PowerMode mode) => Result($"SetMode {mode}");
}

internal sealed class FakeAppearance : FakeBase, IAppearanceService
{
    public AppearanceState State { get; set; } = new(true, true);
    public AppearanceState Read() { Record("Read"); return State; }
    public ServiceResult SetDark(bool dark) => Result($"SetDark {dark}");
}

internal sealed class FakeDisplay : FakeBase, IDisplayService
{
    public ServiceResult SetTopology(DisplayTopology topology) => Result($"SetTopology {topology}");
}

internal sealed class FakeSession : FakeBase, ISessionService
{
    public ServiceResult Run(SystemActionKind action) => Result($"Run {action}");
    public ServiceResult ForceQuitForeground() => Result("ForceQuit");
    public ServiceResult OpenNotificationCenter() => Result("OpenNotificationCenter");
}

internal sealed class FakeUi(SystemHub hub) : IPanelUi
{
    public PanelContext Ctx { get; set; } = PanelContext.Initial(new BarConfig(), new DateTime(2026, 11, 1, 12, 0, 0));
    public int Rebuilds, Hides; public List<PanelKind> Navigations { get; } = []; public List<(string Title, string? Text)> Infos { get; } = []; public List<string> Uris { get; } = [];
    public void Rebuild() { Rebuilds++; Ctx = Ctx with { Sys = hub.Snapshot }; }   // igual que la ventana real: el panel se reconstruye con el estado actual
    public void Navigate(PanelKind kind) => Navigations.Add(kind);
    public void HidePanel() => Hides++;
    public void ShowInfo(string title, string? text) => Infos.Add((title, text));
    public void OpenUri(string uri) => Uris.Add(uri);
    // panel multifunción
    public List<HubTab> TabChanges { get; } = []; public List<string?> SettingsOpened { get; } = []; public int Quits; public ConfirmIcon LastIcon; public List<object> Patches { get; } = []; public bool ConfirmAnswer = true; public List<(string Title, string Text)> Confirms { get; } = [];
    public List<(string Title, IReadOnlyList<Choice> Choices)> Menus { get; } = []; public string? PickNext { get; set; }
    public void TabChanged(HubTab tab) => TabChanges.Add(tab);
    public void OpenSettings(string? section) => SettingsOpened.Add(section);
    public void QuitWinMac() => Quits++;
    public void ShowChoices(string title, IReadOnlyList<Choice> choices, Action<string> onPick) { Menus.Add((title, choices)); if (PickNext is not null) onPick(PickNext); }
    public void PatchConfig(object patch) => Patches.Add(patch);
    public bool Confirm(string title, string text, string action = "Continuar", bool danger = false, ConfirmIcon icon = ConfirmIcon.Warning) { LastIcon = icon; Confirms.Add((title, text)); LastConfirmAction = action; return ConfirmAnswer; }
    public string? LastConfirmAction;
}

/// <summary>Conjunto de servicios simulados, planificador determinista (el trabajo en segundo plano y los avisos al hilo de la barra se ejecutan cuando la prueba lo decide) y las acciones de los paneles.</summary>
internal sealed class Rig
{
    public readonly FakeAudio Audio = new(); public readonly FakeWifi Wifi = new(); public readonly FakeBluetooth Bt = new(); public readonly FakeBrightness Brightness = new();
    public readonly FakePower Power = new(); public readonly FakeAppearance Appearance = new(); public readonly FakeDisplay Display = new(); public readonly FakeSession Session = new();
    public readonly Queue<Action> Background = new(), UiThread = new(); public readonly List<string> Log = [];
    public readonly SystemHub Hub; public readonly FakeUi Ui; public PanelOperations Ops; public int Changed;

    public Rig(BarConfig? cfg = null)
    {
        Hub = new SystemHub(new SystemServices(Audio, Wifi, Bt, Brightness, Power, Appearance, Display, Session), a => UiThread.Enqueue(a), Log.Add, a => Background.Enqueue(a));
        Hub.Changed += () => Changed++;
        Ui = new FakeUi(Hub); if (cfg is not null) Ui.Ctx = Ui.Ctx with { Cfg = cfg };
        Ops = new PanelOperations(Hub, Ui, Log.Add, TimeSpan.Zero);
    }

    /// <summary>Ejecuta el trabajo pendiente en segundo plano y los avisos al hilo de la barra hasta que no quede nada.</summary>
    public void Pump() { while (Background.Count > 0 || UiThread.Count > 0) { while (Background.Count > 0) Background.Dequeue()(); while (UiThread.Count > 0) UiThread.Dequeue()(); } }

    /// <summary>Lee todo una vez, como al abrir el Control Center.</summary>
    public void LoadAll() { Hub.RefreshAudio(); Hub.RefreshWifi(true); Hub.RefreshBluetooth(true); Hub.RefreshFast(); Hub.RefreshBrightness(true); Pump(); Ui.Rebuild(); }

    public void Click(string id) => Ops.Handle(new PanelEvent(PanelEventKind.Click, id));
    public void Slide(string id, double v, bool commit = false) => Ops.Handle(new PanelEvent(commit ? PanelEventKind.SliderCommit : PanelEventKind.SliderChange, id, v));
}

public class SystemHubTests
{
    [Fact]
    public void A_refresh_reads_off_the_ui_thread_and_notifies_once_only_when_something_visible_changed()
    {
        var r = new Rig(); Assert.False(r.Hub.Snapshot.Audio.Available);   // «Leyendo…» hasta la primera lectura
        r.Hub.RefreshAudio(); Assert.Empty(r.Audio.Calls);                  // nada se ejecuta en el hilo que pide: va a segundo plano
        r.Pump(); Assert.Equal(1, r.Changed); Assert.Equal(40, r.Hub.Snapshot.Audio.Volume);
        r.Hub.RefreshAudio(); r.Pump(); Assert.Equal(2, r.Audio.Calls.Count); Assert.Equal(1, r.Changed);   // misma lectura: no se vuelve a dibujar
        r.Audio.State = r.Audio.State with { Volume = 41 }; r.Hub.RefreshAudio(); r.Pump(); Assert.Equal(2, r.Changed);
    }

    [Fact]
    public void Reads_are_single_flight_a_second_request_while_one_is_running_is_dropped()
    {
        var r = new Rig();
        r.Hub.RefreshAudio(); r.Hub.RefreshAudio(); r.Hub.RefreshWifi(false); r.Hub.RefreshWifi(true);
        r.Pump(); Assert.Equal(["Read"], r.Audio.Calls); Assert.Equal(["Read scan=False"], r.Wifi.Calls);
        r.Hub.RefreshAudio(); r.Pump(); Assert.Equal(2, r.Audio.Calls.Count);   // al terminar la anterior ya se puede pedir otra
    }

    [Fact]
    public void A_failing_service_never_propagates_keeps_the_last_state_and_does_not_block_later_reads()
    {
        var r = new Rig(); r.Hub.RefreshAudio(); r.Pump();
        r.Audio.Throw = new InvalidOperationException("COM roto"); r.Hub.RefreshAudio(); r.Pump();
        Assert.Equal(40, r.Hub.Snapshot.Audio.Volume); Assert.Contains(r.Log, l => l.Contains("COM roto"));
        r.Audio.Throw = null; r.Audio.State = r.Audio.State with { Volume = 10 }; r.Hub.RefreshAudio(); r.Pump(); Assert.Equal(10, r.Hub.Snapshot.Audio.Volume);   // la bandera de «en curso» se liberó
    }

    [Fact]
    public void Power_and_theme_are_published_without_waiting_for_the_slow_brightness_probe()
    {
        var r = new Rig();
        r.Hub.RefreshBrightness(true);   // sondeo lento en curso (todavía en cola)
        r.Hub.RefreshFast();
        // se ejecuta SOLO el trabajo rápido: el brillo sigue sin leerse y aun así la energía y el tema ya están
        var queued = r.Background.ToArray(); r.Background.Clear();
        queued[1](); while (r.UiThread.Count > 0) r.UiThread.Dequeue()();
        Assert.Empty(r.Brightness.Calls); Assert.Equal(PowerMode.BestPerformance, r.Hub.Snapshot.Power.Mode); Assert.True(r.Hub.Snapshot.Appearance.Dark);
        queued[0](); while (r.UiThread.Count > 0) r.UiThread.Dequeue()(); Assert.True(r.Hub.Snapshot.Brightness.Available);
    }

    [Fact]
    public void Fast_reads_are_not_dropped_while_the_brightness_probe_is_still_running()
    {
        var r = new Rig();
        r.Hub.RefreshBrightness(true); r.Hub.RefreshFast(); r.Hub.RefreshFast();   // el 2.º rápido se descarta (ya hay uno), pero NO por culpa del brillo
        Assert.Equal(2, r.Background.Count);
    }

    [Fact]
    public void An_unavailable_brightness_is_not_probed_again_for_a_while_unless_forced()
    {
        var r = new Rig(); r.Brightness.State = BrightnessState.None("sin DDC/CI");
        r.Hub.RefreshBrightness(false); r.Pump(); Assert.Single(r.Brightness.Calls);     // primera vez: se mide
        r.Hub.RefreshBrightness(false); r.Pump(); Assert.Single(r.Brightness.Calls);     // ya dijo «no disponible»: no se repite el sondeo de varios segundos
        r.Hub.RefreshBrightness(true); r.Pump(); Assert.Equal(2, r.Brightness.Calls.Count);   // forzado (p. ej. tras mover el deslizador): sí
    }

    [Fact]
    public void A_slider_applies_only_the_last_value_one_at_a_time_and_reports_the_last_result()
    {
        var r = new Rig(); var applied = new List<int>(); ServiceResult? done = null; int doneCalls = 0;
        foreach (int v in new[] { 10, 20, 30 }) r.Hub.SetLatest("volume", v, x => { applied.Add(x); return ServiceResult.Ok; }, res => { done = res; doneCalls++; });
        r.Pump(); Assert.Equal([30], applied); Assert.Equal(1, doneCalls); Assert.True(done!.Value.IsOk);   // 10 y 20 se coalescen
        r.Hub.SetLatest("volume", 5, x => { applied.Add(x); return ServiceResult.Failed("nope"); }, res => done = res); r.Pump();
        Assert.Equal([30, 5], applied); Assert.False(done!.Value.IsOk); Assert.Equal("nope", done.Value.Message);
    }

    [Fact]
    public void When_slider_moves_pile_up_the_notice_of_the_LAST_one_is_the_one_delivered()
    {
        var r = new Rig(); var who = new List<string>();
        r.Hub.SetLatest("brightness", 10, _ => ServiceResult.Ok, _ => who.Add("primero"));
        r.Hub.SetLatest("brightness", 20, _ => ServiceResult.Ok, _ => who.Add("intermedio"));
        r.Hub.SetLatest("brightness", 30, _ => ServiceResult.Ok, _ => who.Add("soltar"));   // el que relee el valor real al soltar el deslizador
        r.Pump(); Assert.Equal(["soltar"], who);
    }

    [Fact]
    public void An_exception_in_a_slider_or_an_operation_becomes_a_failed_result_not_a_crash()
    {
        var r = new Rig(); ServiceResult? a = null, b = null;
        r.Hub.SetLatest("brightness", 1, _ => throw new InvalidOperationException("DDC"), x => a = x);
        r.Hub.Run(() => throw new TimeoutException("lento"), x => b = x); r.Pump();
        Assert.False(a!.Value.IsOk); Assert.Contains("DDC", a.Value.Message); Assert.False(b!.Value.IsOk); Assert.Contains("lento", b.Value.Message);
    }

    [Fact]
    public void A_result_is_delivered_on_the_bar_thread_not_in_the_worker()
    {
        var r = new Rig(); bool delivered = false; r.Hub.Run(() => ServiceResult.Ok, _ => delivered = true);
        r.Background.Dequeue()(); Assert.False(delivered);   // el trabajo terminó pero la entrega espera al hilo de la barra
        r.UiThread.Dequeue()(); Assert.True(delivered);
    }

    [Fact]
    public void Patch_shows_what_the_user_asked_immediately_and_the_real_reading_corrects_it()
    {
        var r = new Rig(); r.LoadAll(); int before = r.Changed;
        r.Hub.Patch(s => s with { Audio = s.Audio with { Volume = 77 } }); r.Pump(); Assert.Equal(77, r.Hub.Snapshot.Audio.Volume); Assert.True(r.Changed > before);
        r.Hub.RefreshAudio(); r.Pump(); Assert.Equal(40, r.Hub.Snapshot.Audio.Volume);   // la lectura real manda
    }

    [Fact]
    public void Restoring_the_previous_output_is_offered_only_while_that_device_still_exists_and_is_not_the_default()
    {
        var r = new Rig(); r.Audio.PreviousDefaultId = "hp"; r.Hub.RefreshAudio(); r.Pump();
        Assert.True(r.Hub.Snapshot.CanRestoreOutput); Assert.Equal("hp", r.Hub.PreviousOutputId);
        r.Audio.State = new AudioState(true, 40, false, [new("spk", "Altavoces", false), new("hp", "Auriculares", true)]); r.Hub.RefreshAudio(); r.Pump(); Assert.False(r.Hub.Snapshot.CanRestoreOutput);   // ya es la predeterminada
        r.Audio.State = new AudioState(true, 40, false, [new("spk", "Altavoces", true)]); r.Hub.RefreshAudio(); r.Pump(); Assert.False(r.Hub.Snapshot.CanRestoreOutput);   // el dispositivo ya no existe
    }

    [Fact]
    public void A_bluetooth_poll_without_devices_keeps_the_device_list_already_known()
    {
        var r = new Rig(); r.Hub.RefreshBluetooth(true); r.Pump(); Assert.Single(r.Hub.Snapshot.Bluetooth.Devices);
        r.Bt.State = new BluetoothState(true, true, []); r.Hub.RefreshBluetooth(false); r.Pump(); Assert.Single(r.Hub.Snapshot.Bluetooth.Devices);   // el sondeo barato del icono no borra la lista
        r.Hub.RefreshBluetooth(true); r.Pump(); Assert.Empty(r.Hub.Snapshot.Bluetooth.Devices);   // la lectura completa sí
    }
}

public class PanelOperationsTests
{
    private static Rig Loaded(BarConfig? cfg = null) { var r = new Rig(cfg); r.LoadAll(); return r; }

    [Fact]
    public void Settings_links_close_the_panel_and_open_the_windows_page_and_open_x_navigates_inside_the_panel()
    {
        var r = Loaded();
        r.Click("open:ms-settings:network-wifi"); Assert.Equal(1, r.Ui.Hides); Assert.Equal(["ms-settings:network-wifi"], r.Ui.Uris);
        r.Click("open:ms-settings:privacy-location"); Assert.Equal("ms-settings:privacy-location", r.Ui.Uris[^1]);
        r.Click("open:wifi"); r.Click("open:bluetooth"); r.Click("open:sound"); r.Click("open:battery"); r.Click("open:display"); r.Click("back");
        Assert.Equal([PanelKind.Wifi, PanelKind.Bluetooth, PanelKind.Sound, PanelKind.Battery, PanelKind.Display, PanelKind.ControlCenter], r.Ui.Navigations);
        r.Click("algo-desconocido"); r.Click("net:Fantasma"); Assert.DoesNotContain(r.Wifi.Calls, c => c.StartsWith("Connect") || c == "Disconnect");   // lo desconocido no hace nada
    }

    [Fact]
    public void Mute_shows_busy_runs_off_thread_then_rereads_the_real_state()
    {
        var r = Loaded(); r.Audio.Calls.Clear();
        r.Click("mute"); Assert.True(r.Ui.Ctx.Busy.ContainsKey("mute")); Assert.DoesNotContain("SetMuted True", r.Audio.Calls);   // aún no corrió: está en segundo plano
        r.Pump(); Assert.Contains("SetMuted True", r.Audio.Calls); Assert.Empty(r.Ui.Ctx.Busy); Assert.Null(r.Ui.Ctx.Message); Assert.Contains("Read", r.Audio.Calls);
    }

    [Fact]
    public void A_failed_operation_clears_busy_and_shows_the_reason()
    {
        var r = Loaded(); r.Audio.Next = ServiceResult.Failed("Windows rechazó el cambio");
        r.Click("mute"); r.Pump();
        Assert.Empty(r.Ui.Ctx.Busy); Assert.Equal("Windows rechazó el cambio", r.Ui.Ctx.Message);
        r.Click("mute"); Assert.Null(r.Ui.Ctx.Message);   // un nuevo intento limpia el aviso anterior
    }

    [Fact]
    public void An_exception_inside_a_service_is_reported_as_the_failure_message_and_the_panel_keeps_working()
    {
        var r = Loaded(); r.Wifi.Throw = new InvalidOperationException("wlanapi cayó");
        r.Click("wifi-radio"); r.Pump(); Assert.Contains("wlanapi cayó", r.Ui.Ctx.Message); Assert.Empty(r.Ui.Ctx.Busy);
    }

    [Fact]
    public void Radios_toggle_the_opposite_of_what_windows_reports()
    {
        var r = Loaded(); r.Click("wifi-radio"); r.Click("bt-radio"); r.Pump();
        Assert.Contains("SetRadio False", r.Wifi.Calls); Assert.Contains("SetRadio False", r.Bt.Calls);   // ambas estaban encendidas
        r.Wifi.State = r.Wifi.State with { RadioOn = false }; r.Bt.State = r.Bt.State with { RadioOn = false }; r.Hub.RefreshWifi(false); r.Hub.RefreshBluetooth(true); r.Pump();
        r.Click("wifi-radio"); r.Click("bt-radio"); r.Pump(); Assert.Contains("SetRadio True", r.Wifi.Calls); Assert.Contains("SetRadio True", r.Bt.Calls);
    }

    [Fact]
    public void A_network_row_connects_to_a_saved_profile_disconnects_the_current_one_and_ignores_unknown_ssids()
    {
        var r = Loaded();
        r.Click("net:Vecino"); r.Pump(); Assert.Contains("Connect PerfilVecino/Vecino", r.Wifi.Calls);
        r.Click("net:Casa"); r.Pump(); Assert.Contains("Disconnect", r.Wifi.Calls);
        r.Click("net:Nueva"); r.Pump(); Assert.Contains("Connect /Nueva", r.Wifi.Calls);   // sin perfil: el adaptador responde «conéctala desde Configuración»; aquí solo se verifica que no inventa un perfil
    }

    [Fact]
    public void A_connection_that_cannot_be_made_shows_the_reason_and_rereads_the_networks()
    {
        var r = Loaded(); r.Wifi.Next = ServiceResult.Unavailable("Esta red no tiene un perfil guardado: conéctala desde Configuración");
        r.Wifi.Calls.Clear(); r.Click("net:Vecino"); Assert.Equal("Conectando…", r.Ui.Ctx.Busy["net:Vecino"]); r.Pump();
        Assert.Contains("perfil guardado", r.Ui.Ctx.Message); Assert.Contains("Read scan=True", r.Wifi.Calls);
    }

    [Fact]
    public void With_wifi_details_a_tap_unfolds_the_row_without_touching_the_network_and_the_action_lives_inside()
    {
        var r = Loaded(new BarConfig { WifiDetails = true });
        r.Wifi.Calls.Clear(); r.Click("net:Vecino"); r.Pump();
        Assert.Equal("Vecino", r.Ui.Ctx.ExpandedNet); Assert.DoesNotContain(r.Wifi.Calls, c => c.StartsWith("Connect") || c == "Disconnect");
        r.Click("net:Vecino"); Assert.Null(r.Ui.Ctx.ExpandedNet);                       // segundo toque la pliega
        r.Click("net:Casa"); r.Click("net:Vecino"); Assert.Equal("Vecino", r.Ui.Ctx.ExpandedNet);   // una sola desplegada a la vez
        r.Click("netact:Vecino"); r.Pump(); Assert.Contains("Connect PerfilVecino/Vecino", r.Wifi.Calls);
        r.Click("netact:Casa"); r.Pump(); Assert.Contains("Disconnect", r.Wifi.Calls);
    }

    [Fact]
    public void Switching_the_output_calls_the_service_and_when_the_api_is_not_compatible_says_why()
    {
        var r = Loaded(); r.Click("out:hp"); r.Pump(); Assert.Contains("SetDefaultOutput hp", r.Audio.Calls); Assert.Null(r.Ui.Ctx.Message);
        r.Audio.Next = ServiceResult.Unavailable("Este equipo no permite cambiar la salida desde WinMac"); r.Click("out:spk"); r.Pump(); Assert.Contains("no permite cambiar", r.Ui.Ctx.Message);
    }

    [Fact]
    public void Restore_output_only_acts_when_there_is_a_previous_device()
    {
        var r = Loaded(); r.Click("audio-restore"); r.Pump(); Assert.DoesNotContain(r.Audio.Calls, c => c.StartsWith("SetDefaultOutput"));
        r.Audio.PreviousDefaultId = "hp"; r.Click("audio-restore"); r.Pump(); Assert.Contains("SetDefaultOutput hp", r.Audio.Calls);
    }

    [Fact]
    public void Power_mode_and_theme_show_the_request_at_once_and_the_real_reading_reverts_them_if_windows_refused()
    {
        var r = Loaded();
        r.Click("power:Balanced"); Assert.Equal(PowerMode.Balanced, r.Hub.Snapshot.Power.Mode);   // se ve al instante
        r.Power.Next = ServiceResult.Denied("Windows no permite cambiar el modo de energía a este usuario"); r.Pump();
        Assert.Equal(PowerMode.BestPerformance, r.Hub.Snapshot.Power.Mode); Assert.Contains("no permite", r.Ui.Ctx.Message);   // la lectura real devolvió el valor verdadero
        r.Click("appearance"); Assert.False(r.Hub.Snapshot.Appearance.Dark); r.Appearance.Next = ServiceResult.Ok; r.Appearance.State = new AppearanceState(true, false); r.Pump();
        Assert.Contains("SetDark False", r.Appearance.Calls); Assert.False(r.Hub.Snapshot.Appearance.Dark);
    }

    [Fact]
    public void Power_and_theme_are_reread_with_the_fast_path_not_the_slow_one()
    {
        var r = Loaded(); r.Brightness.Calls.Clear();
        r.Click("power:Balanced"); r.Power.State = new PowerModeState(true, PowerMode.Balanced); r.Pump();
        Assert.Empty(r.Brightness.Calls);   // cambiar el modo de energía no vuelve a sondear el brillo (lento)
        Assert.Equal(PowerMode.Balanced, r.Hub.Snapshot.Power.Mode);
    }

    [Fact]
    public void Changing_the_display_mode_closes_the_panel_and_reports_a_refusal_in_a_dialog()
    {
        var r = Loaded(); r.Click("topology:Extend"); r.Pump(); Assert.Equal(1, r.Ui.Hides); Assert.Contains("SetTopology Extend", r.Display.Calls); Assert.Empty(r.Ui.Infos);
        r.Display.Next = ServiceResult.Unavailable("Este modo no está disponible con las pantallas conectadas"); r.Click("topology:Duplicate"); r.Pump();
        Assert.Contains(r.Ui.Infos, i => i.Title == "Pantallas" && i.Text!.Contains("no está disponible"));
        r.Click("topology:Inventado"); r.Pump(); Assert.Equal(2, r.Display.Calls.Count);   // un valor desconocido no llega a Windows
    }

    [Fact]
    public void Notifications_close_the_panel_and_open_the_native_center_reporting_failure_in_a_dialog()
    {
        var r = Loaded(); r.Click("notifications"); r.Pump(); Assert.Equal(1, r.Ui.Hides); Assert.Contains("OpenNotificationCenter", r.Session.Calls); Assert.Empty(r.Ui.Infos);
        r.Session.Next = ServiceResult.Failed("sin acceso"); r.Click("notifications"); r.Pump(); Assert.Contains(r.Ui.Infos, i => i.Title == "Notificaciones" && i.Text == "sin acceso");
    }

    [Fact]
    public void The_volume_slider_follows_the_pointer_applies_only_the_last_value_and_confirms_with_the_real_reading_on_release()
    {
        var r = Loaded(); r.Audio.Calls.Clear();
        r.Slide("volume", 55); r.Slide("volume", 60); r.Slide("volume", 63, commit: true);
        Assert.Equal(63, r.Hub.Snapshot.Audio.Volume);   // el deslizador ya está donde está el puntero
        r.Pump(); Assert.Equal(["SetVolume 63", "Read"], r.Audio.Calls);   // 55 y 60 se coalescieron y al soltar se relee
    }

    [Fact]
    public void A_volume_failure_shows_the_message_and_rereads_the_real_volume()
    {
        var r = Loaded(); r.Audio.Next = ServiceResult.Denied("sin permiso"); r.Slide("volume", 10); r.Pump();
        Assert.Equal("sin permiso", r.Ui.Ctx.Message); Assert.Equal(40, r.Hub.Snapshot.Audio.Volume);   // volvió al valor real
    }

    [Fact]
    public void The_brightness_slider_applies_to_every_controllable_monitor_and_stops_at_the_first_failure()
    {
        var r = Loaded(); r.Slide("brightness", 70, commit: true); r.Pump();
        Assert.Equal(["Set m1 70", "Set m2 70"], r.Brightness.Calls.Where(c => c.StartsWith("Set")));
        r.Brightness.Calls.Clear(); r.Brightness.Next = ServiceResult.Unavailable("DDC/CI apagado"); r.Slide("brightness", 20, commit: true); r.Pump();
        Assert.Equal(["Set m1 20"], r.Brightness.Calls.Where(c => c.StartsWith("Set")));   // no insiste con el segundo
        Assert.Contains("DDC/CI", r.Ui.Ctx.Message);
    }

    [Fact]
    public void The_brightness_slider_does_nothing_without_controllable_monitors()
    {
        var r = new Rig(); r.Brightness.State = BrightnessState.None("sin DDC/CI"); r.LoadAll(); r.Brightness.Calls.Clear();
        r.Slide("brightness", 50, commit: true); r.Pump(); Assert.DoesNotContain(r.Brightness.Calls, c => c.StartsWith("Set"));
    }

    [Fact]
    public void The_calendar_navigates_months_and_selects_a_day_without_touching_the_system()
    {
        var r = Loaded(); int y = r.Ui.Ctx.ViewYear, m = r.Ui.Ctx.ViewMonth;
        r.Click("next"); Assert.Equal(m == 12 ? 1 : m + 1, r.Ui.Ctx.ViewMonth); r.Click("prev"); r.Click("prev"); Assert.Equal(m == 1 ? 12 : m - 1, r.Ui.Ctx.ViewMonth);
        r.Click("day:2026-11-15"); Assert.Equal(new DateOnly(2026, 11, 15), r.Ui.Ctx.SelectedDay); r.Click("day:no-es-fecha"); Assert.Equal(new DateOnly(2026, 11, 15), r.Ui.Ctx.SelectedDay);
        _ = y; Assert.Empty(r.Session.Calls);
    }
}
