using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Bar;

public enum BarBackground { Transparent, Glass }
public enum BarThemeMode { System, Light, Dark }
public enum BarTheme { Light, Dark }

/// <summary>Configuración de la barra superior (bar.json). La carga es tolerante: un archivo ausente o corrupto devuelve los valores por defecto.</summary>
public sealed record BarConfig
{
    public int Version { get; init; } = 1;
    /// <summary>Aspecto provisional (D5 de docs/12): transparente como en Tahoe, o con vidrio desenfocado.</summary>
    public BarBackground Background { get; init; } = BarBackground.Glass;
    public BarThemeMode Theme { get; init; } = BarThemeMode.System;
    /// <summary>Reservar el borde superior de la pantalla (AppBar) para que las ventanas maximizadas queden debajo. false = superpuesta.</summary>
    public bool ReserveSpace { get; init; } = true;
    /// <summary>"es" o "en" (nombres de día y mes del reloj). Tablas propias: no dependen de ICU ni de la cultura del sistema.</summary>
    public string Language { get; init; } = "es";
    public bool Use24Hour { get; init; } = true;
    public bool ShowSeconds { get; init; } = false;
    public bool ShowDate { get; init; } = true;
    public bool ShowBatteryPercent { get; init; } = true;
    public bool ShowBattery { get; init; } = true;
    public bool ShowVolume { get; init; } = true;
    public bool ShowNetwork { get; init; } = true;
    /// <summary>Fracción central del ancho que se deja libre (p. ej. para una app tipo «notch»). 0 = sin hueco.</summary>
    public double CenterGap { get; init; } = 0.30;

    // ── Control Center, paneles y menús (la barra «real»)
    public bool ShowBluetooth { get; init; } = true;
    public bool ShowControlCenter { get; init; } = true;
    /// <summary>EXPERIMENTAL: mostrar junto al nombre los menús de la app activa (solo apps Win32 con barra de menús estándar). Apagado por defecto.</summary>
    public bool AppMenus { get; init; } = false;
    /// <summary>Pedir confirmación antes de reiniciar, apagar o cerrar sesión. «Forzar salida» la pide siempre.</summary>
    public bool ConfirmDestructive { get; init; } = true;
    /// <summary>Archivo .ics para los eventos del calendario desplegable (solo lectura). null = sin eventos.</summary>
    public string? CalendarIcsPath { get; init; }
    // Controles del Control Center que se muestran (un control sin respaldo en el equipo se muestra deshabilitado con su motivo, o se oculta aquí).
    /// <summary>Wi-Fi: al tocar una red se despliega con señal, potencia (dBm), banda (2,4 / 5 / 6 GHz) y seguridad, con la acción de conectar dentro. Apagado por defecto (la lista conecta con un toque).</summary>
    public bool WifiDetails { get; init; } = false;
    // ── Panel multifunción (un solo icono) y Tiempo (otro icono independiente)
    public bool ShowHub { get; init; } = true;
    public bool ShowWeather { get; init; } = true;
    /// <summary>Última pestaña del panel multifunción (se recuerda entre sesiones).</summary>
    public Hub.HubTab HubTab { get; init; } = Hub.HubTab.Sound;
    /// <summary>Cada cuánto se leen sensores, red y discos MIENTRAS el panel está abierto (con el panel cerrado no se lee nada).</summary>
    public int HubRefreshMs { get; init; } = 1000;
    public Hub.RateUnit RateUnit { get; init; } = Hub.RateUnit.Auto;
    /// <summary>Ciudad del Tiempo como «Nombre|lat|lon»; null = sin elegir (el panel lo pide).</summary>
    public string? WeatherCity { get; init; }
    /// <summary>Ciudades adicionales «Nombre|lat|lon;Nombre|lat|lon».</summary>
    public string? WeatherFavorites { get; init; }
    public bool WeatherFahrenheit { get; init; } = false;
    /// <summary>Usar la ubicación del equipo en vez de la ciudad elegida. Solo con permiso de Windows; apagado por defecto.</summary>
    public bool WeatherAutoLocation { get; init; } = false;
    public bool CcWifi { get; init; } = true;
    public bool CcBluetooth { get; init; } = true;
    public bool CcSound { get; init; } = true;
    public bool CcBrightness { get; init; } = true;
    public bool CcPower { get; init; } = true;
    public bool CcAppearance { get; init; } = true;
    public bool CcDisplay { get; init; } = true;
    public bool CcNotifications { get; init; } = true;

