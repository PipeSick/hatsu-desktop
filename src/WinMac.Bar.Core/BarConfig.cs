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
    public bool CaptureCopyToClipboard { get; init; } = true;
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
    /// <summary>It7: porcentaje junto al icono del iPhone. false = solo el icono (el estado se ve en la sugerencia y en el panel).</summary>
    public bool ShowPhonePercent { get; init; } = true;
    /// <summary>It7: en un equipo SIN batería (sobremesa) mostrar un icono de enchufe sin porcentaje. Apagado por defecto: el módulo de batería se oculta.</summary>
    public bool ShowPowerWithoutBattery { get; init; } = false;
    /// <summary>It8: módulo Periféricos (batería de ratón, teclado, auriculares…). APAGADO por defecto: desactivado no se crea ningún proveedor ni temporizador.</summary>
    public bool ShowPeripherals { get; init; } = false;
    /// <summary>It8: avisar (por la Isla) cuando un periférico conectado baja del umbral. Apagado por defecto.</summary>
    public bool PeripheralAlerts { get; init; } = false;
    public int PeripheralAlertThreshold { get; init; } = Peripherals.PeripheralSettings.DefaultThreshold;
    /// <summary>It8: dispositivos ocultos por el usuario (ids separados por salto de línea).</summary>
    public string PeripheralHidden { get; init; } = "";
    /// <summary>It8: módulo Cleanup (icono de destellos). APAGADO por defecto. Nunca limpia por sí solo.</summary>
    public bool ShowCleanup { get; init; } = false;
    /// <summary>Periféricos ASUS ROG por el receptor ROG OMNI (docs/64). Envía al receptor la misma consulta que Armoury Crate; por eso es opt-in y viene APAGADO.</summary>
    public bool PeripheralAsusRog { get; init; } = false;
    /// <summary>Nombres que el usuario le pone al teclado y al ratón del receptor ROG OMNI (el receptor no dice el modelo). Vacío = el nombre genérico.</summary>
    public string PeripheralAsusKeyboardName { get; init; } = "";
    public string PeripheralAsusMouseName { get; init; } = "";
    /// <summary>Icono de VPN en la barra (conexiones VPN de Windows: ver, conectar y desconectar). APAGADO por defecto.</summary>
    public bool ShowVpn { get; init; } = false;
    /// <summary>It8: mostrar el progreso de Cleanup en la Isla.</summary>
    public bool CleanupIsland { get; init; } = true;
    /// <summary>It7: botón del Administrador de tareas en la barra (abre taskmgr.exe). Apagado por defecto.</summary>
    public bool ShowTaskManager { get; init; } = false;
    /// <summary>It7: orden elegido por el usuario de los módulos propios movibles («Network,Volume,…», ver <see cref="BarOrdering"/>). Vacío = orden predeterminado.</summary>
    public string BarOrder { get; init; } = "";
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

    // ── Módulos de productividad (Ajustes › Módulos). Cada uno se enciende y se apaga en caliente (FeatureManager); apagado no deja ganchos, atajos ni tareas.
    /// <summary>Ajustes › Módulos › «Reintentar»: cada incremento pide reiniciar los módulos en fallo (el canal de configuración solo lleva valores).</summary>
    public int FeatureRetryToken { get; init; } = 0;
    /// <summary>Hatsu Mission Control: vista general de las ventanas del escritorio virtual actual. APAGADO por defecto.</summary>
    public bool McEnabled { get; init; } = false;
    /// <summary>Atajo de Mission Control («Ctrl+Alt+Up»). Vacío = sin atajo (el módulo sigue activo: Spotlight y Gestures pueden abrirlo).</summary>
    public string McHotkey { get; init; } = "Ctrl+Alt+Up";
    /// <summary>Hatsu Snap: zonas al arrastrar una ventana con la tecla elegida. APAGADO por defecto.</summary>
    public bool SnapEnabled { get; init; } = false;
    /// <summary>Tecla que se mantiene al arrastrar para ver las zonas: Shift, Ctrl o Alt.</summary>
    public string SnapModifier { get; init; } = "Shift";
    /// <summary>Distribución activa de Snap (nombre de una predeterminada o guardada).</summary>
    public string SnapLayout { get; init; } = "Mitades";
    /// <summary>Separación (px) entre ventanas colocadas por Snap.</summary>
    public int SnapGap { get; init; } = 0;
    /// <summary>Ajustes › Módulos › «Editar zonas»: cada incremento abre el editor de zonas de Snap.</summary>
    public int SnapEditToken { get; init; } = 0;
    /// <summary>Atajo para colocar la ventana activa en la zona siguiente de la distribución activa. Vacío = ninguno.</summary>
    public string SnapNextHotkey { get; init; } = "";
    /// <summary>Hatsu Live Text: reconocer el texto de una región de la pantalla (OCR local de Windows). APAGADO por defecto.</summary>
    public bool LiveTextEnabled { get; init; } = false;
    public string LtHotkey { get; init; } = "Ctrl+Alt+Space";
    /// <summary>Conservar los saltos de línea al copiar el texto reconocido.</summary>
    public bool LtKeepLineBreaks { get; init; } = true;
    /// <summary>Copiar el texto al portapapeles en cuanto se reconoce (se puede copiar también desde el panel). El historial de la Isla lo recoge como cualquier copia: no hay un historial aparte.</summary>
    public bool LtAutoCopy { get; init; } = true;
    /// <summary>Live Text: enviar también el texto reconocido a la estantería de la Isla (Shelf+) al reconocerlo. Apagado por defecto: la estantería guarda cosas de forma duradera.</summary>
    public bool LtAutoShelf { get; init; } = false;
    /// <summary>Idioma de reconocimiento (etiqueta BCP-47, p. ej. «es-ES»). Vacío = los idiomas del perfil del usuario.</summary>
    public string LtLanguage { get; init; } = "";
    /// <summary>Spotlight Actions: registro central de acciones en Spotlight. Activado por defecto (no instala ganchos).</summary>
    public bool SpotActionsEnabled { get; init; } = true;
    /// <summary>Hatsu Gestures: esquinas activas y botones del ratón. APAGADO por defecto (instala un gancho de ratón mientras esté activo).</summary>
    public bool GesturesEnabled { get; init; } = false;
    public string GestCornerTL { get; init; } = "none";
    public string GestCornerTR { get; init; } = "none";
    public string GestCornerBL { get; init; } = "none";
    public string GestCornerBR { get; init; } = "none";
    /// <summary>Tiempo (ms) que el puntero debe quedarse en la esquina para disparar la acción.</summary>
    public int GestCornerDelayMs { get; init; } = 450;
    public string GestMouseX1 { get; init; } = "none";
    public string GestMouseX2 { get; init; } = "none";
    /// <summary>Accesos rápidos de Gestures (JSON: aplicaciones y páginas web, máximo 6). Ver <see cref="Features.Launchers"/>.</summary>
    public string GestLaunchers { get; init; } = "";
    public string GestKey1 { get; init; } = "";
    public string GestKey1Action { get; init; } = "none";
    public string GestKey2 { get; init; } = "";
    public string GestKey2Action { get; init; } = "none";

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
        BarOrder = BarOrdering.Serialize(BarOrdering.Parse(BarOrder)),
        PeripheralAlertThreshold = Peripherals.PeripheralSettings.ClampThreshold(PeripheralAlertThreshold),
        PeripheralHidden = Peripherals.PeripheralSettings.SerializeHidden(Peripherals.PeripheralSettings.ParseHidden(PeripheralHidden)),
        NcWidgets = Notifications.NcWidgets.Serialize(Notifications.NcWidgets.Parse(NcWidgets ?? Notifications.NcWidgets.Default)),
        McHotkey = Features.FeatureConfig.NormalizeHotkey(McHotkey, "Ctrl+Alt+Up"),
        LtHotkey = Features.FeatureConfig.NormalizeHotkey(LtHotkey, "Ctrl+Alt+Space"),
        SnapNextHotkey = Features.FeatureConfig.NormalizeHotkey(SnapNextHotkey, ""),
        GestLaunchers = Features.Launchers.Serialize(Features.Launchers.Parse(GestLaunchers)),
        PeripheralAsusKeyboardName = CleanName(PeripheralAsusKeyboardName), PeripheralAsusMouseName = CleanName(PeripheralAsusMouseName),
        GestKey1 = Features.FeatureConfig.NormalizeHotkey(GestKey1, ""), GestKey2 = Features.FeatureConfig.NormalizeHotkey(GestKey2, ""),
        SnapModifier = Features.FeatureConfig.SnapModifiers.Contains(SnapModifier) ? SnapModifier : "Shift",
        SnapLayout = string.IsNullOrWhiteSpace(SnapLayout) ? "Mitades" : SnapLayout.Trim(),
        SnapGap = Math.Clamp(SnapGap, 0, 64),
        LtLanguage = (LtLanguage ?? "").Trim().Length <= 16 ? (LtLanguage ?? "").Trim() : "",
        GestCornerDelayMs = Math.Clamp(GestCornerDelayMs, 150, 2000),
        GestCornerTL = Features.FeatureConfig.NormalizeAction(GestCornerTL), GestCornerTR = Features.FeatureConfig.NormalizeAction(GestCornerTR),
        GestCornerBL = Features.FeatureConfig.NormalizeAction(GestCornerBL), GestCornerBR = Features.FeatureConfig.NormalizeAction(GestCornerBR),
        GestMouseX1 = Features.FeatureConfig.NormalizeAction(GestMouseX1), GestMouseX2 = Features.FeatureConfig.NormalizeAction(GestMouseX2),
        GestKey1Action = Features.FeatureConfig.NormalizeAction(GestKey1Action), GestKey2Action = Features.FeatureConfig.NormalizeAction(GestKey2Action),
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

    private static string CleanName(string? n) { var t = new string((n ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim(); return t.Length > 40 ? t[..40].Trim() : t; }
}
