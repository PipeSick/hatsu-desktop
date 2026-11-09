# 32 · Rendimiento (medición) y distribución

## Medición antes de optimizar
La auditoría visual mide en el runner de Windows del CI, en reposo y tras usar todas las funciones: CPU media de cada proceso (% de un núcleo), memoria de trabajo y
privada, e hilos (`Measure-Idle` en `tools/ci/Visual-Audit.ps1`; tablas en el informe). El runner no tiene GPU (Direct2D en WARP/software) ni un monitor físico: los
valores son orientativos y **no** sustituyen una medición en un PC real. No se ha optimizado nada sin un dato que lo justifique; ver los resultados en el informe del CI.

## Distribución (sin publicar nada)
- `tools/Build-Local.ps1` ⇒ `dist\WinMac` (host, Barra, Dock, NotchWin, extensiones de demostración).
- `tools/Package-WinMac.ps1` ⇒ `WinMac-<versión>-<commit>-win-x64.zip` + `.sha256`, con `VERSION.txt`, `LICENSE`, `NOTICE`, `THIRD_PARTY.md`,
  `Uninstall-WinMac.ps1` y `SHA256SUMS.txt`. Fechas fijadas a la del commit ⇒ el mismo commit da el mismo zip (el CI lo comprueba empaquetando dos veces).
- Versión: `src/Directory.Build.props` (`2.0.0`). Compilación determinista (`Deterministic`, `ContinuousIntegrationBuild` en el CI).
- **Inicio automático**: opcional (Ajustes › General), una sola clave `HKCU\…\Run\WinMac`, reversible.
- **Desinstalación limpia**: `Uninstall-WinMac.ps1` cierra WinMac, quita el inicio automático y, solo si se pide (`-RemoveData`, `-RemoveFolder`), borra sus datos y su
  carpeta. Nunca toca capturas, grabaciones ni otros archivos del usuario.
- **Sin certificado comercial**: el paquete no va firmado; SmartScreen puede avisar la primera vez. No se desactiva ninguna protección de Windows.
- El CI sube el zip como **artefacto temporal** (14 días). **No se crea ninguna versión pública**: eso requiere la autorización expresa del usuario.
- Licencias: WinMac y NotchWin son software libre; dependencias en `THIRD_PARTY.md` (todas gratuitas y de código abierto; ningún servicio de pago).
