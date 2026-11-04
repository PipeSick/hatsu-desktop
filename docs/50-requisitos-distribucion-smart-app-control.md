# Hatsu Desktop — Requisitos de distribución con Smart App Control activado

Rama `feat/hatsu-phase3-installer-onboarding`. **No se genera ni se pide instalar ningún MSI nuevo hasta decidir la vía de firma** (el CI sigue construyendo artefactos de prueba en cada commit; no son candidatos a entrega y no deben instalarse). **No se implementa ningún mecanismo para eludir la política de integridad de código** (ver sección 8).

Cada afirmación lleva una etiqueta de confianza: **[MEDIDO]** observado en el PC físico o en el CI · **[MS]** documentación oficial de Microsoft, *según los resúmenes de búsqueda* (learn.microsoft.com no es accesible desde la sesión de trabajo: conviene releer las páginas citadas) · **[FOROS]** respuestas de la comunidad / terceros, no oficiales · **[INFERIDO]** deducción mía, no probada.

## 1. Resumen
1. Con Smart App Control (SAC) **aplicado**, Windows impide que `HatsuDesktop.exe` cargue su propio `HatsuDesktop.dll` en 2.0.1; por eso el proceso desaparece. **[MEDIDO]**
2. Ningún binario propio ni el MSI llevan firma digital. **[MEDIDO en CI: 26 de 26 binarios propios y el MSI, «NotSigned»]**
3. SAC permite un archivo si (a) el servicio de reputación en la nube lo predice seguro o, si no puede decidir, (b) tiene una firma válida que encadena a una raíz de confianza; si no, lo bloquea. **[MS]** Un archivo sin firma **depende solo de la reputación**, que es **por archivo** y por tanto **parte de cero en cada compilación**. **[MS/FOROS]**
4. Entre 2.0.0 y 2.0.1 **no cambió nada de empaquetado, procedencia ni firma**: cambió el **contenido** del host (otro hash). **[MEDIDO]** Lo más coherente es una decisión de reputación distinta para un archivo nuevo. **[INFERIDO]**
5. La única vía estable es **firmar todo lo propio con un certificado de una autoridad reconocida** y construir reputación con una identidad de firma constante. Eso **no garantiza** la aceptación (sección 6). Requiere decisiones del propietario (sección 9).

## 2. Evidencia concluyente del Windows físico
| Dato | Valor |
|---|---|
| Registro | `Microsoft-Windows-CodeIntegrity/Operational` |
| Evento **3077** | `HatsuDesktop.exe` intentó cargar `HatsuDesktop.dll`; Windows rechazó la carga (no cumplía los requisitos de firma o violaba la política de integridad de código) |
| Evento **3033** | `HatsuDesktop.dll` no cumplía el nivel de firma exigido |
| Ruta | `C:\Users\felip\AppData\Local\Programs\Hatsu Desktop\HatsuDesktop.dll` |
| Política | `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` |
| Repetición | varios intentos de ejecución |

**Qué es esa política [MS]:** la lista oficial de directivas «inbox» de App Control de Microsoft identifica `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` como **`VerifiedAndReputableDesktop`**, la directiva base que se activa en Windows 11 cuando **Smart App Control está activado (modo aplicado)**; su hermana `{1283ac0f-…}` (`VerifiedAndReputableDesktopEvaluation`) es la del **modo evaluación**, que solo audita. Es decir: **la característica que aplica la política es Smart App Control en modo aplicado**; ya no hay que buscar otra. Confirmación local, de solo lectura, en el PC: `citool.exe -lp` (lista las directivas activas) o comprobar que existe `C:\Windows\System32\CodeIntegrity\CiPolicies\Active\{0283ac0f-fff1-49ae-ada1-8a933130cad6}.cip`; el nuevo `Diagnose-Install.ps1` lo imprime.

**Significado de los eventos [MS]:** 3077 = una directiva *aplicada* bloqueó un archivo; 3033 = un archivo no cumplía el nivel de firma exigido y debe aparecer junto a 3077 cuando lo causa una directiva; 3076/3034 son sus equivalentes en modo auditoría/evaluación (no bloquean).

**Consecuencias:**
- Se bloquea la **DLL administrada principal del host** (`HatsuDesktop.exe` es el lanzador .NET y `HatsuDesktop.dll` es el programa). El proceso termina **antes de ejecutar una sola línea de código de Hatsu Desktop**: ni el asistente, ni la bandeja, ni `HatsuDesktop.exe --diagnose` pueden ejecutarse. **Nada dentro del producto puede detectar ni explicar este bloqueo**; la herramienta útil es `Diagnose-Install.ps1` (script de texto, solo lectura). **[INFERIDO, coherente con lo medido]**
- Esto **no tiene relación con el AppBar ni con el asistente** (ya corregidos y validados): son problemas independientes.
- El caso del Dock de la 2.0.0 (bloqueo y desbloqueo tras «Reintentar») y este son el mismo mecanismo con distinto resultado por archivo. **[INFERIDO]**

