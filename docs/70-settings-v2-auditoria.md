# 70 · Hatsu Settings V2 — Fase 0: auditoría y plan

Etiquetas: **OBSERVADO** (leído del código o generado ahora con el constructor real de Ajustes), **INFERIDO** (deducido, sin ejecutarlo en Windows), **PENDIENTE** (decisión o medición que falta).
Nada de esto está implementado: es la auditoría previa. No se ha cambiado ninguna página de Ajustes.

## 1. Ramas y base de trabajo (OBSERVADO)
| Rama | Estado |
|---|---|
| `origin/main` | `8e31db0` (2.0.10). Intocada. |
| `feat/hatsu-modulos-mission-snap` | Local, 10 commits por delante de su remota (los «10 arreglos menores»). Esos 10 ya están también en la remota `feat/notch-v3-integracion`. |
| `feat/notch-v3-hatsu` | Accesos rápidos del Notch V3 (`a686b45`), sobre la anterior. |
| `feat/notch-v3-integracion` (remota) | Contiene **todo lo anterior** + pin de NotchWin + versión 2.0.14: 37 commits sobre `main`, 16 sobre la remota de mission-snap. **Es la base.** |
| **`feat/settings-v2` (propuesta, creada en local)** | Sale de `feat/notch-v3-integracion` (`70f00d7`). No contiene nada más que esta documentación. Sin push. |

NotchWin: `feat/notch-v3` sigue siendo la rama del Notch V3; `tools/notch-pin.txt` de Hatsu apunta a ella solo en la rama de integración. El pin estable no se toca.

## 2. Arquitectura actual de Ajustes (OBSERVADO)
- **Dibujada a mano con Direct2D/DirectWrite (Vortice)** sobre una cadena de intercambio DXGI (`WinMac.Host/SettingsWindow.cs` 533 líneas, `SettingsPainter.cs` 289). No hay controles nativos ni XAML: los campos de texto se dibujan en el mismo lienzo.
- **Modelo puro y probable en Linux**: `SettingsBuilder.Build` (`SettingsModel.cs`) convierte la configuración en `SettingsPage → SettingsGroup → SettingsRow`. Tipos de fila: `Toggle, Segmented, Field, Slider, Button, Info, Preview`. Filas «sin controles decorativos»: lo no usable se atenúa con su `Reason`.
- **Organización** (`SettingsNav.cs`): reagrupa las páginas en cinco bloques sin cambiar ningún id de fila.
- **Layout** (`SettingsLayout.cs`): **coordenadas absolutas en DIP**, ventana fija `860 × 700`, barra lateral `240`, alturas de fila calculadas con una **estimación** de ancho de carácter (`6,4 DIP/car.` en `Lines()`), no con la medida real de DirectWrite.
- **Ventana**: estilo `WS_OVERLAPPED|CAPTION|SYSMENU|MINIMIZEBOX` = **tamaño fijo, sin maximizar ni redimensionar**. DPI del **sistema** (`GetDpiForSystem`) y se rehace en `WM_DPICHANGED`.
- **Tema**: solo sigue el tema de Windows (`SystemDark()`). **No existe** selector Claro/Oscuro/Automático en Ajustes. Paleta estilo iOS (fondo negro absoluto en oscuro).
- **Búsqueda** (`SettingsController`): filtra filas y muestra una página de resultados; **no** salta a la fila dentro de su página.
- **Pruebas existentes**: `SettingsTests` (22), `SettingsPrototypeParityTests` (6), `ModuleSettingsTests`, `ClipboardSettingsTests`, `ConnectDiagnosticsSettingsTests`.

## 3. Inventario actual (OBSERVADO, generado con el constructor real y la configuración por defecto)
**12 páginas, 207 filas** (`docs/settings-v2-matriz.tsv` lista cada fila con su id, tipo, título, página/grupo actual, valor por defecto y **destino en V2**).

