using WinMac.Bar.Features;
using WinMac.Bar.Win.Rendering;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Spotlight Actions: Spotlight busca y ejecuta a través del registro central de acciones (<see cref="ActionRegistry"/>), donde están las de siempre (capturas, grabar, Launchpad, Ajustes, espacios de trabajo), las del
/// sistema (bloquear, suspender, cerrar sesión, reiniciar, apagar: las que cierran cosas piden un segundo Intro) y las de los módulos ACTIVOS. No instala ganchos, atajos ni tareas. Apagado, Spotlight vuelve a su lista clásica.
/// </summary>
internal sealed class SpotlightActionsFeature : IHatsuFeature
{
    private readonly BarHost _host; public SpotlightActionsFeature(BarHost host) { _host = host; }
    public string Id => FeatureConfig.SpotActions;
    public string Title => "Spotlight Actions";
    public string Description => "Spotlight como centro de comandos: acciones de Hatsu, del sistema y de los módulos activos.";
    public string? Info() => $"{_host.Actions.All.Count} acciones registradas";

    public void Start(FeatureScope scope)
    {
        var ids = _host.RegisterCoreActions();
        scope.Add(() => _host.UnregisterActions(ids));
        _host.UseActionRegistry = true; scope.Add(() => _host.UseActionRegistry = false);
    }
}
