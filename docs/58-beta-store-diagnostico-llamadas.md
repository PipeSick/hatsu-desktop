# 58 — Beta firmada por la Store: diagnóstico de llamadas (Hatsu Connect)

Etiquetas: **OBSERVADO** (CI o código), **CONOCIMIENTO** (sobre Partner Center/Store: `learn.microsoft.com` no era accesible desde la sesión; **verifícalo en Partner Center antes de enviar**), **PENDIENTE-HW**.
**No se ha enviado ni publicado nada.** Stable no se tocó. La beta es el mismo producto (misma identidad) con un número de versión mayor.

## 1. Qué trae esta beta (2.0.2.0)
- Nuevo en **Ajustes › Hatsu Connect › Diagnóstico de llamadas (avanzado)**: ejecutar el diagnóstico, ver el estado y un resumen, abrir el informe, copiarlo, guardar una copia donde elijas, y un interruptor **temporal** de «Observar llamadas del sistema» (solo lectura, se apaga solo a los 15 min, no se guarda en la configuración ni toca variables de entorno).
- El diagnóstico lo ejecuta el propio módulo (`HatsuDesktop.Connect.exe --probe-calls`, que se conserva) lanzado por el host: no hace falta terminal ni buscar el ejecutable.
- Estados del informe: **Detectado · Disponible · Acceso denegado · No compatible · No comprobado · Error de ejecución**. Que no aparezca un transporte telefónico **no** se interpreta como incompatibilidad.
- Incluye: versión de Windows y de Hatsu, identidad de paquete (sí/no), `PhoneCallManager`, líneas telefónicas, transporte telefónico Bluetooth (solo `RequestAccessAsync`, no registra ni conecta), endpoints de audio Hands-Free, dispositivos Bluetooth emparejados con sus servicios (HFP-AG, PBAP, MAP, A2DP) **en caché**, y las últimas líneas del registro del observador.
- Privacidad: sin contenido de notificaciones, mensajes, audio, historial ni números completos (se enmascaran: `<número>…NN`); direcciones Bluetooth como `**:**:**:**:**:AB`; rutas y usuario reemplazados; IP ocultas. Nada se envía a ningún servidor; no hay telemetría. Las políticas de privacidad aprobadas no se modificaron.
- Sigue **sin** haber botones de contestar/rechazar/colgar: no hay mecanismo con audio del PC verificado (docs/56-57). La corrección de «Llamada finalizada» (cierre neutro) viaja en esta beta y **debe validarse en el iPhone**.
- No es solo lectura-a-medias: el diagnóstico **no** desconecta/conecta Bluetooth, no contesta, no marca, no registra la app en la línea y no conecta audio.

## 2. Observador (investigación)
`HATSU_CONNECT_SYSTEM_CALLS=1` seguía siendo el único modo. Ahora el host envía una orden puntual `connect.diag {observe}` por el IPC existente: el módulo crea/destruye el observador (`PhoneCallManager.CallStateChanged`), siempre desde cero (no hay duplicados), lo apaga a los 15 min, al desactivarlo, al cerrar y al reiniciarse el módulo. Registra `activa=…, entrante=…` sin números. **No** atribuye al iPhone una llamada de Teams u otra app: `CallTracker.OnTelephony` exige que ESTE iPhone acabe de sonar (ANCS). Sin esa correlación no se muestra nada en Hatsu Notch. Si el módulo Connect está apagado, el interruptor no tiene efecto (la orden no llega a nadie); el diagnóstico sí funciona aparte.

## 3. Empaquetado (OBSERVADO en el CI del commit de la beta, ver §6)
- Identidad **sin cambios**: `PipeSick.HatsuDesktop` · Publisher `CN=ACC0FCB8-4D92-4E20-9731-7254D5E43289` · PublisherDisplayName `PipeSick` · PFN `PipeSick.HatsuDesktop_83tr6qzbbarj8` · Store ID `9NJ0XLP6NXVJ` (`installer/msix/store-identity.json` no cambió).
- Capacidades **sin cambios**: `runFullTrust`, `bluetooth`, `location`, `webcam`, notificaciones; ninguna restringida nueva. `PhoneCallManager` puede pedir `phoneCall` (general): **no se declaró**; si Windows lo niega, el informe lo dirá como «Acceso denegado» (justo para eso sirve la beta).
- Versión del paquete: `2.0.2.0` (`src/Directory.Build.props` 2.0.1 → 2.0.2; la Store exige la 4.ª cifra a 0). Stable publicada: `2.0.1.0`.
- x64 (manifiesto y cabeceras PE), `MinVersion 10.0.19041.0`, tarea de inicio y `runFullTrust` comprobados por `tools/ci/Test-Msix.ps1`, que ahora además ejecuta el diagnóstico **desde el paquete instalado** (termina, escribe en una ruta escribible, trae resumen y no filtra usuario, perfil ni MAC). El runner no tiene Bluetooth: solo prueba que corre y degrada bien.
- El MSIX **sin firmar** se sube a Partner Center; **la Store lo firma**. La copia firmada con certificado de prueba del CI es solo para instalarla en el runner y **no sirve en tu PC ni dice nada de Smart App Control**.