    // ── Launchpad y Spotlight (docs/22)
    /// <summary>Pulsar y soltar SOLA la tecla Windows abre Launchpad en vez del menú Inicio. Los atajos con Windows (Win+E, Win+R, Win+L…) siguen igual: no se bloquea ninguna tecla.</summary>
    public bool LaunchpadOnWinKey { get; init; } = true;
    /// <summary>Atajo de Spotlight. Alt+Espacio sustituye, mientras WinMac está abierto, al menú de ventana de Windows (Restaurar/Mover/Cerrar), igual que PowerToys Run.</summary>
    public Launcher.SpotlightHotkey SpotlightHotkey { get; init; } = Launcher.SpotlightHotkey.AltSpace;
    /// <summary>Spotlight busca también archivos: recientes de Windows y, si el servicio Windows Search responde, el índice de Windows (sin indexar nada por su cuenta).</summary>
    public bool SpotlightFiles { get; init; } = true;

    // ── Capturas (docs/26)
    /// <summary>Atajos Ctrl+Mayús+3 (pantalla), +4 (región) y +5 (barra de captura). Mientras WinMac está abierto tienen prioridad sobre la misma combinación en
    /// las apps; si otro programa ya los registró, Windows los rechaza y se dice en el registro y en Ajustes.</summary>
    public bool CaptureHotkeys { get; init; } = true;
    /// <summary>Formato del archivo: Png (sin pérdida, por defecto) o Jpg.</summary>
    public string CaptureFormat { get; init; } = "Png";
    /// <summary>Además de guardar, copiar la imagen al portapapeles.</summary>
    public bool CaptureCopyToClipboard { get; init; } = false;
    /// <summary>Carpeta de destino; vacía = la de capturas de Windows («Imágenes\Capturas de pantalla»), la misma que lee la galería de la Isla.</summary>
    public string? CaptureFolder { get; init; }
    /// <summary>Modificadores de los atajos de captura (con 3, 4, 5 y 6): CtrlShift (por defecto), CtrlAlt o AltShift.</summary>
    public string CaptureModifiers { get; init; } = "CtrlShift";
    /// <summary>WM-002: segundos de cuenta atrás antes de capturar (0, 3, 5 o 10), para capturar menús abiertos sin que el clic de la captura los cierre.</summary>
    public int CaptureDelaySeconds { get; init; } = 0;
    /// <summary>Quick Look: al guardar una captura, miniatura flotante unos segundos en la esquina; un clic abre la vista previa con acciones.</summary>
    public bool CaptureQuickLook { get; init; } = true;

    /// <summary>Iconos de la bandeja de Windows (apps en segundo plano) en la barra. WinMac recibe los iconos como lo hace la bandeja y se los reenvía a Explorer.</summary>
    public bool ShowTrayIcons { get; init; } = true;
    /// <summary>El menú Sonido de la barra incluye el mezclador: entrada predeterminada y volumen por aplicación. false = solo volumen general y salida (sin leer micrófono ni sesiones).</summary>
    public bool SoundMixer { get; init; } = true;
    /// <summary>WinMac Connect: indicador del iPhone (batería real y estado). Solo aparece con el módulo activo y datos reales.</summary>
    public bool ShowPhone { get; init; } = true;
    /// <summary>WM-035 (EXPERIMENTAL): elegir la salida de audio de cada app desde el menú Sonido. APAGADO por defecto: usa una interfaz interna de Windows (la de su propio mezclador).</summary>
    public bool AppOutputRouting { get; init; } = false;

    // ── Centro de Notificaciones (panel lateral; se abre con el reloj, como en macOS)
    /// <summary>El reloj abre el Centro de Notificaciones de WinMac. false = abre el calendario desplegable de antes.</summary>
    public bool NotificationCenter { get; init; } = true;
    /// <summary>Leer las notificaciones que Windows guarda en su centro (con el permiso de Windows). APAGADO por defecto: el usuario lo activa desde el propio panel.</summary>
    public bool NotificationsFromWindows { get; init; } = false;
    /// <summary>Al descartar o borrar en WinMac, eliminar también del centro de notificaciones de Windows. false = solo se ocultan en WinMac durante la sesión.</summary>
    public bool NotifRemoveFromWindows { get; init; } = true;
    /// <summary>Widgets del Centro de Notificaciones, en orden («calendar,weather,battery»). Vacío = ninguno.</summary>
    public string NcWidgets { get; init; } = Notifications.NcWidgets.Default;