## 3. Cómo decide SAC y qué implica
**[MS, vía resumen]** SAC consulta primero el servicio de reputación en la nube (Intelligent Security Graph): una predicción *segura* permite el archivo; *maliciosa o no deseada* lo bloquea; si **no puede decidir**, comprueba la **firma**: válida → permite; sin firma o inválida → bloquea. La documentación oficial para desarrolladores indica además que **la reputación puede no estar disponible para binarios recién publicados y puede cambiar con el tiempo**, y que **firmar correctamente es la salvaguarda más fiable**; recomienda probar con SAC en **modo evaluación** (que evalúa la firma sin bloquear y registra 3076).
**[FOROS]** Respuestas de la comunidad (Microsoft Q&A) coinciden en: certificado emitido por una CA del **Programa de Raíces Confiables de Microsoft** (o Trusted Signing); firmar **todos** los `.exe` y `.dll` y también el instalador (MSI); un certificado **autofirmado no es equivalente**; **no hay excepción por aplicación**; **desactivar SAC es irreversible** (no se puede reactivar sin reiniciar/reinstalar Windows); cada compilación nueva sin firma «no tiene reputación».

**Implicaciones [INFERIDO a partir de lo anterior]:**
1. **Sin firma, cada versión es una lotería.** La reputación es por hash; una compilación nueva no la tiene. Unos archivos pasan por «predicción segura» y otros no; por eso en 2.0.0 el host pasó y en 2.0.1 no.
2. **Con firma válida**, el segundo criterio de SAC (si la nube no decide) se cumple para el binario firmado, **y la reputación se asocia a la identidad de firma**, no a cada hash. Es la única forma de que las versiones futuras no empiecen de cero. *Pero*: hay informes de binarios **firmados** que SAC bloqueó igualmente (reputación mala o cadena no confiable) → **firmar no garantiza el desbloqueo**.
3. Un instalador firmado no basta: **cada DLL cargada se evalúa** (aquí lo prueba el evento sobre una DLL).

## 4. 2.0.0 frente a 2.0.1 (procedencia, empaquetado, reputación)
| Aspecto | 2.0.0 (`d454c0c`, CI 41881545350) | 2.0.1 (`627f633`, CI 41921205653) |
|---|---|---|
| Canal de distribución | MSI de artefacto de CI, mismo flujo (WiX, por usuario, `%LOCALAPPDATA%\Programs\Hatsu Desktop`) | **idéntico** |
| Publicación de .NET | `dotnet publish -r win-x64 --self-contained` en la misma carpeta, sin ReadyToRun ni single-file | **idéntica** (ningún `.csproj` de módulos cambió) |
| Firma | ninguna | ninguna |
| Procedencia | artefacto de GitHub Actions sin firma ni atestación | igual |
| Contenido del host | — | +392 líneas en el host y su núcleo; **4 P/Invoke nuevos** (`EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor`, `SetErrorMode`); `Diagnose-Install.ps1` añadido al paquete; versión 2.0.0 → 2.0.1 |
| Reputación | **no medible** desde el repositorio ni el CI | **no medible** |
**Conclusión [MEDIDO + INFERIDO]:** no hay diferencia de empaquetado ni de procedencia que explique el bloqueo; hay un **hash distinto** para cada archivo cambiado. No se puede afirmar que los P/Invoke nuevos influyan en la decisión de la nube (no hay forma de saberlo); tampoco se descarta. No se comparó el binario 2.0.0 byte a byte porque el artefacto no es descargable desde la sesión y, además, dos compilaciones nunca coinciden en hash.

