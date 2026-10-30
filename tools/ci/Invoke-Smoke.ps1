<#
  Humo NO interactivo en el runner (sin monitor/GPU reales). Cada paso es informativo y deja evidencia en $OutDir.
  - Prototipos en --quick: detectan caídas al arrancar (p. ej. fallo de D3D11/DirectComposition en un runner sin GPU).
  - Dock.App --render-png: renderizado FUERA de pantalla (Direct2D sobre WARP) -> PNG descargable. Valida que el dibujo corre
    y permite ver el resultado, pero NO valida composición en ventana, blur sobre el escritorio real ni rendimiento.
#>
param([string]$PublishDir = 'ci-publish', [string]$OutDir = 'smoke')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$rows = @()
function Run($label, $exe, $argList, $timeoutSec) {
  if (-not (Test-Path $exe)) { Write-Host "OMITIDO $label (no existe $exe)"; $script:rows += [pscustomobject]@{ Paso = $label; Resultado = 'omitido'; Detalle = 'no existe en este commit' }; return }
  $so = Join-Path $OutDir ($label -replace '[^\w\.-]', '_') + '.txt'
  $p = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory (Split-Path $exe) -PassThru -RedirectStandardOutput $so -RedirectStandardError "$so.err" -WindowStyle Hidden
  if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { }; $res = 'TIEMPO AGOTADO' }
  elseif ($p.ExitCode -eq 0) { $res = 'OK' } else { $res = "salió con $($p.ExitCode)" }
  $det = (Get-Content $so, "$so.err" -ErrorAction SilentlyContinue | Select-Object -Last 3) -join ' | '
  Write-Host "$label -> $res  $det"
  $script:rows += [pscustomobject]@{ Paso = $label; Resultado = $res; Detalle = $det }
}
$q = @('--quick', '--out', "`"$OutDir`"")
Run 'S2 WPF layered --quick' "$PublishDir/S2.DockWpf/S2.DockWpf.exe" (@('--variant', 'layered') + $q) 90
Run 'S4 DComp glass --quick' "$PublishDir/S4.DockDComp/S4.DockDComp.exe" (@('--variant', 'glass') + $q) 90
Run 'S3 WinUI acrylic --quick' "$PublishDir/S3.DockWinUI/S3.DockWinUI.exe" (@('--variant', 'acrylic') + $q) 90
$dock = "$PublishDir/Dock.App/WinMac.Dock.App.exe"
# Escenas con el renderizador REAL (mismo DockController + DockPainter + GlassBackdrop que el dock en pantalla), a 2x para poder revisar detalles.
foreach ($theme in 'light', 'dark') {
  foreach ($pose in 'rest', 'hover', 'open', 'edge', 'hidden') {
    Run "Dock offscreen $theme/$pose" $dock @('--render-png', "`"$OutDir\dock-$theme-$pose.png`"", '--pose', $pose, '--theme', $theme, '--scale', '2') 90
  }
}
Run 'Dock offscreen tint (sin fondo)' $dock @('--render-png', "`"$OutDir\dock-light-tint-hover.png`"", '--pose', 'hover', '--tier', 'tint', '--scale', '2') 90
Run 'Dock bench offscreen (WARP; NO es FPS real)' $dock @('--bench-offscreen', "`"$OutDir\bench-offscreen-light.json`"", '--theme', 'light') 180
Run 'Dock selftest' $dock @('--selftest', '--out', "`"$OutDir\selftest`"") 240
# Vista previa en el log: el sandbox de desarrollo no puede descargar artefactos, pero SÍ leer los logs del job.
# Se emiten versiones reducidas (escala 0.75) de dos escenas en base64 dentro de grupos plegados. Los PNG completos (2x) están en el artefacto.
foreach ($pv in @(@{ n = 'light-hover'; t = 'light'; p = 'hover' }, @{ n = 'dark-open'; t = 'dark'; p = 'open' })) {
  $f = Join-Path $OutDir "preview-$($pv.n).png"
  Run "Vista previa $($pv.n)" $dock @('--render-png', "`"$f`"", '--pose', $pv.p, '--theme', $pv.t, '--scale', '0.75') 90
  if (Test-Path $f) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f))
    Write-Host "::group::PREVIEW_B64 $($pv.n) bytes=$((Get-Item $f).Length) b64len=$($b64.Length)"
    for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host ("PVB64|" + $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i))) }
    Write-Host "::endgroup::"
  }
}
$md = "# Humo no interactivo`n`n> Informativo. El runner no tiene monitor ni GPU reales. Un OK aquí no valida lo visual; un fallo no invalida la compilación.`n`n| Paso | Resultado | Detalle |`n|---|---|---|`n"
foreach ($r in $rows) { $md += "| $($r.Paso) | $($r.Resultado) | $($r.Detalle -replace '\|', '/') |`n" }
$md | Set-Content (Join-Path $OutDir 'smoke.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $md }
exit 0