| Página actual | Grupos | Filas | Destino V2 |
|---|---|---|---|
| General | 2 | 9 | General · Extensiones y diagnóstico |
| Barra superior | 11 | 45 | Barra superior · Atajos y gestos |
| Dock (+ barra de tareas de Windows) | 3 | 10 | Dock |
| Isla (Hatsu Notch) | 5 | 27 | Isla |
| Módulos | 6 | 28 | Módulos (Gestures → Atajos y gestos) |
| Atajos y gestos | 1 | 14 | Atajos y gestos |
| Búsqueda (Spotlight) | 3 | 6 | Spotlight |
| Notificaciones y portapapeles | 5 | 13 | Notificaciones y portapapeles |
| Capturas y grabación | 2 | 7 | Capturas y grabación |
| Clima | 3 | 7 | Clima |
| iPhone y periféricos | 8 | 39 | **iPhone** (30) + **Periféricos** (9) |
| Extensiones y diagnóstico | 2 | 2 (+dinámicas) | Extensiones y diagnóstico |

Reparto por página V2 (suma 207, **ninguna fila sin destino**): Barra superior 42 · iPhone 30 · Módulos 28 · Isla 27 · Atajos y gestos 17 · Notificaciones y portapapeles 13 · Dock 10 · Periféricos 9 · Capturas 7 · Clima 7 · General 6 · Spotlight 6 · Extensiones y diagnóstico 5.
Filas dinámicas que no aparecen en la tabla por defecto (se conservan): reintentos de módulos con problemas, extensiones instaladas, ciudades adicionales del Clima, lanzadores de Gestures, estado del diagnóstico, sugerencias.

### Dependencias entre opciones (OBSERVADO)
- Módulo apagado → sus filas se atenúan con «Activa el módulo» (Mission Control, Snap, Live Text, Gestures).
- Hatsu Connect apagado → batería, notificaciones, llamadas, Notch, avisos y privacidad atenuados.
- «Letra en el notch cerrado» exige «Letras sincronizadas»; «Ruta del reproductor» solo con «Otro».
- Sin Hatsu Notch instalado → página de Isla informa y atenúa.
- «Controlar llamadas» y su estado dependen del diagnóstico; contestar/rechazar sigue **sin ofrecerse** (sin audio verificado).
- Dock: lee/escribe `dock.json`; si no se puede leer, la página lo dice y no muestra controles falsos.

### Persistencia, validaciones y comunicación (OBSERVADO / INFERIDO)
- `winmac.json` vía `ConfigService.Update` (atómico, notifica `Changed`, guarda `LastSaveError`). `dock.json` y `bar.json` siguen aparte. Ajustes de la Isla dentro de `HostConfig.Notch`.
- Los cambios llegan a Barra/Isla **en vivo** por tópicos retenidos (`config.bar`, `config.notch`, `host.cmd`, `notch.cmd`, `notch.feed`, `notch.quick`). Los módulos se arrancan o detienen al cambiar su interruptor (supervisor); el reintento manual existe para módulos caídos. **INFERIDO:** ninguna opción exige reiniciar a mano; falta verificarlo opción por opción en Fase 6.
- Validaciones: campos numéricos con rango (`Fie` con límites, p. ej. elementos de Shelf+ 1–100), atajos con detección de conflicto (`HotkeyStatus`), rutas y ciudades.
- Opción experimental visible: «Salida por aplicación (experimental)» y experimentos HFP del diagnóstico.

### Datos en vivo que **no** viven en el host (OBSERVADO)
Estado del iPhone (batería, conexión) y la lista de periféricos viven en el proceso de la **Barra** (`BarHost.Connect`, `BarHost.Peripherals`), no en el host que dibuja Ajustes. Para tarjetas reales de dispositivos hace falta un **feed de solo lectura Barra → Host** (hoy solo existe Barra → Isla). Ver decisión D5.

## 4. Problemas de diseño actuales
1. **Superposición de campos y descripciones (causa estructural, OBSERVADO en el código):** la altura de fila se calcula con `Lines()` (estimación de 6,4 DIP por carácter) y no con la medida real de DirectWrite; con otra fuente, DPI o texto largo el cálculo se queda corto y el control se dibuja sobre la descripción. El arreglo de raíz es medir con el mismo motor que dibuja.
2. Tamaño fijo 860×700: no se adapta a pantallas pequeñas ni aprovecha las grandes; sin maximizar.
3. DPI por sistema, no por monitor: al cambiar de monitor con otro escalado se reajusta, pero la ventana no es *per-monitor v2* (INFERIDO; probar en Fase 6).
4. Sin selector de tema; fondo negro absoluto y tarjetas grises (lo que la dirección visual pide evitar).
5. Páginas largas en lista plana (Barra superior: 45 filas, iPhone: 39); sin secciones desplegables ni vistas previas fuera de una tira estática.
6. Búsqueda: no salta a la configuración.