## 5. Requisitos concretos de una distribución que funcione con SAC aplicado
| # | Requisito | Confianza |
|---|---|---|
| R1 | **Firmar con Authenticode todos los binarios propios** (`HatsuDesktop*.exe/.dll`, `WinMac.*.dll`, `NotchWin*`) **y el MSI** | MS + FOROS |
| R2 | El certificado debe **encadenar a una raíz del Programa de Raíces Confiables de Microsoft** (CA pública o servicio de firma de Microsoft). **No** autofirmado, **no** CA privada | FOROS (coherente con MS) |
| R3 | **Sello de tiempo** (RFC 3161) en cada firma, para que siga válida al caducar el certificado | MS (EKU de firma de por vida) + práctica |
| R4 | **Identidad de firma constante** entre versiones (mismo certificado/cuenta/publicador) para que la reputación se acumule | INFERIDO (reputación por publicador) |
| R5 | **Ningún binario no firmado en la carga**: incluye bibliotecas de terceros que viajan en el paquete (sección 5.1) | MS (se evalúa cada DLL) |
| R6 | **No recompilar ni republicar por cambios triviales**; cada hash nuevo sin reputación es un riesgo, firmado o no | FOROS |
| R7 | Probar **antes de entregar** en un Windows 11 con SAC en **modo evaluación** (eventos 3076 = lo que bloquearía) y, cuando sea posible, en otro con SAC **aplicado** | MS |
| R8 | La verificación de firmas **bloquea la entrega** en CI (`Verify-Signatures.ps1 -Require`) | proyecto |

### 5.1 Bibliotecas de terceros sin firma de Microsoft
*(En elaboración: la lista nominal de los binarios ajenos sin firma de Microsoft sale del informe `auditoria-binarios.md` del CI de este commit y se incorpora aquí en cuanto termina esa ejecución.)* La cuestión de fondo ya se conoce: **SignPath Foundation no firma binarios de terceros** y firmarlos con nuestra identidad no es apropiado; si alguno de ellos carece de firma y de reputación conocida, hay que sustituirlo, pedir a su autor que firme o aceptar que SAC pueda bloquearlo.

## 6. Vías legítimas de firma y distribución (sin elegir todavía)
Hecho el filtro por R1–R6, **ninguna garantiza la aceptación por SAC** (la reputación y el estado del certificado siguen contando).
| Vía | Qué cubre | Condiciones / límites conocidos | Estado |
|---|---|---|---|
| **A. SignPath Foundation** (programa gratuito para código abierto) | Firma de binarios propios desde el CI, con certificado **de la Fundación** | Proyecto OSS con licencia OSI (Apache-2.0 ✔), **repositorio público**, sin componentes propietarios, equipo con MFA, verificación de origen (compilación trazable a commit y trabajo de CI), **solo binarios propios** (**no** las bibliotecas de terceros), el publicador que Windows muestra es **la Fundación**. Sin responsabilidad por daños | **Necesita decisión**: hacer público el repo y aceptar el publicador. *No se ha contactado a nadie* |
| **B. Servicio de firma de Microsoft** («Trusted/Artifact Signing») | Certificados de corta duración encadenados a Microsoft, integrados en CI | **Disponibilidad limitada en vista previa**: organizaciones de EE. UU., Canadá, UE y Reino Unido; personas físicas solo EE. UU. y Canadá (según foros de Microsoft; **verificar**). **Probablemente no disponible desde Chile** | **Descartada salvo cambio de elegibilidad** |
| **C. CA comercial (OV/EV) con clave en HSM/servicio de firma en la nube** | Certificado a nombre de la persona u organización | Validación de identidad, **clave obligatoriamente en hardware**, coste anual; EV mejora SmartScreen pero **no se ha demostrado** que mejore SAC | **Requiere autorización de costes** |
| **D. Microsoft Store / MSIX** | Las apps de la Store no pasan por la reputación de SAC | Cuenta de desarrollador y empaquetado MSIX; **virtualiza `AppData`** y restringe un programa que reserva el escritorio (descartado en la Fase 2) | Incompatible con la arquitectura actual |
| **E. Autofirmado / CA privada** | — | No encadena a una raíz de confianza; instalar esa raíz en el equipo del usuario es **manipular la protección** | **Descartada** |
| **F. Compilar desde fuente en cada equipo** | Un archivo creado localmente no tiene reputación; aplica lo mismo | Mueve el problema al usuario | Descartada para usuarios finales; válida para desarrollo |

## 7. Criterios de aceptación de un candidato a entrega
1. `Verify-Signatures.ps1 -Require` pasa: **todos** los binarios propios y el MSI con firma **Valid** y **sello de tiempo**.
2. Las bibliotecas de terceros sin firma están **justificadas** (reputación conocida) o sustituidas/firmadas por su autor.
3. En un Windows 11 con SAC en **modo evaluación**: **cero** eventos 3076 sobre archivos de Hatsu Desktop tras ejercitar todas las funciones (el producto carga DLL al usarlas).
4. En un Windows 11 con SAC **aplicado**: instalar, arrancar y usar cada módulo; `Diagnose-Install.ps1` sin eventos 3077/3033 sobre Hatsu Desktop.
5. El resultado queda documentado **por versión**; una versión no se anuncia como funcional con SAC sin esta evidencia.