## 4. Misma identidad: beta frente a stable (CONOCIMIENTO; verifica)
- Un *package flight* entrega a un grupo de probadores un paquete del **mismo** producto y de la **misma identidad**: no se instalan **a la vez**; en un PC hay una sola copia (la Store entrega la de mayor versión que le corresponde al usuario).
- Para que el probador reciba la beta: su cuenta Microsoft debe estar en el grupo del vuelo y la versión del paquete del vuelo debe ser **mayor** que la publicada (2.0.2.0 > 2.0.1.0).
- Volver a Stable: quitar a la cuenta del grupo del vuelo **no baja** de versión por sí solo (la Store no instala versiones menores). Lo seguro: **desinstalar y reinstalar desde la Store** (obtendrá Stable mientras no esté en el vuelo). Desinstalar un MSIX borra sus datos de aplicación virtualizados (ajustes en el almacén del paquete): exporta antes lo que quieras conservar. Cuando se publique un Stable ≥ 2.0.2.x, todos quedan en Stable.
- No se creó otra identidad MSIX.

## 5. Instrucciones (Partner Center) — NO ejecutadas
1. Partner Center › Hatsu Desktop › **Package flights** › *Create new flight* › nombre «Hatsu Desktop Beta» › grupo de probadores (cuentas Microsoft de quienes prueben) › *Create*.
2. Dentro del vuelo › **Packages** › subir `HatsuDesktop-2.0.2.0-store-unsigned.msix` **sin tocarlo** (artefacto de CI, ver §6) › comprobar que el hash coincide con el de §6 (`Get-FileHash … -Algorithm SHA256`).
3. *Notes for certification* (si lo pide): app de escritorio de plena confianza (`runFullTrust`) igual que la versión publicada; el cambio es un diagnóstico local de Bluetooth y llamadas, sin envío de datos; sin capacidades restringidas nuevas.
4. Enviar el vuelo a certificación (un envío del vuelo **no** toca la publicación estable).

## 6. Artefacto y hash
Se rellenan al terminar el CI del commit de la beta (sección «Resultado»).

## 7. Instalar la beta firmada (probador)
1. Tu cuenta Microsoft debe figurar en el grupo del vuelo.
2. Microsoft Store › Biblioteca › *Obtener actualizaciones* (puede tardar horas tras la certificación). Debe aparecer la versión **2.0.2.0** en *Hatsu Desktop › Configuración de la aplicación › Acerca de* o en Configuración › Aplicaciones.
3. Smart App Control puede seguir activado: es el paquete firmado por la Store.
4. Abre Hatsu › Ajustes › **Hatsu Connect** › activa el módulo › **Diagnóstico de llamadas** y sigue la secuencia indicada para iPhone/Android.

## 8. Reversión a Stable
Ver §4: quita la cuenta del grupo, desinstala Hatsu Desktop y vuelve a instalarlo desde la Store.

## 9. Limitaciones pendientes
- Nada de lo telefónico está probado con un teléfono real: la corrección «Llamada finalizada», qué reporta `PhoneCallManager` en una llamada, qué niega Windows al paquete de la Store, los servicios de Android.
- «Abrir informe» abre una **copia temporal** en el Bloc de notas (el informe real vive en la carpeta de datos del paquete, que MSIX virtualiza); no pude comprobar en un MSIX real que el Bloc de notas la abra: si falla, usa «Guardar una copia…».
- El interruptor «Observar» no informa de si el módulo lo recibió; la prueba real es ver las líneas `telefonía del sistema` en el informe siguiente.
- Contestar y hablar desde el PC sigue bloqueado por la capacidad restringida (docs/57).
