using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Aviso de cambio de brillo, solo donde Windows lo da: el panel interno de un portátil emite el evento WMI <c>WmiMonitorBrightnessEvent</c> (root\wmi) al cambiar el brillo
/// (teclas Fn, Configuración, el Centro de actividades, otras apps). Los monitores EXTERNOS por DDC/CI no avisan de nada: su brillo solo se conoce leyéndolo (lento,
/// cientos de ms por monitor), así que NO se vigilan; el cambio hecho desde la propia barra de WinMac sí se conoce y lo anuncia quien lo hace.
/// La espera es bloqueante en un hilo propio (<c>NextEvent</c> con 1 s de tope para poder salir), sin sondeo de valores.
/// </summary>
public sealed partial class BrightnessService
{
    private const int WbemTimedOut = unchecked((int)0x80043001);

    /// <summary>Empieza a escuchar. <paramref name="changed"/> recibe (porcentaje, nombre de la pantalla). null con el motivo si no hay panel interno con brillo.</summary>
    public static IDisposable? WatchInternal(Action<int, string> changed, Action<string> log, out string? why)
    {
        try
        {
            if (!ReadInternal().Any()) { why = "Sin panel interno con brillo (los monitores externos por DDC/CI no avisan de los cambios)"; return null; }
        }
        catch (Exception e) { why = "WMI: " + e.GetType().Name; return null; }

        var w = new InternalWatcher(changed, log); w.Start(); why = null; return w;
    }

    private sealed class InternalWatcher(Action<int, string> changed, Action<string> log) : IDisposable
    {
        private volatile bool _stop; private Thread? _thread;

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "WinMac brillo (WMI)" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        private void Run()
        {
            try
            {
                dynamic? svc = WmiService(); if (svc is null) { log("[brillo] WMI no disponible"); return; }
                dynamic events = svc.ExecNotificationQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                int last = -1;
                while (!_stop)
                {
                    dynamic ev;
                    try { ev = events.NextEvent(1000); }
                    catch (COMException e) when (e.HResult == WbemTimedOut) { continue; }
                    int pct = Math.Clamp((int)ev.Brightness, 0, 100);
                    if (pct == last) continue; last = pct;
                    try { changed(pct, "Pantalla integrada"); } catch (Exception e) { log("[brillo] aviso: " + e.GetType().Name + ": " + e.Message); }
                }
            }
            catch (Exception e) { log("[brillo] se deja de escuchar el panel interno: " + e.GetType().Name + $" 0x{e.HResult:X8}"); }
        }

        public void Dispose() => _stop = true;   // el hilo sale en ≤ 1 s (es de fondo: no retiene el cierre del proceso)
    }
}
