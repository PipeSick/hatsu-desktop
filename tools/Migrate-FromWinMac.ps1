<#
.SYNOPSIS
  Ayuda para pasar de WinMac a Hatsu Desktop sin perder nada. Por defecto SOLO MUESTRA lo que haría (simulación); no cambia nada hasta que se usa -Apply.
.DESCRIPTION
  Hatsu Desktop usa las MISMAS carpetas de datos que WinMac (%APPDATA%\WinMac y %LOCALAPPDATA%\WinMac): tus ajustes, anclas del Dock, orden de iconos, Peripherals y
  Cleanup se conservan solos. Este script cubre lo único que puede quedar apuntando a la versión antigua:
    1. Una instancia de WinMac en ejecución: con -Apply se le pide el cierre ORDENADO (nunca se mata un proceso).
    2. El inicio automático (HKCU\...\Run, valor «WinMac»): con -Apply y SOLO SI YA ESTABA ACTIVADO, pasa a esta instalación (conserva sus argumentos). Nunca lo crea.
    3. Con -RemoveLegacyFiles (y -Apply): borra de ESTA carpeta únicamente los ejecutables y archivos con el nombre antiguo (WinMac.exe, WinMac.Bar.App.*, etc.) que quedaron al extraer
       encima de una instalación anterior. No toca otras carpetas, tus datos ni la instalación antigua de otra ubicación.
  No necesita administrador. No se borra ninguna carpeta.
.PARAMETER Apply
  Ejecuta los cambios (sin él, solo se muestran).
.PARAMETER RemoveLegacyFiles
  Con -Apply: quita de esta carpeta los archivos antiguos con nombre WinMac*.
#>
param([switch]$Apply, [switch]$RemoveLegacyFiles)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$newExe = Join-Path $here 'HatsuDesktop.exe'
if (-not (Test-Path $newExe)) { throw "Ejecuta este script desde la carpeta de Hatsu Desktop (no se encuentra HatsuDesktop.exe en $here)." }
$mode = if ($Apply) { 'APLICANDO' } else { 'SIMULACIÓN (no se cambia nada; usa -Apply para aplicar)' }
Write-Host "Hatsu Desktop — migración desde WinMac · $mode"; Write-Host ''

# 1. Instancias en ejecución
$running = @(Get-Process -Name 'WinMac', 'HatsuDesktop' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) { Write-Host ("Hay {0} instancia(s) en ejecución: {1}" -f $running.Count, (($running | ForEach-Object { $_.ProcessName }) -join ', ')) } else { Write-Host 'No hay ninguna instancia en ejecución.' }
if ($Apply -and $running.Count -gt 0) {
  Write-Host 'Pidiendo el cierre ordenado (HatsuDesktop.exe --quit; usa los mismos nombres de cierre que WinMac)…'
  & $newExe --quit | Out-Null
  $deadline = (Get-Date).AddSeconds(25)
  while ((Get-Date) -lt $deadline -and @(Get-Process -Name 'WinMac', 'HatsuDesktop' -ErrorAction SilentlyContinue).Count -gt 0) { Start-Sleep -Milliseconds 500 }
  $left = @(Get-Process -Name 'WinMac', 'HatsuDesktop' -ErrorAction SilentlyContinue)
  if ($left.Count -gt 0) { Write-Warning 'La instancia no se cerró sola: ciérrala desde el icono de la bandeja («Salir») y vuelve a ejecutar. No se fuerza el cierre.' } else { Write-Host 'Cerrada.' }
}

# 2. Inicio automático
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$val = (Get-ItemProperty -Path $run -Name 'WinMac' -ErrorAction SilentlyContinue).WinMac
if (-not $val) { Write-Host 'Inicio automático: desactivado. No se cambia (actívalo en Ajustes › General si lo quieres).' }
else {
  Write-Host "Inicio automático actual: $val"
  if ($Apply) { & $newExe --adopt-autostart } else { Write-Host "  → con -Apply pasaría a: ""$newExe"" (se conservan sus argumentos)" }
}

# 3. Archivos antiguos en ESTA carpeta
$legacy = @('WinMac.exe', 'WinMac.dll', 'WinMac.pdb', 'WinMac.deps.json', 'WinMac.runtimeconfig.json', 'WinMac.Bar.App.exe', 'WinMac.Bar.App.dll', 'WinMac.Bar.App.pdb', 'WinMac.Bar.App.deps.json', 'WinMac.Bar.App.runtimeconfig.json',
  'WinMac.Dock.App.exe', 'WinMac.Dock.App.dll', 'WinMac.Dock.App.pdb', 'WinMac.Dock.App.deps.json', 'WinMac.Dock.App.runtimeconfig.json', 'WinMac.Connect.App.exe', 'WinMac.Connect.App.dll', 'WinMac.Connect.App.pdb', 'WinMac.Connect.App.deps.json', 'WinMac.Connect.App.runtimeconfig.json') |
  Where-Object { Test-Path (Join-Path $here $_) }
if ($legacy.Count -eq 0) { Write-Host 'Archivos con el nombre antiguo en esta carpeta: ninguno.' }
else {
  Write-Host ('Archivos con el nombre antiguo en esta carpeta ({0}): {1}' -f $legacy.Count, ($legacy -join ', '))
  if ($Apply -and $RemoveLegacyFiles) { foreach ($f in $legacy) { Remove-Item -LiteralPath (Join-Path $here $f) -Force }; Write-Host 'Quitados.' }
  elseif ($Apply) { Write-Host '  (se conservan; usa -RemoveLegacyFiles si quieres quitarlos)' }
}
Write-Host ''; Write-Host 'Tus ajustes y datos (%APPDATA%\WinMac, %LOCALAPPDATA%\WinMac) no se tocan. Ninguna carpeta se borra.'
if ($Apply) { Write-Host 'Para empezar: ejecuta HatsuDesktop.exe.' }
