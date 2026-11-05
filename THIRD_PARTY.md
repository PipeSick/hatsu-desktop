# Componentes de terceros

Hatsu Desktop (antes WinMac) se distribuye bajo **Apache-2.0** (ver `LICENSE`). Este archivo lista lo que se **distribuye con el producto**, lo que solo se usa para **compilar o probar**, y lo **previsto**. Las licencias se verificaron en los `.nuspec` de los paquetes restaurados (versión indicada); revisar de nuevo al actualizar cada dependencia.

## Se distribuye con Hatsu Desktop (Dock y barra superior)
| Componente | Versión | Licencia | Uso |
|---|---|---|---|
| Vortice.Direct2D1, Vortice.Direct3D11, Vortice.DirectComposition, Vortice.DXGI | 3.8.3 | MIT | Bindings .NET de DirectX (render) |
| Vortice.DirectX, Vortice.Mathematics | 3.8.3 / 2.1.0 | MIT | Dependencias transitivas de las anteriores |
| SharpGen.Runtime, SharpGen.Runtime.COM | 2.4.2-beta | MIT | Dependencias transitivas |
| System.Data.OleDb (Microsoft) | 9.0.0 | MIT | Spotlight: consultas al índice de Windows Search (proveedor del propio Windows) |
| Runtime de .NET (si se publica autocontenido) | 10.x | MIT (y licencias propias de componentes del runtime) | Ejecución |

## Solo compilación y pruebas (no se distribuyen)
| Componente | Versión | Licencia |
|---|---|---|
| xunit, xunit.core, xunit.assert, xunit.analyzers, xunit.runner.visualstudio | 2.9.2 / 1.16.0 / 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.*, Microsoft.CodeCoverage | 17.12.0 | MIT |

## Prototipos (`spikes/`, no se distribuyen)
| Componente | Versión | Licencia |
|---|---|---|
| Microsoft.WindowsAppSDK (prototipo S3, WinUI 3) | 2.6.4 | Términos de licencia de Microsoft (archivo `license.txt` del paquete), **no OSI** |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.* | Términos de licencia del SDK de Windows (herramientas de compilación) |

## Previsto (todavía NO incluido)
| Componente | Licencia | Condiciones a cumplir antes de distribuirlo |
|---|---|---|
| **NotchWin** (`PipeSick/notch-win`) | MIT, © «NotchWin contributors» | Conservar su `LICENSE` y su aviso. Sus autores declaran que reimplementa el comportamiento de Vorssaint (GPLv3) **sin copiar su código**: conservar esa declaración y no usar nombre ni recursos de Vorssaint |
| Microsoft.WindowsAppSDK (runtime de NotchWin, 1.6.x) | Términos de licencia de Microsoft | Según su `license.txt` (§3), los archivos que el paquete copia junto a la aplicación **se pueden redistribuir**, pero hay requisitos: la aplicación debe aportar funcionalidad principal propia, los usuarios finales deben aceptar términos que protejan a Microsoft al menos como esos, y hay una cláusula de indemnidad. **El instalador único deberá incluir un contrato de usuario final que lo cubra; revisar con asesoría legal antes de publicar** [V] |

## Recursos visuales y marcas
No se incluyen logotipos, iconos ni fuentes de Apple ni de terceros: los iconos de la barra y del Dock son geometrías propias dibujadas en código. «macOS», «Tahoe», «Dynamic Island» y «Vorssaint» pertenecen a sus respectivos titulares y solo se mencionan con fines descriptivos.
