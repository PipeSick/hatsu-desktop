# Iteración 8 — Dock por tema, bandeja, Peripherals y Cleanup

Ramas: WinMac `feat/winmac-iteration-8` (base `3ab86ad`), NotchWin `feat/winmac-iteration-8` (base `b486220`; solo añade una prueba de contrato). Pin en `tools/notch-pin.txt`. Sin PR, merge ni release. Sin cambios en llamadas/HFP ni en las letras sincronizadas.

> Pruebas automáticas y capturas de CI = lógica y dibujo con datos de MUESTRA. **No validan hardware real**: nada de Peripherals se ha probado con tus dispositivos.

## 1. Diagnóstico de los dos problemas heredados
**Dock (mismo tinte en claro y oscuro)** — dos causas demostradas en código:
1. `DockHost` guardaba el `GlassStyle` en un campo de solo lectura resuelto UNA vez al arrancar: cambiar el modo de Windows no hacía nada hasta reiniciar.
2. Los presets tenían un tinte muy tenue (α 0,20 claro / 0,40 oscuro) y saturación 1,0: el fondo de pantalla desenfocado mandaba, así que con un fondo rosa/lila ambos temas se veían rosados. Además las marcas (indicadores, separadores) eran blancas también en claro.
Corrección: material propio por tema (oscuro = grafito con violeta sutil α 0,62, saturación 0,75; claro = blanco translúcido α 0,42+0,10, marcas oscuras), cambio en caliente (aviso `ImmersiveColorSet` + repaso de 2 s) que solo reconstruye pincel y desenfoque. Posición, animaciones, autoocultación y configuración guardada no se tocan. Pruebas: diferencia de material, tinte violeta sutil, contraste de marcas ≥ 3:1 sobre fondo negro/medio/blanco, y reglas de cambio (`DockThemeTracker`).

**Bandeja (el panel se ocultaba con el menú abierto)** — causa más probable (inferida; no reproducida aquí): la iteración 7 solo reconocía menús de clase `#32768` y se rendía a los 1,5 s; las apps con menús propios (Qt, Electron, XAML…) no eran detectadas y el panel se cerraba "a los pocos segundos". Corrección: tres señales (ventana `#32768` de otro proceso; modo menú del hilo con `GetGUIThreadInfo`; ventana emergente nueva de la app mientras tiene el foco), espera de 2,5 s, margen de 0,6 s entre submenús, y **sin cierre por tiempo** cuando no se detecta nada: el panel sigue mientras el primer plano sea el panel o la app y se cierra al pasar a otra cosa. Límite: 10 min si una señal quedara pegada. No se interviene en el foco ni en el cierre de menús de terceros (solo lectura).
Limitación: si una app muestra un menú que no es observable por ninguna señal y mantiene el foco, el panel permanece hasta que el usuario cambie de ventana (o pulse Esc/abra otro panel). Pendiente verificar con Win32 clásico, Qt, Electron y WinUI.

## 2. Peripherals — matriz de compatibilidad (por método y modelo)
Estado de WinMac: **Confirmado = verificado con hardware (ninguno todavía)**; Experimental = implementado con la interfaz estándar, sin verificar; No disponible = sin interfaz viable/documentada (no se implementa nada).

| Método / dispositivo | Estado | Notas |
|---|---|---|
| Bluetooth clásico emparejado: conexión + propiedad de batería de Windows | Experimental | La propiedad `{104EA319-…} 2` no está documentada; solo con el dispositivo conectado |
| Bluetooth LE: servicio Battery (0x180F / 0x2A19) | Experimental | Solo si ya está conectado (no se despierta nada); lectura sin caché |
| HID/USB con clase Batería de Windows (IOCTL de batería) | Experimental | Excluye SAI/UPS por nombre; la mayoría de ratones/teclados no usan este canal |
| ASUS ROG Keris II Origin — Bluetooth | Experimental (vía BT estándar, si lo expone) | No verificado |
| ASUS ROG Keris II Origin — receptor 2,4 GHz | **No disponible** | Protocolo propietario de Armoury Crate; sin interfaz documentada/autorizada |
| ASUS ROG Strix Scope II 96 — Bluetooth | Experimental | Teclado: si el modo BT expone Battery Service; no verificado |
| ASUS ROG Strix Scope II 96 — 2,4 GHz / USB | **No disponible** | Idem; por cable no hay batería que consultar |
| ASUS ROG Delta II (auriculares) | Experimental en BT si lo expone; 2,4 GHz **No disponible** | No verificado |
| Razer Kitty (auriculares) | **No disponible / no aplicable** si es la edición USB con cable (sin batería); si fuera un modelo Bluetooth, Experimental | A confirmar con el modelo exacto |
| Ratón ASUS edición Hatsune Miku | Experimental en BT; 2,4 GHz **No disponible** | No verificado |
| Integraciones de fabricante (Armoury Crate, Synapse, G HUB…) | **No implementado** | Sin interfaz documentada y autorizada; no se hace ingeniería inversa |

