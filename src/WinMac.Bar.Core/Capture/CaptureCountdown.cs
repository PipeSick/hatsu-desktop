namespace WinMac.Bar.Capture;

/// <summary>
/// WM-002: cuenta atrás antes de congelar la pantalla, para capturar menús abiertos. El usuario lanza la captura, abre el menú y, al llegar a cero, la pantalla
/// se copia ANTES de mostrar ninguna interfaz de WinMac y sin cambiar el foco (un cambio de foco cerraría el menú). No promete capturar cualquier menú:
/// los que se cierran solos por tiempo o por perder el ratón pueden cerrarse antes de que acabe.
/// </summary>
public sealed class CaptureCountdown
{
    public static readonly int[] Allowed = [3, 5, 10];

    public CaptureCountdown(CaptureMode mode, int seconds, double now)
    {
        Mode = mode; Seconds = Allowed.Contains(seconds) ? seconds : Allowed.OrderBy(a => Math.Abs(a - seconds)).First(); _end = now + Seconds;
    }

    private readonly double _end;
    public CaptureMode Mode { get; }
    public int Seconds { get; }
    public bool Cancelled { get; private set; }

    /// <summary>Segundos enteros que se muestran (5, 4, 3, 2, 1); 0 cuando toca capturar.</summary>
    public int Remaining(double now) => Cancelled ? 0 : (int)Math.Max(0, Math.Ceiling(_end - now - 1e-9));
    public bool Due(double now) => !Cancelled && now >= _end;
    public void Cancel() => Cancelled = true;
    public string Text(double now) => $"Captura en {Remaining(now)} s";
}
