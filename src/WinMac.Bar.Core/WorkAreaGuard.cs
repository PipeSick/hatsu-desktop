namespace WinMac.Bar;

/// <summary>
/// WM-003: comprueba que la franja que la barra pidió con SHAppBarMessage está REALMENTE fuera del área de trabajo del monitor. Al primer arranque (inicio de
/// sesión, Explorer aún cargando, barra de tareas recién configurada) ABM_NEW/ABM_SETPOS pueden fallar o no aplicarse, y la barra tapaba los menús de las
/// apps maximizadas. No se toca ninguna configuración de Windows: solo se vuelve a pedir la reserva, con espera creciente, hasta que Windows la confirma.
/// </summary>
public sealed class WorkAreaGuard
{
    private int _failures; private double _nextCheck;

    /// <summary>Segundos de espera tras <paramref name="failures"/> comprobaciones fallidas seguidas: 2, 4, 8… hasta 30 (se sigue intentando siempre).</summary>
    public static double Backoff(int failures) => Math.Min(30, 2 * Math.Pow(2, Math.Max(0, failures - 1)));

    /// <summary>¿El área de trabajo del monitor empieza en o por debajo del borde inferior de la barra? (con la barra arriba).</summary>
    public static bool Reserved(int workTop, int workBottom, int barTop, int barBottom) => workTop >= barBottom || workBottom <= barTop;

    public int Failures => _failures;

    /// <summary>Se acaba de pedir la reserva: Windows tarda un momento en recolocar el área de trabajo; no se mide antes de 2 s.</summary>
    public void Placed(double now) => _nextCheck = Math.Max(_nextCheck, now + 2);

    /// <summary>
    /// Llamado periódicamente con el reloj (segundos). Devuelve <c>true</c> cuando toca volver a registrar la reserva. <paramref name="reserved"/> es la lectura actual.
    /// </summary>
    public bool Due(double now, bool reserved)
    {
        if (reserved) { _failures = 0; _nextCheck = now + 5; return false; }
        if (now < _nextCheck) return false;
        _failures++; _nextCheck = now + Backoff(_failures);
        return true;
    }
}