    /// <summary>Formato de los mensajes entre el host y la barra (camelCase), el mismo patrón que los ajustes de la Isla.</summary>
    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Corrige lo inválido (archivo editado a mano o versión futura).</summary>
    public BarConfig Sanitize() => this with
    {
        Language = Language == "en" ? "en" : "es",
        CaptureModifiers = Launcher.HotkeyAudit.CaptureModifierIds.Contains(CaptureModifiers) ? CaptureModifiers : "CtrlShift",
        CenterGap = double.IsFinite(CenterGap) ? Math.Clamp(CenterGap, 0, 0.6) : 0.30,
        CalendarIcsPath = string.IsNullOrWhiteSpace(CalendarIcsPath) ? null : CalendarIcsPath.Trim().Trim('"'),
        HubRefreshMs = Math.Clamp(HubRefreshMs, 500, 10_000),
        CaptureDelaySeconds = CaptureDelaySeconds switch { <= 0 => 0, <= 3 => 3, <= 5 => 5, _ => 10 },
        WeatherCity = Hub.GeoPoint.Parse(WeatherCity)?.Serialize(),
        WeatherFavorites = Hub.GeoPoint.ParseList(WeatherFavorites) is { Count: > 0 } favs ? Hub.GeoPoint.SerializeList(favs) : null,
        NcWidgets = Notifications.NcWidgets.Serialize(Notifications.NcWidgets.Parse(NcWidgets ?? Notifications.NcWidgets.Default)),
    };

    /// <summary>Aplica un parche JSON (camelCase, solo los campos presentes): lo desconocido o de otro tipo se ignora campo a campo.</summary>
    public BarConfig ApplyPatch(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return this;
        try
        {
            var node = System.Text.Json.JsonSerializer.SerializeToNode(this, Wire)!.AsObject();
            foreach (var p in patch.EnumerateObject())
            {
                if (!node.TryGetPropertyValue(p.Name, out var cur) && !KnownNullable.Contains(p.Name)) continue;   // campo desconocido
                if (p.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) continue;
                bool ok = (cur?.GetValueKind(), p.Value.ValueKind) switch
                {
                    (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => true,
                    (JsonValueKind.Number, JsonValueKind.Number) => true,
                    (JsonValueKind.String, JsonValueKind.String) => true,
                    (null or JsonValueKind.Null or JsonValueKind.String, JsonValueKind.String or JsonValueKind.Null) when KnownNullable.Contains(p.Name) => true,   // texto opcional: se puede poner y quitar
                    _ => false,
                };
                if (!ok) continue;
                // campo por campo: un valor inválido (p. ej. un tema que no existe) descarta SOLO ese campo
                var previous = node.TryGetPropertyValue(p.Name, out var old) ? old?.DeepClone() : null;
                node[p.Name] = System.Text.Json.Nodes.JsonNode.Parse(p.Value.GetRawText());
                try { _ = System.Text.Json.JsonSerializer.Deserialize<BarConfig>(node, Wire); }
                catch (JsonException) { node[p.Name] = previous; }
            }

            return (System.Text.Json.JsonSerializer.Deserialize<BarConfig>(node, Wire) ?? this).Sanitize();
        }
        catch (Exception) { return this; }   // un parche ilegible nunca estropea la configuración
    }

    private static readonly HashSet<string> KnownNullable = new(StringComparer.OrdinalIgnoreCase) { "calendarIcsPath", "weatherCity", "weatherFavorites" };

    /// <summary>Lo que se envía al módulo (camelCase).</summary>
    public string ToWireJson() => System.Text.Json.JsonSerializer.Serialize(this, Wire);

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static BarConfig FromJson(string json) => JsonSerializer.Deserialize<BarConfig>(json, Json) ?? new BarConfig();

    public static BarConfig LoadOrDefault(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new BarConfig(); }
        catch (Exception) { return new BarConfig(); }
    }

    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp"; File.WriteAllText(tmp, ToJson()); File.Move(tmp, path, overwrite: true); return true;
        }
        catch (Exception) { return false; }
    }
}
