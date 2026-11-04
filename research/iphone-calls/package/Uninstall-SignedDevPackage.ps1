<#
  P3 — DESHACE lo que instaló Install-SignedDevPackage.ps1, leyendo %LOCALAPPDATA%\CallProbeP3\<id>\state.json.
  Quita SOLO: el paquete de esa ejecución (si su identidad coincide exactamente), el certificado de esa ejecución (por HUELLA EXACTA, en TrustedPeople y en CurrentUser\My) y los archivos de su carpeta.
  Nunca borra certificados por Subject/nombre, ni paquetes ajenos (si hay una identidad inesperada, se detiene y lo explica), ni nada de otras ejecuciones. state.json se conserva como registro.
  Administrador y Windows PowerShell 5.1. Si hay varias ejecuciones pendientes hay que indicar -RunId.
  Antes de esto, si registraste el resultado de la prueba (stage3 register) sin --keep, ya está revertido; si usaste --keep, ejecuta antes:  .\Run-Probe.ps1 stage3 unregister
#>
param([string]$RunId = '', [string]$Base = [Environment]::GetFolderPath('LocalApplicationData'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Usa Windows PowerShell 5.1 (powershell.exe), no pwsh.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta esta consola como administrador.' }
. (Join-Path $PSScriptRoot 'P3.Common.ps1')
$r = Invoke-P3Uninstall -Base $Base -RunId $RunId -Ops (New-P3RealOps)
if ($null -eq $r) { return }
if ($r.Complete) { Write-Host 'P3 LIMPIO: paquete, certificado y archivos de esa ejecución eliminados (state.json se conserva).' }
else { Write-Host 'LIMPIEZA INCOMPLETA: lee los mensajes [P3] de arriba y el state.json. No se ha borrado nada ajeno.'; exit 4 }