## 5. Mockups frente a capacidades reales
Revisados: 01, 02, 03, 04, 05, 06, 07, 08, 09 y 12 (**10 y 11** son variantes de Barra y Dock; los reviso al fijar el sistema en Fase 1). **No hay mockup** de Spotlight, Notificaciones y portapapeles, Capturas y grabación, Clima, Periféricos (los conceptos A/B los muestran juntos con iPhone) ni Extensiones y diagnóstico: se diseñan extendiendo el sistema, y los apruebas con captura real.

Regla aplicada: lo que el mockup muestra y **no existe** no se simula. Se lista aquí; por defecto **no se incorpora** salvo que tú lo pidas.

| Página | El mockup muestra | Realidad | Propuesta |
|---|---|---|---|
| General | «Buscar actualizaciones» y «Canal Estable» | No hay actualizador propio (Store/MSI) ni canales | Mostrar versión; sin botón de actualizar ni canal |
| General | «Ejecutándose» | Hay estado real por módulo (supervisor) | Mostrar el estado real; no inventar «salud» global |
| General | «Ejecutar en segundo plano», «Restaurar al iniciar», **Idioma** | No existen (Hatsu solo está en español; inicio automático sí) | No mostrar; Idioma solo si quieres traducirlo (proyecto aparte) |
| General | Tarjetas «Módulos», «Notificaciones», «Portapapeles» con interruptor | Notificaciones/Portapapeles no son módulos de Hatsu; son funciones de la Barra y una pestaña del Notch | Componentes reales: Barra, Dock, Hatsu Connect, Isla. Atajos a las demás páginas (D4) |
| Barra | «Control de música», «Notificaciones», «Portapapeles» como elementos | No existen como iconos de la barra | Solo los reales: Sonido, Rendimiento, Red, Discos, Tiempo, Wi-Fi, Bluetooth, Batería, Apps en segundo plano + VPN, Limpieza, Periféricos, Administrador de tareas |
| Barra | Agrupación (por grupos/juntos/automática), **Altura 28 px**, **Densidad**, Modo compacto/completo | No existen; «Estilo» real = Vidrio/Transparente | No incorporar (D4) |
| Dock | Posición **inferior/superior/izq./der.** | Solo borde inferior; sí hay alineación Centro/Izq./Der. | Usar la alineación real |
| Dock | Ampliación con **factor 1,5×**, «Animaciones», «Mantener en primer plano» | Solo interruptor de ampliación; sin factor/animaciones/primer plano | No incorporar (D4) |
| Isla | Reordenar «Actividades y prioridad» | La prioridad es fija (`HomeFocus`) | Mostrarla **solo lectura** (D4) |
| Isla | **Color de acento**, tema Automática/Clara/Oscura de la isla, **Tamaño**, «Mostrar al acercar», «Ocultar tras 5 s», «Agregar pestaña» | No existen (acento fijo violeta V3, glass y posición sí; pestañas son una lista fija de 13) | Conservar: posición, vidrio, duraciones, pestañas. No incorporar el resto (D4) |
| Módulos | 12 «módulos» (Clima, Música, Notificaciones, Portapapeles, Capturas, Temporizador, Calendario, Descargas…) | Son **pestañas del Notch o funciones de la Barra**; los módulos reales (FeatureManager) son Barra, Dock, Hatsu Connect, Isla, Mission Control, Snap, Live Text, Spotlight Actions, Gestures, Shelf+ | Galería con los reales; contador y estado desde el registro. No segundo sistema (D4) |
| Atajos | **Gestos del ratón: rueda sobre la barra** | No existe (sí botones laterales X1/X2) | No incorporar |
| Atajos | Tabla de atajos editables (Abrir isla, Captura, Temporizador…) | Solo atajos de módulos y 2 personalizados; modificadores de capturas | Mostrar los reales con su estado/conflicto; editar los que ya se editan |
| Atajos | «Gestos multitáctiles (Win Precision)» | **No permitido** por las APIs usadas (ya documentado) | No incorporar |
| Atajos | «Indicador de esquina», «Evitar activaciones», «Atajos globales» | Existe la espera en la esquina (ms); el resto no | Solo la espera |
| iPhone | **«Desemparejar»** | Se empareja/quita en Configuración de Bluetooth de Windows | Botón «Abrir Bluetooth de Windows» |
| iPhone | **Estado de carga ⚡ «Cargando»** | **No disponible** por Bluetooth (ya documentado en la página) | Solo batería % y conexión |
| iPhone | Imagen del dispositivo | Hatsu no conoce el modelo | Icono genérico de iPhone |
| iPhone | Estados: desactivado, sin emparejar, conectando, conectado, desconectado, error, permisos | Parte de esos estados existen en `PhoneLinkStatus`; permisos/error de conexión se derivan del diagnóstico | Implementar los que se pueden distinguir; el resto no se finge |
| Periféricos | Tarjetas por dispositivo | Hay proveedores y batería en la Barra | Necesita feed Barra→Host (D5). Sin batería = «Sin dato», nunca un %. |