## 8. Lo que NO se hará (elusión)
No se pedirá ni se recomendará: desactivar o poner en evaluación SAC, Defender o SmartScreen; añadir exclusiones o certificados a las raíces de confianza del usuario; renombrar o repaquetar para engañar a la reputación; envolver el código en un ejecutable «de confianza» ajeno; inyectar o cargar el código por otra vía para evitar la comprobación de carga de DLL; marcar archivos con atributos de reputación; ni usar certificados de otros. Tampoco se firmará con una identidad que no sea la del propietario real del proyecto.

## 9. Decisiones que necesita el propietario
1. ¿Se puede hacer **público** el repositorio (condición de la vía A) y se acepta que el publicador sea «SignPath Foundation»?
2. Si no: ¿se autoriza un **coste** (vía C: certificado OV/EV + servicio de firma con clave protegida)? Importe a confirmar con el proveedor; **no se asume**.
3. ¿Se acepta **detener las publicaciones sin firma** (R6) hasta resolverlo y dejar de ofrecer MSI de prueba a equipos con SAC?
4. ¿Hay disponible un Windows 11 limpio (máquina virtual) para la prueba en **modo evaluación** (R7)? Para el modo aplicado hace falta una instalación nueva.

## 10. Lo que se hizo en esta ronda
- `Diagnose-Install.ps1` **identifica la política** (`{0283ac0f…}` = SAC aplicado; `{1283ac0f…}` = evaluación) y **resume por archivo** los eventos 3033/3034/3076/3077 de los últimos 30 días (con veces, primera/última hora y política), lista las directivas activas con `citool.exe -lp` (solo lectura), indica si existen los archivos `.cip`, y enumera los binarios propios sin firma válida.
- `tools/ci/Audit-Binaries.ps1` lista **por nombre** los binarios ajenos sin firma de Microsoft.
- `tools/ci/Verify-Signatures.ps1`: informativa hoy; pasará a obligatoria (`-Require`) cuando exista certificado.
- Nada de esto cambia el comportamiento del producto ni intenta evitar la política.

## 11. Lo que no se sabe
- Por qué SAC aceptó el host 2.0.0 y rechazó el 2.0.1 (decisión de la nube; opaca).
- Si **todos** los archivos de Hatsu Desktop están bloqueados o solo algunos (el nuevo informe lo resume; pendiente de que lo ejecutes).
- Si una firma de la Fundación SignPath o de una CA comercial sería aceptada **desde la primera versión** o requeriría acumular reputación.
- Si las bibliotecas de terceros sin firma pasan por reputación conocida.

## Fuentes consultadas
- Microsoft Learn, *Inbox App Control policies* (identificación de `VerifiedAndReputableDesktop` y su variante de evaluación), vía [resultado de búsqueda](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/inbox-appcontrol-policies).
- Microsoft Learn, [*Test your app with Smart App Control*](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control) (modo evaluación, `citool.exe -lp`, eventos 3076/3077, reputación de binarios nuevos).
- Microsoft Learn, [*Understanding Application Control events*](https://learn.microsoft.com/windows/security/threat-protection/windows-defender-application-control/event-id-explanations) (3077, 3033/3034).
- Microsoft Support, [*What is Smart App Control?*](https://support.microsoft.com/en-us/topic/what-is-smart-app-control-285ea03d-fa88-4d56-882e-6698afdb7003) (orden de decisión: reputación y luego firma; irreversibilidad al desactivar).
- Microsoft Q&A (comunidad): [*Application signed and blocked by Smart App Control*](https://learn.microsoft.com/en-us/answers/questions/5791210/application-signed-and-blocked-by-smart-app-contro) y [*Smart App Control certificate authentication*](https://learn.microsoft.com/en-us/answers/questions/5819682/smart-app-control-certificate-authentication) **[FOROS]**.
- SignPath Foundation, [términos](https://signpath.org/terms) y [verificación de origen](https://docs.signpath.io/origin-verification) (requisitos del programa OSS; condiciones sujetas a cambio: confirmar en su sitio).
- Foros de Microsoft Q&A sobre elegibilidad de *Trusted/Artifact Signing* (individuos EE. UU./Canadá) **[FOROS, verificar]**.
- No se pudieron abrir: learn.microsoft.com (documentación oficial completa), textslashplain.com y blog.devgenius.io (hosts fuera de la lista de dominios permitidos de la sesión).
