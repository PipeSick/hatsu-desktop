<#
  Verificación técnica del ZIP de la beta (sin interfaz): se descomprime en una carpeta temporal y se comprueba que trae todo lo necesario para
  arrancar en un Windows 11 sin .NET instalado (módulos autocontenidos), que las sumas SHA-256 cuadran y que los ejecutables responden a una orden
  sin ventanas. Sale con 1 si algo falta.
#>
param([string]$Zip)
$ErrorActionPreference = 'Stop'; $fails = 0
function Check([string]$n, [bool]$ok, [string]$d = '') { if ($ok) { Write-Host "PASS  $n $d" } else { Write-Host "FAIL  $n $d"; $script:fails++ } }
$dir = Join-Path ([IO.Path]::GetTempPath()) ('winmac-zip-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
Expand-Archive -Path $Zip -DestinationPath $dir
$root = Join-Path $dir 'HatsuDesktop'
foreach ($f in 'HatsuDesktop.exe', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe', 'NotchWin\NotchWin.App.exe', 'Uninstall-HatsuDesktop.ps1', 'Restore-Taskbar.ps1', 'VERSION.txt', 'HatsuDesktop.ico', 'Migrate-FromWinMac.ps1', 'LICENSE', 'SHA256SUMS.txt') {
  Check "contiene $f" (Test-Path (Join-Path $root $f))
}
# Hatsu Desktop: identidad pública en los metadatos de los ejecutables y icono incrustado (los NOMBRES de archivo se conservan por compatibilidad)
foreach ($pair in @(@('HatsuDesktop.exe', 'Hatsu Desktop'), @('HatsuDesktop.Bar.exe', 'Hatsu Desktop — Barra superior'), @('HatsuDesktop.Dock.exe', 'Hatsu Desktop — Dock'), @('HatsuDesktop.Connect.exe', 'Hatsu Desktop — Connect'))) {
  $vi = (Get-Item (Join-Path $root $pair[0])).VersionInfo
  Check "metadatos de $($pair[0]): producto" ($vi.ProductName -eq 'Hatsu Desktop') $vi.ProductName
  Check "metadatos de $($pair[0]): descripción" ($vi.FileDescription -eq $pair[1]) $vi.FileDescription
}
Add-Type -AssemblyName System.Drawing
foreach ($e in 'HatsuDesktop.exe', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe') {
  $ico = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $root $e)); $bmp = $ico.ToBitmap(); $px = $bmp.GetPixel([int]($bmp.Width * 0.25), [int]($bmp.Height * 0.2))
  Check "icono propio incrustado en $e (violeta, no el genérico)" ($px.A -gt 200 -and $px.B -gt $px.G + 40 -and $px.B -gt 150) "RGB $($px.R),$($px.G),$($px.B)"
}
# Fase 2: ya no viajan los ejecutables con el nombre antiguo (evita lanzar por error una versión vieja)
foreach ($f in 'WinMac.exe', 'WinMac.Bar.App.exe', 'WinMac.Dock.App.exe', 'WinMac.Connect.App.exe') { Check "sin el ejecutable antiguo $f" (-not (Test-Path (Join-Path $root $f))) }
# Autocontenido: el runtime de .NET viaja en el paquete (el usuario no instala nada)
foreach ($f in 'hostfxr.dll', 'coreclr.dll') { Check "autocontenido: $f junto a HatsuDesktop.exe" (@(Get-ChildItem $root -Recurse -Filter $f).Count -gt 0) }
Check 'NotchWin trae el runtime de Windows App SDK' (@(Get-ChildItem (Join-Path $root 'NotchWin') -Filter 'Microsoft.WindowsAppRuntime*.dll').Count -gt 0)
Check 'NotchWin trae su recurso .pri' (@(Get-ChildItem (Join-Path $root 'NotchWin') -Filter '*.pri').Count -gt 0)
# Sumas
$bad = 0; foreach ($l in Get-Content (Join-Path $root 'SHA256SUMS.txt')) {
  if ($l -notmatch '^([0-9a-f]{64})  (.+)$') { continue }
  $p = Join-Path $root ($Matches[2].Replace('/', '\')); if (-not (Test-Path $p) -or (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) { $bad++ }
}
Check 'SHA256SUMS.txt coincide con los archivos' ($bad -eq 0) "($bad distintos)"
# Arranque sin ventanas: el host dibuja Ajustes fuera de pantalla (carga .NET, Direct2D y el código de Ajustes) y sale con 0
$png = Join-Path $dir 'ajustes.png'
$p = Start-Process -FilePath (Join-Path $root 'HatsuDesktop.exe') -ArgumentList @('--render-settings', 'general', '--out', "`"$png`"", '--scale', '1') -PassThru -Wait -WindowStyle Hidden
Check 'HatsuDesktop.exe del paquete arranca (orden sin ventanas) y sale con 0' ($p.ExitCode -eq 0 -and (Test-Path $png)) "(código $($p.ExitCode))"
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
if ($fails) { Write-Host "::error title=Paquete::$fails comprobación(es) del ZIP fallaron"; exit 1 }
Write-Host 'Paquete verificado.'; exit 0