## 6. Matriz de correspondencia
`docs/settings-v2-matriz.tsv`: 207 filas, columnas `id · tipo · título · página actual · grupo actual · valor por defecto · PÁGINA V2 · SECCIÓN V2`. La prueba de Fase 1 comprobará que cada id sigue existiendo en la V2 (no se pierde ninguna).

## 7. Arquitectura visual propuesta
**Recomendación (D1): mantener Direct2D propio y rehacer el motor de layout y el pintor**, no pasar a WinUI 3. Razones: el host es Win32 puro sin XAML ni Windows App SDK (un proceso más pesado en el arranque); el modelo `SettingsBuilder` ya es puro y probado; se conserva la ventana «dibujada» que el equipo ya validó; y el defecto actual (superposición) se arregla en el medidor, no cambiando de tecnología.

1. **Tokens y temas** (`SettingsTheme`, puro): claro, oscuro (grafito + lavanda) y automático. Campo nuevo `settingsTheme` (`System` por defecto) en `HostConfig`; ver §9.
2. **Motor de layout fluido** (`SettingsFlow`, puro, probado en Linux): medir/colocar con `Stack`, `Wrap` y `Grid` responsive, anchos mínimos, columnas que se reorganizan, alturas por contenido. Mide texto con una abstracción `ITextMeasurer` (DirectWrite en Windows, estimador solo en pruebas). **Sin coordenadas absolutas.**
3. **Pintor** reescrito sobre los tokens: cristal moderado con respaldo sólido si el efecto de Windows no está disponible, bordes finos, sombras suaves.
4. **Componentes compartidos**: encabezado de página, barra lateral, buscador, tarjeta de ajuste, tarjeta de módulo, tarjeta de dispositivo, interruptor, slider, selector segmentado, campo, lista reordenable, indicador de estado, vista previa, sección desplegable, diálogo, tooltip, estado vacío, error, carga. Una sola implementación por componente.
5. **Ventana**: redimensionable y maximizable, tamaño mínimo (~900×600), *per-monitor DPI v2*, navegación por teclado.
6. **Páginas bajo demanda**: se construyen al abrirse y se liberan al cerrarse; un solo temporizador compartido; las vistas previas se dibujan con la configuración, **sin iniciar servicios**.
7. **Modelo**: se amplía `SettingsRow`/`SettingsPage` de forma compatible (nuevos tipos: tarjeta, vista previa, desplegable, dispositivo, galería) sin cambiar ids. Búsqueda: resultado con salto a la fila y resalte.

## 8. Rendimiento
Medición base (antes de tocar nada): memoria y CPU del host con Ajustes cerrado/abierto en la versión actual; repetir tras cada fase. **PENDIENTE** (necesita Windows real; el CI no es representativo). Compromisos: sin animaciones permanentes, un solo timer, transparencia de Windows con respaldo sólido.

## 9. Compatibilidad y persistencia
- Un único campo nuevo previsto: `settingsTheme`. Valor por defecto `System`; `HostConfig` carga de forma tolerante, así que las configuraciones existentes siguen valiendo sin migración. Prueba de ida y vuelta con un `winmac.json` antiguo.
- Cualquier otro campo nuevo (D4) pasa por aquí con la misma regla y prueba antes de añadirlo.
- Los ids de fila no cambian; la matriz lo vigila.

