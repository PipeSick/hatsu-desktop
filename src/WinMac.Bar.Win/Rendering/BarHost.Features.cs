using WinMac.Bar.Features;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Features;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Módulos de productividad (Mission Control, Snap, Live Text, Spotlight Actions, Gestures): se encienden y apagan en caliente con el <see cref="FeatureManager"/>. Viven en el proceso de la Barra
/// (que ya tiene las superposiciones, los atajos y los ganchos); apagado un módulo, todo lo suyo se libera (atajos, ganchos, ventanas, hilos). Los servicios compartidos (catálogo de ventanas,
/// registro de atajos) se crean la primera vez que un módulo los necesita y se liberan cuando ya no los usa nadie.
/// </summary>
public sealed partial class BarHost
{
    private FeatureManager? _features; private HotkeyRegistry? _hotkeys; private HotkeyBinder? _binder;
    private int _retrySeen;

    internal HotkeyBinder Binder => _binder ??= new HotkeyBinder(_hotkeys ??= new HotkeyRegistry(), Post, _log) { Changed = () => Post(ReportHotkeys) };
    internal FeatureManager Features => _features!;

    private void StartFeatures()
    {
        _hotkeys ??= new HotkeyRegistry(); _binder ??= new HotkeyBinder(_hotkeys, Post, _log) { Changed = () => Post(ReportHotkeys) };
        _features = new FeatureManager(_log);
        RegisterFeatures(_features);
        _features.StatusChanged += () => Post(ReportFeatures);
        _retrySeen = _cfg.FeatureRetryToken;
        SyncLegacyHotkeys(null);
        _features.Apply(_cfg);
    }

    private void ApplyFeatures(BarConfig old, BarConfig c)
    {
        if (_features is null) return;
        SyncLegacyHotkeys(null);
        if (c.FeatureRetryToken != _retrySeen) { _retrySeen = c.FeatureRetryToken; _features.RetryAllFailed(); }
        _features.Apply(c);
    }

    private void StopFeatures() { try { _features?.Dispose(); } catch (Exception e) { _log("[módulos] error al apagar: " + e.Message); } _features = null; }

    /// <summary>Anota en el registro los atajos que la Barra ya tenía (Spotlight, capturas) para que los módulos vean los conflictos.</summary>
    private void SyncLegacyHotkeys(IReadOnlyList<HotkeyInfo>? list)
    {
        var reg = _hotkeys; if (reg is null) return;
        reg.ReleaseOwner("legacy");
        var src = list ?? _captureKeyInfo.Select(k => k.Info).Concat(SpotlightHotkeyInfo() is { } s ? [s] : []).ToList();
        int i = 0; foreach (var k in src) reg.Note("legacy", "k" + i++, k.Purpose, k.Keys);
    }

    private void ReportFeatures()
    {
        if (_features is null) return;
        ModuleEvent?.Invoke("bar.features", new { items = _features.Snapshot().Select(s => new { id = s.Id, state = s.State.ToString(), detail = s.Detail, faults = s.Faults }).ToArray() });
    }

    /// <summary>Alta de los módulos conforme se implementan (cada uno aporta su interruptor y su <see cref="IHatsuFeature"/>).</summary>
    partial void RegisterFeaturesCore(FeatureManager m);
    private void RegisterFeatures(FeatureManager m) => RegisterFeaturesCore(m);
}
