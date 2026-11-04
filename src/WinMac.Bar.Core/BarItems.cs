using WinMac.Connect;

namespace WinMac.Bar;

/// <summary>
/// It7: el indicador del iPhone en la barra. Solo datos reales: un porcentaje viejo NUNCA se muestra como actual
/// (conectado sin lectura válida → «—», estado desconocido), y sin conexión el icono va tachado.
/// </summary>
public static class PhoneBar
{
    public static BarItem? Evaluate(PhoneState? ph, DateTime nowUtc, bool showPercent)
    {
        if (ph is not { DeviceName: { } name }) return null;
        if (ph.Status is PhoneLinkStatus.NoDevice or PhoneLinkStatus.NoBluetooth) return null;
        if (ph.BatteryStatus == PhoneFeatureStatus.Off) return null;
        const double W = BarModel.IconW;
        switch (ph.Status)
        {
            case PhoneLinkStatus.Connected:
                if (PhoneStatusText.CurrentBattery(ph, nowUtc) is { } pct)
                    return new BarItem(BarItemKind.Phone, showPercent ? $"{pct}%" : "", W, Tip: $"{name} — batería {pct} %");
                string last = ph.BatteryAtUtc is { } at ? $" (última lectura {PhoneStatusText.Ago(at, nowUtc)})" : "";
                return new BarItem(BarItemKind.Phone, showPercent ? "—" : "", W, Tip: $"{name} conectado — batería desconocida{last}");
            case PhoneLinkStatus.Connecting:
                return new BarItem(BarItemKind.Phone, "", W, Look: BarItemLook.Dim, Tip: $"{name} — conectando…");
            default:   // Disconnected, Error
                return new BarItem(BarItemKind.Phone, "", W, Look: BarItemLook.Slashed, Tip: $"{name} — desconectado");
        }
    }
}

/// <summary>
/// It7: el módulo de energía del PC. Con batería real → batería + %; sin batería (sobremesa, o solo un SAI) → oculto,
/// salvo que el usuario pida el enchufe (sin %). Nunca se inventa un 100 %.
/// </summary>
public static class PcPowerBar
{
    public static BarItem? Evaluate(BatteryInfo b, BarConfig c)
    {
        if (b.Present)
            return new BarItem(BarItemKind.Battery, c.ShowBatteryPercent ? $"{b.Percent}%" : "", BarModel.BatteryW, Tip: Tip(b));
        if (c.ShowPowerWithoutBattery && b.Plugged)
            return new BarItem(BarItemKind.PowerPlug, "", BarModel.IconW, Key: "Battery", Tip: "Conectado a la corriente (sin batería)");
        return null;
    }

    public static string Tip(BatteryInfo b) =>
        $"Batería {b.Percent} % — {BatteryText.State(b)}" + (BatteryText.TimeLeft(b.SecondsLeft) is { } t && !b.Plugged ? $" · {t}" : "");
}

/// <summary>Clasifica un dispositivo de la clase Batería por la ruta de su interfaz: la batería del equipo (ACPI) frente a un SAI/UPS (HID o USB), que no es la batería del PC.</summary>
public enum BatteryDeviceKind { Internal, Ups }

public static class BatteryDeviceClass
{
    public static BatteryDeviceKind Classify(string? interfacePath)
    {
        string p = (interfacePath ?? "").Replace('/', '\\').ToLowerInvariant();
        string rest = p.StartsWith(@"\\?\") ? p[4..] : p;
        return rest.StartsWith("hid#") || rest.StartsWith("usb#") ? BatteryDeviceKind.Ups : BatteryDeviceKind.Internal;
    }

    /// <summary>¿Hay una batería del equipo entre estas rutas? Solo SAI (o ninguna) → false: la «batería» que informa Windows es un SAI y no se muestra como batería del PC.</summary>
    public static bool HasInternal(IEnumerable<string> paths) => paths.Any(p => Classify(p) == BatteryDeviceKind.Internal);
}

/// <summary>
/// It7: orden de los módulos propios de la barra derecha. El reloj, el Centro de control y la fila de la izquierda no se mueven.
/// El orden se guarda como lista de identificadores («Network,Volume,Battery»); lo desconocido se ignora y un módulo nuevo entra en su sitio predeterminado.
/// </summary>
public static class BarOrdering
{
    public static bool IsMovable(BarItem i) => i.Kind is not (BarItemKind.Clock or BarItemKind.ControlCenter or BarItemKind.WinMacMenu or BarItemKind.AppName or BarItemKind.AppMenu);

    public static IReadOnlyList<string> Parse(string? s) =>
        (s ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length is > 0 and <= 40).Distinct(StringComparer.Ordinal).Take(40).ToList();

    public static string Serialize(IEnumerable<string> ids) => string.Join(',', ids);

    /// <summary>Los módulos movibles según el orden guardado, seguidos de los fijos en su orden original.</summary>
    public static List<BarItem> Apply(IReadOnlyList<BarItem> items, string? saved)
    {
        var movable = items.Where(IsMovable).ToList(); var fixedTail = items.Where(i => !IsMovable(i)).ToList();
        var byId = movable.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        var order = Parse(saved).Where(byId.ContainsKey).ToList();
        for (int k = 0; k < movable.Count; k++)
        {   // un módulo sin posición guardada va justo detrás de su predecesor predeterminado
            string id = movable[k].Id; if (order.Contains(id)) continue;
            int at = 0; for (int j = k - 1; j >= 0; j--) { int p = order.IndexOf(movable[j].Id); if (p >= 0) { at = p + 1; break; } }
            order.Insert(at, id);
        }
        var res = order.Select(id => byId[id]).ToList(); res.AddRange(fixedTail); return res;
    }

    /// <summary>Mueve <paramref name="id"/> al hueco <paramref name="gap"/> (0..n, contado sobre la lista ANTES de quitarlo).</summary>
    public static List<string> Move(IReadOnlyList<string> visible, string id, int gap)
    {
        var l = visible.ToList(); int from = l.IndexOf(id); if (from < 0) return l;
        gap = Math.Clamp(gap, 0, l.Count); l.RemoveAt(from); if (gap > from) gap--; l.Insert(Math.Clamp(gap, 0, l.Count), id); return l;
    }

    /// <summary>Orden a guardar: las posiciones de los módulos hoy ocultos (p. ej. el iPhone desconectado) se conservan.</summary>
    public static string Merge(string? saved, IReadOnlyList<string> visibleAfter)
    {
        var vis = visibleAfter.ToHashSet(); var res = new List<string>(); int vi = 0;
        foreach (var s in Parse(saved)) res.Add(vis.Contains(s) && vi < visibleAfter.Count ? visibleAfter[vi++] : s);
        while (vi < visibleAfter.Count) res.Add(visibleAfter[vi++]);
        return Serialize(res.Distinct());
    }

    /// <summary>Hueco de inserción (0..n) para un puntero en X entre los módulos movibles (por el centro de cada uno).</summary>
    public static int GapAt(IReadOnlyList<BarItemFrame> movableFrames, double x)
    { int g = 0; foreach (var f in movableFrames) if (x > f.X + f.W / 2) g++; return g; }
}
