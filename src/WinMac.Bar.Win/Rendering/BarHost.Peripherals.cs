using WinMac.Bar.Notch;
using WinMac.Bar.Notifications;
using WinMac.Bar.Peripherals;
using WinMac.Bar.Win.Shell.Peripherals;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// It8: módulo Periféricos dentro de la barra. DESACTIVADO por defecto y sin procesos propios: con el módulo apagado no existen proveedores, temporizadores ni datos en memoria.
/// Encendido, solo lee (Bluetooth estándar y HID) cuando se abre el panel, al pulsar «Actualizar» y —solo si hay alertas activadas— cada 10 minutos. Cada proveedor corre con plazo en
/// un hilo aparte: si no responde, la barra sigue y la tarjeta lo dice. Las alertas salen por el sistema de notificaciones de la Isla, sin otro canal.
/// </summary>
public sealed partial class BarHost
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(8), AlertPoll = TimeSpan.FromMinutes(10), MinManualGap = TimeSpan.FromSeconds(3);
    private PeripheralStore? _periph; private IPeripheralProvider[]? _periphProviders; private PeripheralAlertEngine? _periphAlerts; private CancellationTokenSource? _periphCts;
    private bool _periphBusy; private DateTime _nextPeriphPoll = DateTime.MaxValue, _lastPeriphRead = DateTime.MinValue; private uint _periphAlertId = 0x70000000;

    private void PeripheralsTick()
    {
        if (!_cfg.ShowPeripherals) { if (_periph is not null) StopPeripherals(); return; }
        if (_cfg.PeripheralAlerts && !_periphBusy && DateTime.UtcNow >= _nextPeriphPoll) RefreshPeripherals(force: false);
    }

    private void StopPeripherals()
    {
        try { _periphCts?.Cancel(); } catch (Exception) { }
        _periphCts = null; _periph?.Clear(); _periph = null; _periphProviders = null; _periphAlerts?.Reset(); _periphAlerts = null; _periphBusy = false; _nextPeriphPoll = DateTime.MaxValue;
    }

    private void RefreshPeripherals(bool force)
    {
        if (!_cfg.ShowPeripherals || _periphBusy) return;
        if (!force && DateTime.UtcNow - _lastPeriphRead < MinManualGap) return;
        _periph ??= new PeripheralStore(); _periphAlerts ??= new PeripheralAlertEngine();
        _periphProviders = _cfg.PeripheralAsusRog ? [new BluetoothPeripheralProvider(), new HidBatteryProvider(), new AsusRogProvider(_cfg.PeripheralAsusKeyboardName, _cfg.PeripheralAsusMouseName)] : [new BluetoothPeripheralProvider(), new HidBatteryProvider()];   // se rearma en cada lectura: activar o apagar el lector ASUS se nota al instante
        var cts = _periphCts = new CancellationTokenSource(); var store = _periph; var providers = _periphProviders;
        _periphBusy = true; _lastPeriphRead = DateTime.UtcNow; RebuildPeripheralsPanel();
        Task.Run(async () =>
        {
            var results = new List<ProviderResult>();
            foreach (var p in providers)
            {
                try { results.Add(await ProviderRunner.RunAsync(p, ProviderTimeout, cts.Token)); } catch (Exception) { results.Add(ProviderResult.Fail(p.Id, ProviderStatus.Failed)); }
            }

            Post(() =>
            {
                if (!ReferenceEquals(store, _periph)) return;   // se apagó el módulo mientras tanto
                var now = DateTime.UtcNow; foreach (var r in results) store.Apply(r, now);
                foreach (var r in results.Where(r => r.Status != ProviderStatus.Ok)) _log($"[periféricos] proveedor {r.ProviderId}: {r.Status} {r.Error}");
                _periphBusy = false; _nextPeriphPoll = DateTime.UtcNow + AlertPoll;
                EvaluatePeripheralAlerts(store, now);
                RebuildPeripheralsPanel();
            });
        });
    }

    private void EvaluatePeripheralAlerts(PeripheralStore store, DateTime now)
    {
        if (_periphAlerts is null) return;
        var hidden = PeripheralSettings.ParseHidden(_cfg.PeripheralHidden);
        foreach (var a in _periphAlerts.Evaluate(store.View(now, hidden), _cfg.PeripheralAlerts, _cfg.PeripheralAlertThreshold, now))
        {
            _log($"[periféricos] batería baja ({a.Percent} %)");   // sin el nombre del dispositivo en el registro
            if (NotchEvent is null || !_notchOpts.Notifications) continue;
            Send(NotchFeed.Notification(new AppNotification(++_periphAlertId, "winmac.peripherals", "Periféricos", "Batería baja: " + a.Name, $"Queda un {a.Percent} %. Conéctalo a la carga.", DateTimeOffset.Now), null));
        }
    }

    private PeripheralsView PeripheralsViewNow()
    {
        var hidden = PeripheralSettings.ParseHidden(_cfg.PeripheralHidden); var all = _periph?.View(DateTime.UtcNow, hidden) ?? [];
        return new PeripheralsView(all.Where(d => !d.Hidden).ToList(), all.Count(d => d.Hidden), _periphBusy, _cfg.PeripheralAlerts, _cfg.PeripheralAlertThreshold);
    }

    private void RebuildPeripheralsPanel() { if (_pkind == PanelKind.Peripherals && _popover is { IsOpen: true }) { _pctx = WithModules(_pctx with { Now = DateTime.Now }); _popover.SetModel(PanelBuilder.Build(PanelKind.Peripherals, _pctx)); } }

    private void PeripheralsAction(PanelEvent e)
    {
        if (e.Kind != PanelEventKind.Click) return;
        if (e.Id == PanelBuilder.PeriphRefresh) RefreshPeripherals(force: true);
        else if (e.Id == PanelBuilder.PeriphUnhide) { ((IPanelUi)this).PatchConfig(new { peripheralHidden = "" }); RebuildPeripheralsPanel(); }
        else if (e.Id == PanelBuilder.PeriphSettings) ((IPanelUi)this).OpenSettings("bar");
        else if (e.Id.StartsWith(PanelBuilder.PeriphHidePrefix, StringComparison.Ordinal))
        {
            ((IPanelUi)this).PatchConfig(new { peripheralHidden = PeripheralSettings.Hide(_cfg.PeripheralHidden, e.Id[PanelBuilder.PeriphHidePrefix.Length..]) }); RebuildPeripheralsPanel();
        }
    }
}
