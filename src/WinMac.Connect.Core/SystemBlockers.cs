namespace WinMac.Connect;

/// <summary>
/// Cosas del sistema que programas de «optimización» o directivas suelen cambiar y que pueden impedir la telefonía Bluetooth de Windows (docs/62). Puro: la lectura del registro está en Connect.App.
/// Solo se informa; Hatsu NUNCA cambia estas configuraciones. Que aparezcan no prueba que sean LA causa (no está verificado que cada una afecte a <c>RegisterApp</c>).
/// </summary>
public static class SystemBlockers
{
    /// <summary>Servicios de Windows relacionados con teléfono/Bluetooth, con su nombre visible en español.</summary>
    public static readonly (string Service, string Display)[] Services =
    [("PhoneSvc", "Servicio telefónico"), ("CDPSvc", "Servicio de plataforma de dispositivos conectados"), ("BTAGService", "Servicio de puerta de enlace de audio Bluetooth"), ("bthserv", "Servicio de compatibilidad con Bluetooth")];

    /// <param name="enableMmx">Valor de HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableMmx (directiva «Vinculación teléfono-PC en este dispositivo»): 0 = bloqueada; null = no definida.</param>
    /// <param name="serviceStart">Valor «Start» de cada servicio (HKLM\SYSTEM\CurrentControlSet\Services\&lt;nombre&gt;): 4 = deshabilitado; null = no se pudo leer.</param>
    public static string? Describe(int? enableMmx, IReadOnlyDictionary<string, int?> serviceStart)
    {
        var parts = new List<string>();
        if (enableMmx == 0) parts.Add("la directiva «Vinculación teléfono-PC en este dispositivo» está en «No vincular» (EnableMmx=0): es el motivo de «El administrador del sistema ha bloqueado esta característica» en Enlace Móvil");
        var off = Services.Where(s => serviceStart.TryGetValue(s.Service, out var v) && v == 4).Select(s => $"«{s.Display}» ({s.Service})").ToList();
        if (off.Count > 0) parts.Add("servicios deshabilitados: " + string.Join(", ", off));
        return parts.Count == 0 ? null : string.Join("; ", parts) + ". Suele deberse a un programa de optimización o a una directiva; Hatsu no cambia esto";
    }
}
