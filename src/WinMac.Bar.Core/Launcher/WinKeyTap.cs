namespace WinMac.Bar.Launcher;

/// <summary>Qué hacer con una tecla vista por el gancho de teclado.</summary>
public enum WinKeyAction
{
    /// <summary>Dejarla pasar sin más (siempre: el gancho NUNCA bloquea una tecla).</summary>
    Pass,
    /// <summary>Es el «soltar» de una pulsación SOLA de Windows: antes de dejarla pasar, inyectar una tecla neutra para que Windows no abra el menú Inicio, y abrir Launchpad.</summary>
    MaskAndOpen,
}

/// <summary>
/// Reconoce la pulsación aislada de la tecla Windows (pulsar y soltar sin otra tecla en medio). Es la técnica de la «tecla máscara»: el gancho deja pasar TODAS las teclas,
/// también Windows, así que Win+E, Win+R, Win+L, Win+D, Win+Tab y cualquier otro atajo funcionan igual que siempre; solo cuando se suelta Windows sin haber pulsado nada más
/// se envía antes una tecla sin asignar (VK 0xE8) para que Windows lo vea como una combinación y no abra el menú Inicio.
/// Las teclas inyectadas por otros programas (o por nosotros) no cuentan. Una pulsación larga (más de <see cref="MaxTap"/>) no abre nada y deja el comportamiento de Windows.
/// </summary>
public sealed class WinKeyTap
{
    public const int VkLWin = 0x5B, VkRWin = 0x5C, VkMask = 0xE8;
    public static readonly TimeSpan MaxTap = TimeSpan.FromMilliseconds(700);

    private bool _down, _combo; private long _downAt;

    /// <summary>Activado en los ajustes. Desactivado, todo pasa sin cambios.</summary>
    public bool Enabled { get; set; } = true;

    /// <param name="vk">Código virtual.</param><param name="keyDown">true = pulsada, false = soltada.</param>
    /// <param name="injected">La generó un programa (bandera LLKHF_INJECTED).</param><param name="ms">Marca de tiempo del evento (ms).</param>
    public WinKeyAction OnKey(int vk, bool keyDown, bool injected, long ms)
    {
        bool isWin = vk is VkLWin or VkRWin;
        if (injected) return WinKeyAction.Pass;
        if (isWin)
        {
            if (keyDown)
            {
                if (!_down) { _down = true; _combo = false; _downAt = ms; }   // la repetición automática no reinicia nada
                return WinKeyAction.Pass;
            }

            bool tap = _down && !_combo && ms - _downAt <= MaxTap.TotalMilliseconds;
            _down = false; _combo = false;
            return tap && Enabled ? WinKeyAction.MaskAndOpen : WinKeyAction.Pass;
        }

        if (_down && keyDown) _combo = true;   // Win + cualquier otra tecla: es un atajo, no se toca
        return WinKeyAction.Pass;
    }

    /// <summary>Un clic del ratón con Windows pulsada también es una combinación (p. ej. Win+clic en algunas utilidades).</summary>
    public void OnMouse() { if (_down) _combo = true; }

    /// <summary>El gancho se reinstaló o se perdió el estado (bloqueo de sesión, UAC): se empieza de cero.</summary>
    public void Reset() { _down = false; _combo = false; }
}
