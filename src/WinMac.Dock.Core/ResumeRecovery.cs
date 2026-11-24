namespace WinMac.Dock;

/// <summary>
/// Al volver de la suspensión Windows tarda varios segundos en reordenar pantallas, Explorer y áreas de trabajo, y durante ese rato las lecturas son transitorias
/// (el monitor puede informar otra resolución, el área de trabajo puede volver a ocupar toda la pantalla). Un solo ajuste inmediato queda desfasado: por eso, tras
/// reanudar, se reafirma la posición y la reserva de espacio en varios momentos (<see cref="Steps"/>). Puro: el reloj lo pasa quien llama.
/// </summary>
public sealed class ResumeRecovery
{
    /// <summary>Segundos tras reanudar en los que se vuelve a reafirmar (el primero espera a que Windows empiece a recolocar; el último cubre pantallas lentas).</summary>
    public static readonly double[] Steps = [1.5, 5, 12, 30];

    private double? _resumedAt; private int _next;

    /// <summary>Windows avisó de que el equipo reanudó (también llega si ya había una recuperación en curso: se reinicia).</summary>
    public void Resumed(double now) { _resumedAt = now; _next = 0; }

    public bool Active => _resumedAt is not null;

    /// <summary>true UNA vez por cada paso de <see cref="Steps"/> cuando llega su momento; false si no hay recuperación en curso o aún no toca.</summary>
    public bool Due(double now)
    {
        if (_resumedAt is not { } t0) return false;
        if (_next >= Steps.Length) { _resumedAt = null; return false; }
        if (now - t0 < Steps[_next]) return false;
        _next++; return true;
    }
}
