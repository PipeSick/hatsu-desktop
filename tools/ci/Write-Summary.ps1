<#
  Job Summary del humo del Dock: tablas con PNG (tamaño), bench (WARP) y selftest, con enlaces a los artefactos.
  GitHub NO renderiza imágenes de artefactos dentro del resumen (requieren autenticación): las imágenes se ven abriendo el artefacto.
#>
param([string]$SmokeDir = 'smoke', [string]$SmokeUrl = '', [string]$SheetUrl = '', [string]$BinUrl = '', [string]$Sha = '')
$out = New-Object System.Text.StringBuilder
function L($t = '') { [void]$out.AppendLine($t) }
L '# Dock — validación en runner Windows'
L ''
L '> **Qué es y qué no es.** Los PNG los produce el **renderizador real del Dock** (`DockController` + `DockPainter` + `GlassBackdrop`) fuera de pantalla, sobre **WARP (render por software)** y con un **fondo procedural**.'
L '> Validan que el dibujo se ejecuta y muestran su aspecto aproximado. **No validan** la ventana con DirectComposition, la entrada del ratón, el fondo real del escritorio, la GPU física ni el rendimiento.'
L ''
L '## Artefactos'
L '| Artefacto | Contenido |'
L '|---|---|'
if ($SheetUrl) { L "| [Hoja de contacto]($SheetUrl) | ``contact-sheet.png`` (todas las escenas, claro/oscuro) + vistas previas |" }
if ($SmokeUrl) { L "| [Resultados de humo]($SmokeUrl) | PNG a 2×, bench, selftest, logs |" }
if ($BinUrl) { L "| [Binarios]($BinUrl) | Dock y prototipos publicados (**temporales, no son una versión oficial**) |" }
L ''
$pngs = Get-ChildItem $SmokeDir -Filter 'dock-*.png' -ErrorAction SilentlyContinue | Sort-Object Name
if ($pngs) {
  Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
  L '## PNG generados'
  L '| Archivo | Píxeles | KB |'
  L '|---|---|---|'
  foreach ($f in $pngs) {
    $dim = '?'
    try { $im = [System.Drawing.Image]::FromFile($f.FullName); $dim = "$($im.Width)×$($im.Height)"; $im.Dispose() } catch { }
    L "| $($f.Name) | $dim | $([math]::Round($f.Length / 1KB, 1)) |"
  }
  L ''
} else { L '**No se generó ningún PNG.**'; L '' }

$sheet = Join-Path $SmokeDir 'contact-sheet.png'
if (Test-Path $sheet) { L "Hoja de contacto: ``contact-sheet.png`` ($([math]::Round((Get-Item $sheet).Length / 1KB)) KB) — abre el artefacto para verla." ; L '' }

$bench = Get-ChildItem $SmokeDir -Filter 'bench-offscreen-*.json' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($bench) {
  $b = Get-Content $bench.FullName -Raw | ConvertFrom-Json
  L '## Bench de dibujo fuera de pantalla — **RENDER POR SOFTWARE (WARP)**'
  L ''
  L "> Dispositivo: **$($b.Device)** (software: $($b.IsSoftware)). **No son FPS reales ni representan una GPU física.** Sirven para comparar versiones del painter y detectar regresiones gruesas."
  L ''
  L '| Medida | p50 | p95 | p99 | máx |'
  L '|---|---|---|---|---|'
  L "| Dibujar un fotograma (ms) | $($b.PaintMs.P50) | $($b.PaintMs.P95) | $($b.PaintMs.P99) | $($b.PaintMs.Max) |"
  L "| Dibujar + leer el fotograma (ms) | $($b.PaintPlusReadbackMs.P50) | $($b.PaintPlusReadbackMs.P95) | $($b.PaintPlusReadbackMs.P99) | $($b.PaintPlusReadbackMs.Max) |"
  L ''
  L "Superficie $($b.SurfacePx) · fotogramas $($b.Frames) · construcción del fondo $($b.BackdropBuildMs) ms · memoria $($b.WorkingSetMb) MB"
  L ''
  L 'Limitaciones: ' + (($b.Limitations | ForEach-Object { "$_" }) -join ' · ')
  L ''
} else { L '**No se generó el bench.**'; L '' }

$st = Get-ChildItem $SmokeDir -Filter 'Dock_selftest.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($st) {
  L '## `--selftest`'
  L ''
  L '```'
  Get-Content $st.FullName | Where-Object { $_ -match '\[OK |\[FALLO\]|SELFTEST|Windows:' } | ForEach-Object { L $_ }
  L '```'
  L ''
}
$smoke = Join-Path $SmokeDir 'smoke.md'
if (Test-Path $smoke) { L '## Pasos del humo'; L ''; Get-Content $smoke | Select-Object -Skip 4 | ForEach-Object { L $_ } }
$txt = $out.ToString()
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $txt } else { Write-Host $txt }
