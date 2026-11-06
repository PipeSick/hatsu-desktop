using WinMac.Bar.Win.Interop;
using WinMac.Bar.Vpn;
using WinMac.Bar.Win.Shell;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// VPN de Windows en la barra (apagado por defecto). Sin servicios propios: con el icono encendido se lee el estado cada pocos segundos en un hilo de trabajo; conectar y desconectar
/// lo lanza solo el usuario desde el panel, con <c>rasdial.exe</c> (credenciales guardadas en el perfil de Windows).
/// </summary>
public sealed partial class BarHost
{
    private readonly IVpnService _vpnSvc = new RasVpnService();
    private VpnState _vpn = VpnState.None("Leyendo…");
    private string? _vpnBusyName, _vpnBusyText, _vpnMsg;
    private DateTime _lastVpn = DateTime.MinValue; private int _vpnReading;

    private bool VpnPanelOpen => _pkind == PanelKind.Vpn && _popover is { IsOpen: true };

    private VpnView VpnViewNow() => new(_vpn, _vpnBusyName, _vpnBusyText, _vpnMsg);

    private void VpnTick()
    {
        if (!_cfg.ShowVpn) { if (_vpn.Available) _vpn = VpnState.None("Leyendo…"); return; }
        double every = VpnPanelOpen ? 2 : 6;
        if ((DateTime.UtcNow - _lastVpn).TotalSeconds < every || _vpnBusyName is not null) return;
        RefreshVpn();
    }

    private void RefreshVpn()
    {
        _lastVpn = DateTime.UtcNow;
        if (Interlocked.Exchange(ref _vpnReading, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try { var s = _vpnSvc.Read(); Post(() => ApplyVpn(s)); }
            catch (Exception e) { _log("[vpn] lectura: " + e.GetType().Name); }
            finally { Volatile.Write(ref _vpnReading, 0); }
        });
    }

    private void ApplyVpn(VpnState s)
    {
        bool same = s.Available == _vpn.Available && s.Profiles.Count == _vpn.Profiles.Count && s.Profiles.Zip(_vpn.Profiles).All(p => p.First.Name == p.Second.Name && p.First.Connected == p.Second.Connected);
        _vpn = s;
        if (same) return;
        UpdateState(force: false);
        if (VpnPanelOpen) RebuildVpnPanel();
    }

    private void RebuildVpnPanel() { _pctx = WithModules(_pctx with { Now = DateTime.Now }); _popover?.SetModel(PanelBuilder.Build(PanelKind.Vpn, _pctx)); }

    private void VpnAction(PanelEvent e)
    {
        if (e.Kind != PanelEventKind.Click) return;
        if (e.Id == PanelBuilder.VpnOpenSettings) { BarNative.ShellExecuteW(IntPtr.Zero, "open", VpnText.SettingsUri, null, null, 1); return; }
        if (!e.Id.StartsWith(PanelBuilder.VpnTogglePrefix, StringComparison.Ordinal) || _vpnBusyName is not null) return;
        var p = _vpn.Profiles.FirstOrDefault(x => x.Name == e.Id[PanelBuilder.VpnTogglePrefix.Length..]); if (p is null) return;
        bool disconnect = p.Connected;
        _vpnBusyName = p.Name; _vpnBusyText = disconnect ? "Desconectando…" : "Conectando…"; _vpnMsg = null; RebuildVpnPanel();
        _ = Task.Run(() =>
        {
            string? err; VpnState s;
            try { err = disconnect ? _vpnSvc.Disconnect(p) : _vpnSvc.Connect(p); s = _vpnSvc.Read(); }
            catch (Exception ex) { err = "No se pudo cambiar la VPN (" + ex.GetType().Name + ")."; s = _vpn; }
            Post(() =>
            {
                _vpnBusyName = null; _vpnBusyText = null; _vpnMsg = err; _lastVpn = DateTime.UtcNow;
                if (err is not null) _log($"[vpn] {(disconnect ? "desconectar" : "conectar")} «{p.Name}»: {err}");
                ApplyVpn(s); if (VpnPanelOpen) RebuildVpnPanel();
            });
        });
    }
}
