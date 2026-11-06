using WinMac.Bar.Vpn;

namespace WinMac.Bar;

public static partial class PanelBuilder
{
    public const string VpnTogglePrefix = "vpn:toggle:", VpnOpenSettings = "vpn:settings", VpnRefresh = "vpn:refresh";

    public static PanelModel VpnPanel(PanelContext c)
    {
        var v = c.Vpn ?? new VpnView(VpnState.None("Leyendo…"));
        var w = new List<Widget> { new(WidgetKind.Header, "vpn-title", "VPN") };
        if (!v.State.Available)
            w.Add(new Widget(WidgetKind.Note, "vpn-na", v.State.Reason ?? "No se pudo leer las conexiones VPN."));
        else if (v.State.Profiles.Count == 0)
        {
            w.Add(new Widget(WidgetKind.Note, "vpn-empty", "No hay conexiones VPN de Windows. Agrega una en Configuración › Red › VPN y aparecerá aquí."));
        }
        else
        {
            foreach (var p in v.State.Profiles)
            {
                bool busy = v.BusyName == p.Name;
                string sub = busy ? v.BusyText ?? "Trabajando…" : p.Connected ? "Conectada · toca para desconectar" : "Desconectada · toca para conectar";
                w.Add(new Widget(WidgetKind.Row, VpnTogglePrefix + p.Name, p.Name, sub, On: p.Connected, Enabled: v.BusyName is null, Glyph: p.Connected ? "check" : null));
            }
        }

        if (v.Message is { Length: > 0 } m) w.Add(new Widget(WidgetKind.Note, "vpn-msg", m));
        w.Add(new Widget(WidgetKind.Button, VpnOpenSettings, "Abrir Configuración de VPN"));
        return new PanelModel(PanelKind.Vpn, "VPN", w);
    }
}
