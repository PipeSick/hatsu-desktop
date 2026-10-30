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
foreach ($pose in 'rest', 'hover', 'hidden') {
  Run "Dock offscreen $pose" $dock @('--render-png', "`"$OutDir\dock-$pose.png`"", '--pose', $pose) 60
}
Run 'Dock selftest (ventana real, 3 s)' $dock @('--selftest') 60
$md = "# Humo no interactivo`n`n> Informativo. El runner no tiene monitor ni GPU reales. Un OK aquí no valida lo visual; un fallo no invalida la compilación.`n`n| Paso | Resultado | Detalle |`n|---|---|---|`n"
foreach ($r in $rows) { $md += "| $($r.Paso) | $($r.Resultado) | $($r.Detalle -replace '\|', '/') |`n" }
$md | Set-Content (Join-Path $OutDir 'smoke.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $md }
exit 0