Reglas implementadas y probadas: porcentaje solo 0–100 (255, −1, 101… se descartan); sin dato = «Batería desconocida», nunca 100 %; «Cargando» solo si el dispositivo lo informa en una lectura actual; lectura > 15 min = «lectura antigua»; desconectado = última lectura marcada; un proveedor que falla o no responde (plazo 8 s) no bloquea ni borra lo conocido y la tarjeta lo dice; alertas con umbral configurable (5–50 %), histéresis de 5 puntos, intervalo mínimo de 4 h, sin repetir al reconectar, solo con lectura actual y fuera de carga; el aviso va por las notificaciones de la Isla. Apagado, no existen proveedores ni temporizadores ni datos en memoria; encendido solo lee al abrir el panel/«Actualizar» y cada 10 min si hay alertas.

## 3. Cleanup — categorías
| Categoría | Qué trata | Tras confirmar | Notas |
|---|---|---|---|
| Archivos temporales del usuario | `%TEMP%`, > 24 h sin modificar | Automático tras confirmar | En uso → se omite |
| Caché de internet de Windows | `…\INetCache`, > 24 h | Tras confirmar | Se regenera |
| Caché de sombreadores | `…\D3DSCache`, > 24 h | Tras confirmar | Se regenera |
| Volcados de errores | `…\CrashDumps`, > 7 días | Tras confirmar | Solo diagnóstico |
| Informes de errores (WER) del usuario | `…\WER\ReportArchive/Queue`, > 7 días | Tras confirmar | |
| Registros antiguos de WinMac | `…\WinMac\logs`, `.log/.txt`, > 14 días | Tras confirmar | Los actuales no se tocan |
| Papelera | API del shell | **Segunda confirmación explícita**, nunca marcada por defecto | Definitivo |
| Restos de instalaciones/actualizaciones de Windows | — | **Solo revisión** | Exige administrador; WinMac no eleva privilegios |
| Restos de apps desinstaladas | — | **Solo revisión** | No se puede demostrar que estén huérfanos; no se borra por nombre |

Excluido por diseño: archivos grandes, duplicados, documentos/fotos/descargas/proyectos, registro de Windows, limpieza automática o en segundo plano.
Seguridad (todo con pruebas): analizar → confirmar → limpiar; raíz aceptada solo si es segura (no unidad, no superficial, no dentro ni alrededor de Documentos/Escritorio/Descargas/Imágenes/OneDrive/Windows/Program Files…; un `TEMP` redirigido a Documentos se rechaza); contención por segmentos de ruta; **no se siguen** enlaces simbólicos, junctions ni reparse points (se omiten); cada archivo se revalida justo antes de borrar (existe, no es enlace, dentro de la raíz, mismo tamaño y fecha, sigue siendo antiguo); un análisis manipulado con rutas fuera de la raíz no borra nada; los archivos abiertos por otro proceso no se borran (se abren sin compartir antes); bloqueado/sin permiso/cambiado se cuentan y el espacio liberado es solo el de lo efectivamente borrado; cancelación entre archivos; carpetas vacías resultantes se quitan, nunca la raíz; los registros no incluyen nombres de archivos.
Isla: actividad de extensión (`notch.activity`, id `winmac-cleanup`) con estados *Analizando archivos temporales*, *Espacio recuperable encontrado*, *Limpiando archivos seleccionados*, completada/parcial/cancelada; no sustituye música, letras ni notificaciones (prueba de contrato en NotchWin).

## 4. Ajustes
Barra superior › Periféricos (icono, avisos, umbral, mostrar ocultos) y Limpieza (icono, progreso en la Isla). Todos apagados por defecto salvo «progreso en la Isla». Los ajustes anteriores no cambian (las filas nuevas se buscan por nombre de campo).

## 5. Limitaciones y no implementado
- Nada de Peripherals está verificado en hardware; no hay lectura por receptores 2,4 GHz ni por software de fabricante.
- Restos de apps desinstaladas y residuos de instalación/actualización: solo informativos.
- Sin limpieza automática. Sin detección de duplicados/archivos grandes.
- El ciclo de vida de menús de bandeja no es observable en todos los casos (ver §1).

## 6. Pruebas manuales en hardware real
1. Dock: cambiar el modo de Windows con un fondo rosa y con uno oscuro; indicadores y separadores legibles; autoocultación y posición intactas.
2. Bandeja: clic derecho en iconos con menús Win32 clásico, Qt, Electron, WinUI, Volumen y «Quitar hardware»: el panel sigue; se cierra al cerrar el menú, con Esc y al hacer clic fuera.
3. Periféricos con tus 5 dispositivos en cada modo (BT / 2,4 GHz / USB): ver qué aparece y qué no; comparar el % con el software del fabricante; apagar el dispositivo → «Desconectado» con última lectura; mover el ratón fuera de alcance; comprobar que no se inventa 100 %.
4. Alertas: bajar del umbral una vez; reconectar sin repetir; cargar > umbral+5 y volver a bajar.
5. Desactivar el módulo: sin proceso ni sondeos (Administrador de tareas / Process Monitor).
6. Cleanup: analizar (no debe borrar nada); marcar y confirmar; abrir un archivo temporal en una app y comprobar que se omite; vaciar Papelera (segunda confirmación); cancelar a mitad; comprobar la Isla con música y letras activas; junction dentro de `%TEMP%` apuntando a una carpeta de prueba (no debe tocarse).
7. Claro/oscuro, 2 monitores y DPI distinto en los dos paneles.
