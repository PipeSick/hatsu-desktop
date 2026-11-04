<#
.SYNOPSIS
  Desinstala Hatsu Desktop (antes WinMac) de forma limpia (docs/32). Sin administrador; solo toca lo que WinMac creó.
.DESCRIPTION
  1. Cierra WinMac (WinMac.exe --quit) y espera a sus módulos.
  1b. Si la barra de tareas de Windows quedó oculta por WinMac (WM-034), la restaura.
  2. Quita el inicio automático (el valor «WinMac» de HKCU\...\Run), si existe.
  3. Con -RemoveData, borra también los datos y ajustes de WinMac (%APPDATA%\WinMac y %LOCALAPPDATA%\WinMac). Por defecto se conservan.
  4. Con -RemoveFolder, borra la carpeta del programa (la que contiene este script o -InstallDir).
  Nunca toca capturas, grabaciones ni otros archivos tuyos (Imágenes, Vídeos), ni NotchWin si lo usas por separado.
.EXAMPLE
  pwsh Uninstall-HatsuDesktop.ps1         # deja tus ajustes por si vuelves a instalar
  pwsh Uninstall-HatsuDesktop.ps1 -RemoveData -RemoveFolder
#>
param([string]$InstallDir = $PSScriptRoot, [switch]$RemoveData, [switch]$RemoveFolder)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDir 'WinMac.exe'
if (Test-Path $exe) { Write-Host 'Cerrando Hatsu Desktop…'; & $exe --quit | Out-Null; Start-Sleep -Seconds 2 }
# WM-034: si la barra de tareas de Windows quedó oculta, vuelve (y su «Ocultar automáticamente», como estaba). Sin registro de WinMac no cambia nada.
if (Test-Path (Join-Path $env:LOCALAPPDATA 'WinMac\taskbar-restore.json')) {
  $rt = Join-Path $InstallDir 'Restore-Taskbar.ps1'
  if (Test-Path $exe) { Start-Process -FilePath $exe -ArgumentList '--restore-taskbar', '--quiet' -Wait -WindowStyle Hidden } elseif (Test-Path $rt) { & $rt }
  Write-Host 'Barra de tareas de Windows restaurada.'
}
$left = Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'WinMac.Connect.App', 'NotchWin.App', 'WinMac.Ext.Demos' -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and $_.Path.StartsWith((Resolve-Path $InstallDir).Path, [StringComparison]::OrdinalIgnoreCase) }
if ($left) { Write-Host "Esperando a $($left.Count) proceso(s)…"; $left | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue }

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -Path $run -Name 'WinMac' -ErrorAction SilentlyContinue)) { Remove-ItemProperty -Path $run -Name 'WinMac'; Write-Host 'Inicio automático quitado.' }

if ($RemoveData) {
  foreach ($d in (Join-Path $env:APPDATA 'WinMac'), (Join-Path $env:LOCALAPPDATA 'WinMac')) { if (Test-Path $d) { Remove-Item -Recurse -Force $d; Write-Host "Borrado: $d" } }
} else { Write-Host 'Ajustes y datos conservados (usa -RemoveData para borrarlos).' }

if ($RemoveFolder) {
  if (-not (Test-Path (Join-Path $InstallDir 'WinMac.exe'))) { throw "No parece una carpeta de Hatsu Desktop: $InstallDir" }
  Write-Host "Borrando la carpeta del programa: $InstallDir"
  Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "timeout /t 2 >nul & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden   # después de que este script termine
}
Write-Host 'Hatsu Desktop desinstalado.'
