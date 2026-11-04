<#
  Humo NO interactivo en el runner (sin monitor/GPU reales). Cada paso es informativo y deja evidencia en $OutDir.
  - Prototipos en --quick: detectan caídas al arrancar (p. ej. fallo de D3D11/DirectComposition en un runner sin GPU).
  - Dock.App --render-png: renderizado FUERA de pantalla (Direct2D sobre WARP) -> PNG descargable. Valida que el dibujo corre
    y permite ver el resultado, pero NO valida composición en ventana, blur sobre el escritorio real ni rendimiento.
#>
param([string]$PublishDir = 'ci-publish', [string]$OutDir = 'smoke')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
# Rutas ABSOLUTAS: los ejecutables se lanzan con su propio directorio de trabajo.
$OutDir = (Resolve-Path $OutDir).Path; $PublishDir = (Resolve-Path $PublishDir).Path
$rows = @()
function Run($label, $exe, $argList, $timeoutSec, [switch]$Informational) {
  if (-not (Test-Path $exe)) { Write-Host "OMITIDO $label (no existe $exe)"; $script:rows += [pscustomobject]@{ Paso = $label; Resultado = 'omitido'; Detalle = 'no existe en este commit' }; return }
  $so = (Join-Path $OutDir ($label -replace '[^\w\.-]', '_')) + '.txt'
  try {
    $p = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory (Split-Path $exe) -PassThru -RedirectStandardOutput $so -RedirectStandardError "$so.err" -ErrorAction Stop
    if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { }; $res = 'TIEMPO AGOTADO' }
    elseif ($p.ExitCode -eq 0) { $res = 'OK' } else { $res = "salió con $($p.ExitCode)" }
  } catch { $res = "no se pudo ejecutar: $($_.Exception.Message)" }
  if ($Informational -and $res -ne 'OK') { $res = "$res (informativo)" }
  $det = (Get-Content $so, "$so.err" -ErrorAction SilentlyContinue | Select-Object -Last 4) -join ' | '
  Write-Host "$label -> $res  $det"
  $script:rows += [pscustomobject]@{ Paso = $label; Resultado = $res; Detalle = $det }
}
$q = @('--quick', '--out', "`"$OutDir`"")
Run 'S2 WPF layered --quick' "$PublishDir/S2.DockWpf/S2.DockWpf.exe" (@('--variant', 'layered') + $q) 90 -Informational
Run 'S4 DComp glass --quick' "$PublishDir/S4.DockDComp/S4.DockDComp.exe" (@('--variant', 'glass') + $q) 90 -Informational
Run 'S3 WinUI acrylic --quick' "$PublishDir/S3.DockWinUI/S3.DockWinUI.exe" (@('--variant', 'acrylic') + $q) 90 -Informational
$dock = "$PublishDir/Dock.App/HatsuDesktop.Dock.exe"
# Escenas con el renderizador REAL (mismo DockController + DockPainter + GlassBackdrop que el dock en pantalla), a 2x para poder revisar detalles.
foreach ($theme in 'light', 'dark') {
  foreach ($pose in 'rest', 'hover', 'open', 'edge', 'hidden', 'preview') {
    Run "Dock offscreen $theme/$pose" $dock @('--render-png', "`"$OutDir\dock-$theme-$pose.png`"", '--pose', $pose, '--theme', $theme, '--scale', '2') 90
  }
}
# Tamaño y posición del Dock (mismo renderizador; proporciones escaladas, barra pegada a un lado)
Run 'Dock offscreen tamaño pequeño (44)' $dock @('--render-png', "`"$OutDir\dock-light-size44.png`"", '--pose', 'hover', '--theme', 'light', '--scale', '2', '--size', '44') 90
Run 'Dock offscreen tamaño grande (96)' $dock @('--render-png', "`"$OutDir\dock-light-size96.png`"", '--pose', 'hover', '--theme', 'light', '--scale', '2', '--size', '96') 90
Run 'Dock offscreen alineado a la derecha' $dock @('--render-png', "`"$OutDir\dock-light-right.png`"", '--pose', 'rest', '--theme', 'light', '--scale', '2', '--align', 'right') 90
Run 'Dock offscreen tint (sin fondo)' $dock @('--render-png', "`"$OutDir\dock-light-tint-hover.png`"", '--pose', 'hover', '--tier', 'tint', '--scale', '2') 90
Run 'Dock bench offscreen (WARP; NO es FPS real)' $dock @('--bench-offscreen', "`"$OutDir\bench-offscreen-light.json`"", '--theme', 'light') 180
Run 'Miniaturas de ventanas reales (--thumb-test, informativo)' $dock @('--thumb-test', '--out', "`"$OutDir\thumbs`"") 90 -Informational
Run 'Dock selftest' $dock @('--selftest', '--out', "`"$OutDir\selftest`"") 240
# Iconos REALES con el servicio de iconos (IShellItemImageFactory) de aplicaciones presentes en el runner. Informativo: el runner no es un escritorio de usuario.
foreach ($app in @('%WINDIR%\explorer.exe', '%WINDIR%\notepad.exe', '%WINDIR%\System32\calc.exe', '%WINDIR%\System32\mspaint.exe')) {
  $exp = [Environment]::ExpandEnvironmentVariables($app); $leaf = [IO.Path]::GetFileNameWithoutExtension($exp)
  Run "Icono real $leaf" $dock @('--icon-test', "`"$exp`"", '--out', "`"$OutDir\icon-$leaf.png`"") 60 -Informational
}
# Barra superior: PNG fuera de pantalla (datos ficticios), lecturas reales del sistema (informativo) y selftest. El AppBar solo se registra en --selftest-appbar (efímero en el runner).
$bar = "$PublishDir/Bar.App/HatsuDesktop.Bar.exe"
foreach ($theme in 'light', 'dark') { foreach ($bg in 'glass', 'transparent') {
  Run "Barra offscreen $theme/$bg" $bar @('--render-png', "`"$OutDir\bar-$theme-$bg.png`"", '--theme', $theme, '--bg', $bg, '--scale', '2') 90
} }
foreach ($tab in 'sound', 'system', 'network', 'disks') { Run "Panel multifunción $tab (datos de muestra, fuera de pantalla)" $bar @('--render-panel', 'hub', '--tab', $tab, '--out', "`"$OutDir\panel-hub-$tab.png`"", '--scale', '2') 90 }
Run 'Panel del Tiempo (datos de muestra, fuera de pantalla)' $bar @('--render-panel', 'weather', '--out', "`"$OutDir\panel-weather.png`"", '--scale', '2') 90
# Pantalla de 1080p a 100 % (≈1016 DIP libres bajo la barra) y a 150 % (≈672 DIP): el contenido se desplaza y las pestañas y el pie siguen visibles. Arriba y abajo del recorrido.
Run 'Panel Discos con altura limitada a 720 DIP, arriba (desplazable)' $bar @('--render-panel', 'hub', '--tab', 'disks', '--max-height', '720', '--scroll', '0', '--out', "`"$OutDir\panel-hub-disks-scroll-arriba.png`"", '--scale', '1') 90
Run 'Panel Discos con altura limitada a 720 DIP, abajo (desplazable)' $bar @('--render-panel', 'hub', '--tab', 'disks', '--max-height', '720', '--scroll', '9999', '--out', "`"$OutDir\panel-hub-disks-scroll-abajo.png`"", '--scale', '1') 90
Run 'Panel Sistema a 150 % de DPI en 1080p (centro)' $bar @('--render-panel', 'hub', '--tab', 'system', '--max-height', '672', '--scroll', '120', '--out', "`"$OutDir\panel-hub-system-150dpi-1080p.png`"", '--scale', '1.5') 90
# Control de las franjas verticales: con --bands se dibujan las dos bandas del fondo sintético de contraste (las que antes aparecían); los PNG normales no las llevan.
Run 'Panel Sistema con las franjas del fondo de prueba (control)' $bar @('--render-panel', 'hub', '--tab', 'system', '--bands', '--out', "`"$OutDir\panel-hub-system-franjas-control.png`"", '--scale', '2') 90
# Centro de Notificaciones (datos de MUESTRA, no notificaciones reales): oscuro y claro, el estado antes de activarlo y una pantalla de 1080p a 150 % (≈672 DIP: el centro se desplaza).
foreach ($theme in 'dark', 'light') { Run "Centro de Notificaciones $theme (datos de muestra, fuera de pantalla)" $bar @('--render-panel', 'notifications', '--theme', $theme, '--out', "`"$OutDir\panel-notifications-$theme.png`"", '--scale', '2') 90 }
Run 'Centro de Notificaciones sin activar (fuera de pantalla)' $bar @('--render-panel', 'notifications-off', '--out', "`"$OutDir\panel-notifications-off.png`"", '--scale', '2') 90
Run 'Centro de Notificaciones a 150 % de DPI en 1080p (desplazable)' $bar @('--render-panel', 'notifications', '--max-height', '672', '--out', "`"$OutDir\panel-notifications-150dpi-1080p.png`"", '--scale', '1.5') 90
# Ajustes de WinMac (ventana estilo iOS): se dibuja FUERA de pantalla con el pintor real. Valida el dibujo, NO el ratón ni el foco.
$hostExe = "$PublishDir/Host/HatsuDesktop.exe"
function RunSettings($label, $png, $extra) {
  $out = Join-Path $OutDir $png
  Run $label $hostExe (@('--render-settings') + $extra + @('--out', "`"$out`"", '--scale', '2')) 90
  if (Test-Path $hostExe) {
    $ok = (Test-Path $out) -and ((Get-Item $out).Length -gt 8000)
    if (-not $ok) { Write-Host "FALTA O VACÍO: $png"; $script:rows += [pscustomobject]@{ Paso = "$label (PNG)"; Resultado = 'FALLO'; Detalle = 'el PNG no se generó o está vacío' } }
    else { $det = (Get-Content "$out.txt" -ErrorAction SilentlyContinue | Select-Object -First 1); Write-Host "  $png $((Get-Item $out).Length) bytes  $det" }
  }
}
RunSettings 'Ajustes general (oscuro)' 'ajustes-general.png' @('general')
RunSettings 'Ajustes barra superior (oscuro)' 'ajustes-bar.png' @('bar')
RunSettings 'Ajustes clima y ciudades (oscuro)' 'ajustes-clima.png' @('weather')
RunSettings 'Ajustes escritorio y Dock (oscuro)' 'ajustes-dock.png' @('dock')
RunSettings 'Ajustes capturas y grabación (oscuro)' 'ajustes-capturas.png' @('capture')
RunSettings 'Ajustes Spotlight y Launchpad (oscuro)' 'ajustes-spotlight.png' @('spot')
RunSettings 'Ajustes portapapeles (oscuro)' 'ajustes-portapapeles.png' @('clip')
RunSettings 'Ajustes notificaciones (oscuro)' 'ajustes-notificaciones.png' @('nc')
RunSettings 'Ajustes isla (oscuro)' 'ajustes-notch.png' @('notch')
RunSettings 'Ajustes clima con sugerencias de ciudad' 'ajustes-clima-sugerencias.png' @('weather', '--suggest')
RunSettings 'Ajustes campo de ciudad en edición (texto, selección y cursor)' 'ajustes-clima-edicion.png' @('weather', '--edit')
RunSettings 'Ajustes clima y ciudades (claro)' 'ajustes-clima-claro.png' @('weather', '--theme', 'light')
RunSettings 'Ajustes barra superior desplazada (claro)' 'ajustes-bar-desplazada-claro.png' @('bar', '--theme', 'light', '--scroll', '400')
RunSettings 'Ajustes isla sin NotchWin instalada' 'ajustes-notch-sin-isla.png' @('notch', '--no-notch')
Run 'Barra offscreen light/glass con resalte' $bar @('--render-png', "`"$OutDir\bar-light-glass-hover.png`"", '--theme', 'light', '--bg', 'glass', '--scale', '2', '--hover', '4') 90
Run 'Barra --status (informativo)' $bar @('--status') 60 -Informational
Run 'Barra selftest' $bar @('--selftest') 120
Run 'Barra selftest-system (informativo: el runner no tiene Wi-Fi, Bluetooth ni brillo)' $bar @('--selftest-system') 120 -Informational
Run 'Barra selftest-monitors (informativo: monitores y barras previstas)' $bar @('--selftest-monitors') 60 -Informational
Run 'Barra selftest-appbar (informativo)' $bar @('--selftest-appbar') 120 -Informational
# Host (HatsuDesktop.exe) con módulos de mentira: instancia única, cierre ordenado, host muerto → hijos muertos, caídas, inicio automático, puertos. Es GATING (falla el job si algo falla).
$hostOut = Join-Path $OutDir 'host-tests.txt'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\ci\Test-Host.ps1') -PublishDir $PublishDir -OutDir $OutDir *>&1 | Tee-Object -FilePath $hostOut | ForEach-Object { Write-Host $_ }
$hostRes = if ($LASTEXITCODE -eq 0) { 'OK' } else { "salió con $LASTEXITCODE" }
$rows += [pscustomobject]@{ Paso = 'Host: pruebas de ciclo de vida (Test-Host.ps1)'; Resultado = $hostRes; Detalle = ((Get-Content $hostOut -ErrorAction SilentlyContinue | Select-Object -Last 3) -join ' | ') }
# Vista previa en el log: el sandbox de desarrollo no puede descargar artefactos, pero SÍ leer los logs del job.
# Se emiten versiones reducidas (escala 0.75) de dos escenas en base64 dentro de grupos plegados. Los PNG completos (2x) están en el artefacto.
foreach ($pv in @(@{ n = 'light-hover'; t = 'light'; p = 'hover' }, @{ n = 'dark-open'; t = 'dark'; p = 'open' }, @{ n = 'light-preview'; t = 'light'; p = 'preview' }, @{ n = 'dark-preview'; t = 'dark'; p = 'preview' }, @{ n = 'light-size44'; t = 'light'; p = 'hover'; x = @('--size', '44') }, @{ n = 'light-size96'; t = 'light'; p = 'hover'; x = @('--size', '96') }, @{ n = 'light-right'; t = 'light'; p = 'rest'; x = @('--align', 'right') })) {
  $f = Join-Path $OutDir "preview-$($pv.n).png"
  Run "Vista previa $($pv.n)" $dock (@('--render-png', "`"$f`"", '--pose', $pv.p, '--theme', $pv.t, '--scale', '0.75') + @($pv.x)) 90
  if (Test-Path $f) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
    Write-Host "::group::PREVIEW_B64 $($pv.n) bytes=$((Get-Item $f).Length) b64len=$($b64.Length)"
    for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host ("PVB64|" + $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i))) }
    Write-Host "::endgroup::"
  }
}
foreach ($pv in @(@{ n = 'bar-light-glass'; t = 'light'; b = 'glass' }, @{ n = 'bar-dark-glass'; t = 'dark'; b = 'glass' }, @{ n = 'bar-dark-transparent'; t = 'dark'; b = 'transparent' })) {
  $f = Join-Path $OutDir "preview-$($pv.n).png"
  Run "Vista previa $($pv.n)" $bar @('--render-png', "`"$f`"", '--theme', $pv.t, '--bg', $pv.b, '--scale', '1') 90
  if (Test-Path $f) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
    Write-Host "::group::PREVIEW_B64 $($pv.n) bytes=$((Get-Item $f).Length) b64len=$($b64.Length)"
    for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host ("PVB64|" + $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i))) }
    Write-Host "::endgroup::"
  }
}
# Centro de Notificaciones en el log (escala 1, datos de MUESTRA): permite revisar el dibujo desde el sandbox sin descargar artefactos.
foreach ($pv in @(@{ n = 'apple-dark'; k = 'apple'; t = 'dark' }, @{ n = 'apple-light'; k = 'apple'; t = 'light' }, @{ n = 'battery-dark'; k = 'battery'; t = 'dark' }, @{ n = 'battery-partial'; k = 'battery-partial'; t = 'dark' }, @{ n = 'nc-dark'; k = 'notifications'; t = 'dark' }, @{ n = 'nc-light'; k = 'notifications'; t = 'light' }, @{ n = 'nc-off'; k = 'notifications-off'; t = 'dark' })) {
  $f = Join-Path $OutDir "preview-$($pv.n).png"
  Run "Vista previa $($pv.n)" $bar @('--render-panel', $pv.k, '--theme', $pv.t, '--out', "`"$f`"", '--scale', '1') 90
  if (Test-Path $f) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
    Write-Host "::group::PREVIEW_B64 $($pv.n) bytes=$((Get-Item $f).Length) b64len=$($b64.Length)"
    for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host ("PVB64|" + $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i))) }
    Write-Host "::endgroup::"
  }
}
# Hoja de contacto: compone los PNG REALES del renderizador (no genera imágenes propias) para revisarlos de un vistazo.
try {
  Add-Type -AssemblyName System.Drawing
  $poses = 'rest', 'hover', 'open', 'edge', 'hidden', 'preview'; $themes = 'light', 'dark'; $cellW = 1200; $labelH = 26
  $imgs = @{}; $cellH = 0
  foreach ($t in $themes) { foreach ($p in $poses) { $f = Join-Path $OutDir "dock-$t-$p.png"; if (Test-Path $f) { $im = [System.Drawing.Image]::FromFile($f); $imgs["$t/$p"] = $im; $h = [int]($im.Height * $cellW / $im.Width); if ($h -gt $cellH) { $cellH = $h } } } }
  if ($imgs.Count -gt 0) {
    $sheet = New-Object System.Drawing.Bitmap ($cellW * 2), (($cellH + $labelH) * $poses.Count)
    $g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(30, 30, 34)); $g.InterpolationMode = 'HighQualityBicubic'
    $font = New-Object System.Drawing.Font 'Segoe UI', 11; $brush = [System.Drawing.Brushes]::White
    for ($r = 0; $r -lt $poses.Count; $r++) { for ($c = 0; $c -lt 2; $c++) {
      $k = "$($themes[$c])/$($poses[$r])"; $x = $c * $cellW; $y = $r * ($cellH + $labelH)
      $g.DrawString("$k  (renderizador real, WARP, fondo procedural)", $font, $brush, $x + 6, $y + 4)
      if ($imgs.ContainsKey($k)) { $im = $imgs[$k]; $g.DrawImage($im, $x, $y + $labelH, $cellW, [int]($im.Height * $cellW / $im.Width)) }
    } }
    $g.Dispose(); $sheet.Save((Join-Path $OutDir 'contact-sheet.png'), [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
    foreach ($im in $imgs.Values) { $im.Dispose() }
    Write-Host "Hoja de contacto: $OutDir\contact-sheet.png"
  }
} catch { Write-Host "Hoja de contacto no generada: $($_.Exception.Message)" }

$md = "# Humo no interactivo`n`n> Informativo. El runner no tiene monitor ni GPU reales. Un OK aquí no valida lo visual; un fallo no invalida la compilación.`n`n| Paso | Resultado | Detalle |`n|---|---|---|`n"
foreach ($r in $rows) { $md += "| $($r.Paso) | $($r.Resultado) | $($r.Detalle -replace '\|', '/') |`n" }
$md | Set-Content (Join-Path $OutDir 'smoke.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $md }
$bad = @($rows | Where-Object { $_.Resultado -ne 'OK' -and $_.Resultado -ne 'omitido' -and $_.Resultado -notlike '*(informativo)' })
if ($bad.Count) { Write-Host "::error title=Humo del Dock::$($bad.Count) paso(s) fallaron: $(($bad | ForEach-Object { $_.Paso }) -join '; ')"; exit 1 }
exit 0
