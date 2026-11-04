<#
.SYNOPSIS
  Prueba visual REAL de los iconos de la bandeja en la barra superior de WinMac, en el runner de Windows.
.DESCRIPTION
  1. Arranca la barra real (WinMac.Bar.App, sin config: valores por defecto) con WINMAC_TRAY_DIAG=1, que anota en bar.log los iconos visibles de ESA barra.
  2. Captura la franja superior de la pantalla (barra-antes.png).
  3. Arranca tres procesos de prueba que ponen un icono cada uno con Shell_NotifyIcon (TrayIconProcess.ps1: Information, Warning, Error).
  4. Espera a que bar.log diga que la barra tiene los tres visibles (estado interno) y dónde está la flecha de las apps en segundo plano.
  5. Pulsa esa flecha con el ratón (como un usuario): se abre el desplegable con la cuadrícula de iconos.
  6. Captura otra vez (winmac-tray-icons-real.png) y compara con la de antes: busca, BAJO la barra (en el desplegable), los píxeles nuevos de los tres iconos
     (azul de Information, amarillo de Warning, rojo de Error) y dónde están.
  Las capturas son de la pantalla real (Graphics.CopyFromScreen), no del render fuera de pantalla. Nada de esto toca el código de producción.
#>
param([string]$PublishDir = 'ci-publish', [string]$OutDir = 'tray-capture')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path; $PublishDir = (Resolve-Path $PublishDir).Path
$bar = "$PublishDir/Bar.App/HatsuDesktop.Bar.exe"
$log = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'
$report = Join-Path $OutDir 'informe.txt'
function Say($s) { Write-Host $s; Add-Content -Path $report -Value $s -Encoding utf8 }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Say "Pantalla principal: $($screen.Width)x$($screen.Height) en ($($screen.X),$($screen.Y))"
$stripH = [Math]::Min(320, $screen.Height)   # la barra y el desplegable que se abre debajo
function Shot([string]$path) {
  $bmp = New-Object System.Drawing.Bitmap $screen.Width, $stripH
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($screen.X, $screen.Y, 0, 0, (New-Object System.Drawing.Size $screen.Width, $stripH), [System.Drawing.CopyPixelOperation]::SourceCopy)   # con DWM, la copia de pantalla ya trae lo compuesto
  $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

if (Test-Path $log) { Remove-Item $log -Force }
$env:WINMAC_TRAY_DIAG = '1'
$barProc = Start-Process -FilePath $bar -ArgumentList '--no-config' -PassThru
Say "Barra real arrancada (pid $($barProc.Id))"
Start-Sleep -Seconds 8
Shot (Join-Path $OutDir 'barra-antes.png')

$icons = @(
  @{ Kind = 'Information'; Text = 'WinMac prueba 1 (Information)' },
  @{ Kind = 'Warning'; Text = 'WinMac prueba 2 (Warning)' },
  @{ Kind = 'Error'; Text = 'WinMac prueba 3 (Error)' })
$procs = foreach ($i in $icons) {
  Start-Process powershell.exe -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSScriptRoot\TrayIconProcess.ps1`"", '-Text', "`"$($i.Text)`"", '-Kind', $i.Kind, '-Seconds', '120') -PassThru -WindowStyle Hidden
}
Say "Procesos de prueba con icono de bandeja: $(($procs | ForEach-Object { $_.Id }) -join ', ')"

# Estado interno: la propia barra anota qué iconos tiene visibles
$stateOk = $false; $lastLine = ''
for ($t = 0; $t -lt 40 -and -not $stateOk; $t++) {
  Start-Sleep -Milliseconds 750
  if (Test-Path $log) {
    $lines = Get-Content $log -Encoding utf8 | Where-Object { $_ -match '\[bandeja\] visibles' }
    if ($lines) { $lastLine = $lines[-1]; $stateOk = ($icons | Where-Object { $lastLine -notlike "*$($_.Text)*" }).Count -eq 0 }
  }
}
Say "Estado interno de la barra: $(if ($stateOk) { 'los tres iconos de prueba están visibles' } else { 'NO están los tres' })"
Say "  última línea: $lastLine"
if (Test-Path $log) { Get-Content $log -Encoding utf8 | Where-Object { $_ -match '\[bandeja\]' } | ForEach-Object { Say "  log: $_" } }

Start-Sleep -Seconds 2
# La flecha de las apps en segundo plano: la barra anota su posición en pantalla; se pulsa como lo haría un usuario
$btn = Get-Content $log -Encoding utf8 | Where-Object { $_ -match '\[bandeja\] botón: x=(\d+) y=(\d+)' } | Select-Object -Last 1
if (-not ($btn -match 'x=(\d+) y=(\d+)')) { Say 'La barra no indicó dónde está la flecha de las apps en segundo plano'; exit 1 }
$bx = [int]$Matches[1]; $by = [int]$Matches[2]; Say "Flecha de las apps en segundo plano en ($bx, $by): se pulsa"
Shot (Join-Path $OutDir 'barra-con-flecha.png')
Add-Type -Namespace WinMacCi -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
'@
[WinMacCi.Mouse]::SetCursorPos($bx, $by) | Out-Null; Start-Sleep -Milliseconds 300
[WinMacCi.Mouse]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [WinMacCi.Mouse]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
[WinMacCi.Mouse]::SetCursorPos($bx, $by + 260) | Out-Null   # el puntero fuera del desplegable (sin resalte encima de un icono)
Start-Sleep -Seconds 2   # que se abra y se dibuje
$shotPath = Join-Path $OutDir 'winmac-tray-icons-real.png'
Shot $shotPath
Say "Captura real: $shotPath"

# Verificación visual: píxeles nuevos de cada color en el desplegable, bajo la barra (y de 30 a 320 px a 100 %)
$before = [System.Drawing.Bitmap]::FromFile((Join-Path $OutDir 'barra-antes.png')); $after = [System.Drawing.Bitmap]::FromFile($shotPath)
$fromY = 30
$found = @{ Information = New-Object System.Collections.Generic.List[int]; Warning = New-Object System.Collections.Generic.List[int]; Error = New-Object System.Collections.Generic.List[int] }
for ($y = $fromY; $y -lt $after.Height; $y++) {
  for ($x = 0; $x -lt $after.Width; $x++) {
    $a = $after.GetPixel($x, $y); $b = $before.GetPixel($x, $y)
    if ([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B) -lt 60) { continue }   # sin cambio
    if ($a.B -gt 150 -and $a.R -lt 110 -and $a.G -lt 170) { $found.Information.Add($x) }
    elseif ($a.R -gt 190 -and $a.G -gt 150 -and $a.B -lt 90) { $found.Warning.Add($x) }
    elseif ($a.R -gt 170 -and $a.G -lt 90 -and $a.B -lt 90) { $found.Error.Add($x) }
  }
}
$before.Dispose(); $after.Dispose()
$visible = 0
foreach ($k in 'Information', 'Warning', 'Error') {
  $xs = $found[$k]
  if ($xs.Count -ge 12) { $visible++; Say ("  icono {0}: {1} píxeles nuevos en el desplegable, x {2}–{3}" -f $k, $xs.Count, ($xs | Measure-Object -Minimum).Minimum, ($xs | Measure-Object -Maximum).Maximum) }
  else { Say "  icono ${k}: NO se ve en el desplegable ($($xs.Count) píxeles)" }
}
Say "Iconos de prueba visibles en el desplegable: $visible de 3"

# Vista previa en el log (recorte alrededor de la flecha y el desplegable) para revisarla sin descargar el artefacto
$img = [System.Drawing.Bitmap]::FromFile($shotPath); $cw = [Math]::Min(420, $img.Width); $cx0 = [Math]::Max(0, [Math]::Min($img.Width - $cw, $bx - $screen.X - [int]($cw / 2)))
$ch = [Math]::Min(220, $img.Height); $rect = [System.Drawing.Rectangle]::new([int]$cx0, 0, [int]$cw, [int]$ch)
$crop = $img.Clone($rect, $img.PixelFormat); $img.Dispose()
$prev = Join-Path $OutDir 'preview-tray-right.png'; $crop.Save($prev, [System.Drawing.Imaging.ImageFormat]::Png); $crop.Dispose()
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($prev))
Write-Host "::group::PREVIEW_B64 tray-real bytes=$((Get-Item $prev).Length) b64len=$($b64.Length)"
for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host ("PVB64|" + $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i))) }
Write-Host "::endgroup::"

foreach ($p in $procs) { try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { } }
& $bar --quit | Out-Null; Start-Sleep -Seconds 2; try { Stop-Process -Id $barProc.Id -Force -ErrorAction SilentlyContinue } catch { }
if (-not $stateOk -or $visible -lt 3) { Say 'RESULTADO: la prueba NO se cumplió (ver arriba)'; exit 1 }
Say 'RESULTADO: los tres iconos de prueba están en el estado interno de la barra Y se ven en el desplegable de la captura real'