## 10. Plan por fases (rama `feat/settings-v2`, commits pequeños)
| Fase | Contenido | Pruebas / CI |
|---|---|---|
| 0 (esta) | Auditoría, matriz, discrepancias, plan | Dump del constructor real |
| 1 | `SettingsTheme`, `SettingsFlow` + `ITextMeasurer`, componentes, ventana redimensionable, barra lateral, buscador con salto, **página General** como prueba de concepto | Linux: layout (sin solapes por construcción, anchos 640–3840, textos largos), ids de la matriz, temas. Windows: build + captura real |
| 2 | Barra superior, Dock, Isla (con vistas previas) | Linux + CI Windows |
| 3 | Módulos (galería sobre `FeatureManager`), Atajos y gestos, Spotlight | idem |
| 4 | Notificaciones y portapapeles, Capturas y grabación, Clima | idem |
| 5 | iPhone y Periféricos (feed Barra→Host), Extensiones y diagnóstico | idem + estados del iPhone |
| 6 | Regresión completa, DPI 100–200 %, 1280×720 → 3840×2160, persistencia, rendimiento, **build portable** | CI Windows + lista manual |

**Build portable integrada con Notch V3** (procedimiento): `tools/Build-Local.ps1` genera `dist/WinMac` (host, Barra, Dock, Connect y NotchWin del pin vigente); el CI publica el mismo contenido como artefacto zip y como MSIX sin firmar. Para probar Settings V2 + Notch V3 juntos, la rama `feat/settings-v2` hereda el pin de `feat/notch-v3`. Documentaré el procedimiento exacto en Fase 6.

Cada fase termina con: implementado · verificado en Linux · compila en Windows · pendiente de revisión visual en tu PC. **Una compilación correcta no se presenta como aprobación visual**, y los HTML de los mockups no se presentan como capturas de la UI real.

## 11. Decisiones que necesito de ti
- **D1** Tecnología: Direct2D propio con motor de layout nuevo (recomendado) o WinUI 3.
- **D2** Ventana redimensionable y maximizable (el mockup lo muestra): sí (recomendado).
- **D3** Campo nuevo `settingsTheme` (Automático/Claro/Oscuro) con migración trivial: sí (recomendado).
- **D4** Funciones del mockup que hoy no existen (§5): por defecto **ninguna**. ¿Quieres alguna (p. ej. color de acento de la Isla, factor de ampliación del Dock)? Cada una implica backend y prueba.
- **D5** Feed de solo lectura Barra → Host para tarjetas del iPhone y de periféricos (no recoge datos nuevos). Necesario para las páginas 11 y 12 con datos reales.
- **D6** Autorización de **push de validación** de `feat/settings-v2` para el CI de Windows (solo esa rama, sin merge ni pines).

## 12. Riesgos
- Reescribir el pintor toca la ventana que ya funciona: se hace detrás de la misma interfaz de modelo y con el pintor antiguo disponible hasta Fase 6.
- Las capturas de CI no son tu pantalla; la aprobación visual es tuya.
- Medidas de rendimiento solo fiables en Windows real.

## 13. Decisiones del propietario (registradas tras la Fase 0)
- **D1-D3 aprobadas:** Direct2D propio con motor de layout nuevo (mide el texto real), ventana redimensionable/maximizable, campo `settingsTheme` (Automático por defecto).
- **D4 (a construir de verdad, con backend y prueba cada una):** color de acento de la Isla · factor de ampliación del Dock (slider) · altura y densidad de la Barra. Cada una añade un campo de configuración con valor por defecto igual al comportamiento actual (sin cambiar lo que ya ve el usuario) y prueba de ida y vuelta con un `winmac.json` antiguo. Quedan **fuera**: agrupación de la Barra, posición del Dock en otros bordes, reordenar prioridades de la Isla, rueda del ratón, gestos multitáctiles.
- **D5 autorizada:** feed de solo lectura Barra → Host (nombre, conexión, batería que ya se leen). Sin dato = «Sin dato».
- **D6 autorizada:** push de validación **solo de `feat/settings-v2`** para el CI de Windows. Sin merge, pines ni releases.
