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
$dock = "$PublishDir/Dock.App/WinMac.Dock.App.exe"
# Escenas con el renderizador REAL (mismo DockController + DockPainter + GlassBackdrop que el dock en pantalla), a 2x para poder revisar detalles.
foreach ($theme in 'light', 'dark') {
  foreach ($pose in 'rest', 'hover', 'open', 'edge', 'hidden', 'preview') {
    Run "Dock offscreen $theme/$pose" $dock @('--render-png', "`"$OutDir\dock-$theme-$pose.png`"", '--pose', $pose, '--theme', $theme, '--scale', '2') 90
  }
}
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
$bar = "$PublishDir/Bar.App/WinMac.Bar.App.exe"
foreach ($theme in 'light', 'dark') { foreach ($bg in 'glass', 'transparent') {
  Run "Barra offscreen $theme/$bg" $bar @('--render-png', "`"$OutDir\bar-$theme-$bg.png`"", '--theme', $theme, '--bg', $bg, '--scale', '2') 90
} }
Run 'Barra offscreen light/glass con resalte' $bar @('--render-png', "`"$OutDir\bar-light-glass-hover.png`"", '--theme', 'light', '--bg', 'glass', '--scale', '2', '--hover', '4') 90
Run 'Barra --status (informativo)' $bar @('--status') 60 -Informational
Run 'Barra selftest' $bar @('--selftest') 120
Run 'Barra selftest-appbar (informativo)' $bar @('--selftest-appbar') 120 -Informational
# Vista previa en el log: el sandbox de desarrollo no puede descargar artefactos, pero SÍ leer los logs del job.
# Se emiten versiones reducidas (escala 0.75) de dos escenas en base64 dentro de grupos plegados. Los PNG completos (2x) están en el artefacto.
foreach ($pv in @(@{ n = 'light-hover'; t = 'light'; p = 'hover' }, @{ n = 'dark-open'; t = 'dark'; p = 'open' }, @{ n = 'light-preview'; t = 'light'; p = 'preview' }, @{ n = 'dark-preview'; t = 'dark'; p = 'preview' })) {
  $f = Join-Path $OutDir "preview-$($pv.n).png"
  Run "Vista previa $($pv.n)" $dock @('--render-png', "`"$f`"", '--pose', $pv.p, '--theme', $pv.t, '--scale', '0.75') 90
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
