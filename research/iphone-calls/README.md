# Investigación: llamadas del iPhone desde Windows (rama `research/iphone-calls`)

Carpeta **aislada** de investigación. No forma parte de WinMac ni de NotchWin y no modifica ningún archivo existente.

- `INFORME.md` — informe técnico (auditoría de `DeniedBySystem`, vías, matriz, arquitectura, plan de validación, conclusión).
- `CallProbe/` — prueba de concepto para Windows (etapas 1–5). Solo lectura salvo `register`, `answer`, `reject`, `hangup` (cada una exige escribir una palabra exacta). **Nunca marca ni inicia llamadas.**
- `CallProbe.Logic/`, `CallProbe.Logic.Tests/` — lógica pura y pruebas (corren en Linux y en CI).
- `package/` — manifiesto y scripts de registro en modo desarrollador (sin certificados).
- `tools/ListWinRtApi/` — lista los tipos WinRT de un SDK leyendo sus metadatos.
- `evidence/` — listados de `Windows.ApplicationModel.Calls` en los SDK 19041 y 26100.

Estado: la prueba de concepto **compila** y su lógica pasa 20 pruebas; **no se ha ejecutado contra ningún iPhone**.

```powershell
dotnet test CallProbe.Logic.Tests
dotnet publish CallProbe -c Release -r win-x64 --self-contained -o publish
.\package\Run-Probe.ps1 stage1 -Unpackaged
.\package\Run-Probe.ps1 stage2 -Unpackaged
```

## Iteración 2 (bloqueo de RegisterApp)

Resultado real: `RegisterApp()` retorna sin excepción pero `IsRegistered()` sigue en False (ver INFORME §12). Nuevas herramientas de **solo lectura**:
`stage3 observe --seconds N [--interval MS]` (línea de tiempo de IsRegistered / AudioRoutingStatus / enlace Classic con eventos del transporte, presencia de API en la build y propiedades del transporte). `stage3 status` y `stage3 register` ahora imprimen la misma línea de tiempo.
P3 (paquete firmado): `package/Install-SignedDevPackage.ps1` / `Uninstall-SignedDevPackage.ps1` (lógica en `P3.Common.ps1`, pruebas simuladas en `Test-P3.ps1`). Modifican el almacén de confianza; procedimiento, riesgos y comandos en INFORME §13.
