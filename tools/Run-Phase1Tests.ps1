<#
.SYNOPSIS
  Paquete de pruebas de la Fase 1 de WinMac: comprueba requisitos, compila, ejecuta pruebas NO invasivas,
  recolecta métricas y genera JSON + resumen. No instala nada en el sistema y no cambia configuraciones.

.DESCRIPTION
  Qué hace, en orden:
    1. Comprueba requisitos (Windows 11, SDK de .NET 10, espacio, batería). NO instala nada: si falta algo, lo dice y sale.
    2. Pide tu consentimiento para que `dotnet restore` descargue paquetes NuGet a tu caché de usuario (salvo -AcceptRestore).
    3. Compila los prototipos a la carpeta local `artifacts/` (ignorada por git).
    4. Ejecuta: sonda de plataforma (solo lectura) y los prototipos de dock (ventana visible ~35 s cada uno, sin tocar tu configuración).
    5. Recolecta métricas -> JSON en `bench-results/<fecha>/`.
    6. Genera capturas de las poses estáticas (opcional; ver -NoScreenshots) y `summary.md`.
    7. Limpia: termina procesos lanzados; no queda ningún cambio temporal que restaurar.

  NUNCA ejecuta S5.TaskbarGuard (modifica el escritorio). Tampoco existe ningún parámetro para ejecutarlo desde aquí.
  Opt-in (no se ejecutan por defecto): -IncludeWindowTracker (S1, pasivo, 30 s), -IncludeUia (S6, interactivo).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\Run-Phase1Tests.ps1
  (-ExecutionPolicy Bypass vale solo para ESTA ejecución; no cambia la política del sistema.)

.EXAMPLE
  .\tools\Run-Phase1Tests.ps1 -CheckOnly          # solo comprueba requisitos
  .\tools\Run-Phase1Tests.ps1 -Quick              # prueba de humo rápida (fases acortadas; no se evalúan umbrales)
  .\tools\Run-Phase1Tests.ps1 -Runs 3             # 3 repeticiones; el resumen usa la mediana
#>
[CmdletBinding()]
param(
  [switch]$CheckOnly,
  [switch]$AcceptRestore,
  [switch]$SkipBuild,
  [switch]$Quick,
  [int]$Runs = 1,
  [switch]$NoScreenshots,
  [switch]$IncludeWindowTracker,
  [switch]$IncludeUia,
  [string]$OutDir,
  [string]$SummaryFrom   # regenera summary.md a partir de una carpeta de resultados existente (no compila ni ejecuta nada)
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Spikes = Join-Path $Root 'spikes'
$Art = Join-Path $Root 'artifacts'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutDir) { $OutDir = Join-Path $Root "bench-results\$Stamp" }
$script:Children = New-Object System.Collections.Generic.List[int]

function Say($m, $c = 'Gray') { Write-Host $m -ForegroundColor $c }
function Fail($m, $code) { Say "ERROR: $m" Red; exit $code }

# ------------------------------------------------------------------ Resumen (también reutilizable con -SummaryFrom)
function New-Summary {
function Median($xs) { $s = @($xs | Sort-Object); if ($s.Count -eq 0) { return $null }; $s[[int][math]::Floor(($s.Count - 1) / 2)] }
$thr = Get-Content (Join-Path $PSScriptRoot 'thresholds.json') -Raw | ConvertFrom-Json
$reports = @(Get-ChildItem $OutDir -Filter 'S*.Dock*-*.json' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
$rows = @(); $md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# WinMac — Resumen de pruebas Fase 1 ($Stamp)")
[void]$md.AppendLine("")
[void]$md.AppendLine("**Máquina:** $($info.Windows) · CPU: $($info.Cpu) · GPU: $($info.Gpu -join '; ') · RAM: $($info.RamGb) GB · Ejecuciones por prototipo: $Runs" + $(if ($Quick) { " · **MODO RÁPIDO (umbrales no evaluados)**" } else { "" }))
[void]$md.AppendLine("")
[void]$md.AppendLine("> Leyenda: **Objetivo** = umbral de tools/thresholds.json (aceptado como objetivo, no como resultado). **Medición** = valor producido por este equipo en esta ejecución (mediana de las ejecuciones). Nada de esto es una conclusión sobre qué tecnología gana: eso lo decide el informe, con tus revisiones.")
[void]$md.AppendLine("")
$groups = $reports | Group-Object { "$($_.Spike) / $($_.Variant)" }
function Row($metric, $value, $target, $ok) { $st = if ($Quick) { 'n/a' } elseif ($ok) { 'CUMPLE' } else { 'NO CUMPLE' }; "| $metric | $([math]::Round($value,2)) | $target | $st |" }
foreach ($g in $groups) {
  $rs = @($g.Group); $first = $rs[0]
  $hz = [double](Median ($rs | ForEach-Object { $_.RefreshHz })); $budget = 1000.0 / $hz
  $m = @{
    sd = Median ($rs | ForEach-Object { $_.Sweep.DroppedPct }); sp99 = Median ($rs | ForEach-Object { $_.Sweep.P99Ms }); sp50 = Median ($rs | ForEach-Object { $_.Sweep.P50Ms }); sfps = Median ($rs | ForEach-Object { $_.Sweep.MeanFps })
    hd = Median ($rs | ForEach-Object { $_.Hide.DroppedPct }); idle = Median ($rs | ForEach-Object { $_.IdleCpuPct }); cpu = Median ($rs | ForEach-Object { $_.SweepCpuPct })
    ws = Median ($rs | ForEach-Object { $_.WorkingSetMb }); ap = Median ($rs | ForEach-Object { $_.ApplyP95Ms }); st = Median ($rs | ForEach-Object { $_.StartupMs })
  }
  [void]$md.AppendLine("## $($g.Name)  (ejecuciones: $($rs.Count), refresco: $hz Hz, presupuesto de frame: $([math]::Round($budget,2)) ms)")
  [void]$md.AppendLine("")
  [void]$md.AppendLine("| Métrica | Medición | Objetivo | Estado |"); [void]$md.AppendLine("|---|---|---|---|")
  [void]$md.AppendLine((Row 'Frames perdidos en barrido (%)' $m.sd ("<= " + $thr.sweep_droppedPct_max) ($m.sd -le $thr.sweep_droppedPct_max)))
  [void]$md.AppendLine((Row 'Frames perdidos en ocultar/mostrar (%)' $m.hd ("<= " + $thr.hide_droppedPct_max) ($m.hd -le $thr.hide_droppedPct_max)))
  [void]$md.AppendLine((Row 'Frame-time p99 en barrido (ms)' $m.sp99 ("<= " + [math]::Round($thr.sweep_p99_budgetMultiple_max * $budget, 1)) ($m.sp99 -le $thr.sweep_p99_budgetMultiple_max * $budget)))
  [void]$md.AppendLine((Row 'CPU en reposo (%)' $m.idle ("<= " + $thr.idleCpuPct_max) ($m.idle -le $thr.idleCpuPct_max)))
  [void]$md.AppendLine((Row 'CPU en animación (%)' $m.cpu ("<= " + $thr.sweepCpuPct_max) ($m.cpu -le $thr.sweepCpuPct_max)))
  [void]$md.AppendLine((Row 'Memoria (working set, MB)' $m.ws ("<= " + $thr.workingSetMb_max) ($m.ws -le $thr.workingSetMb_max)))
  [void]$md.AppendLine((Row 'Costo CPU de aplicar el frame, p95 (ms) [proxy de latencia]' $m.ap ("<= " + [math]::Round($thr.applyP95_budgetFraction_max * $budget, 1)) ($m.ap -le $thr.applyP95_budgetFraction_max * $budget)))
  [void]$md.AppendLine((Row 'Arranque hasta primer render (ms)' $m.st ("<= " + $thr.startupMs_max) ($m.st -le $thr.startupMs_max)))
  [void]$md.AppendLine("")
  [void]$md.AppendLine("Informativo: p50 barrido $([math]::Round($m.sp50,2)) ms · FPS medio barrido $([math]::Round($m.sfps,1))")
  [void]$md.AppendLine("")
  [void]$md.AppendLine("Fidelidad implementada: " + ($first.FeaturesImplemented -join ', '))
  [void]$md.AppendLine("")
  [void]$md.AppendLine("Fidelidad NO implementada: " + ($first.FeaturesMissing -join ', '))
  [void]$md.AppendLine("")
  if ($first.Notes) { [void]$md.AppendLine("Nota: $($first.Notes)"); [void]$md.AppendLine("") }
  $rows += [pscustomobject]@{ Prototype = $g.Name; Runs = $rs.Count; DroppedPct = $m.sd; P99Ms = $m.sp99; IdleCpu = $m.idle; SweepCpu = $m.cpu; MemMB = $m.ws; ApplyP95Ms = $m.ap }
}
if ($failed.Count) { [void]$md.AppendLine("## Ejecuciones fallidas"); $failed | ForEach-Object { [void]$md.AppendLine("- $_") }; [void]$md.AppendLine("") }
[void]$md.AppendLine("## Limitaciones de estas mediciones")
[void]$md.AppendLine("- La cadencia de frames de WPF/WinUI se toma de CompositionTarget.Rendering y la de DirectComposition del tiempo entre Present(); ninguna es presentación real al monitor. Para el dato definitivo, medir con PresentMon (herramienta externa; no incluida).")
[void]$md.AppendLine("- La latencia entrada->pantalla no se mide (requiere hardware); se informa el costo de CPU de aplicar cada frame como proxy.")
[void]$md.AppendLine("- Los prototipos son mínimos y no equivalen a un dock completo. Que uno cumpla umbrales no prueba que el dock final los cumpla.")
[void]$md.AppendLine("- Capturas de fidelidad: ver carpeta screenshots/ y docs/02-fidelidad.md para compararlas con tus capturas de referencia de macOS Tahoe.")
$md.ToString() | Set-Content (Join-Path $OutDir 'summary.md') -Encoding UTF8
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutDir 'summary.json') -Encoding UTF8
if ($rows) { $rows | Format-Table -AutoSize | Out-String | Write-Host }
}

if ($SummaryFrom) {
  $OutDir = $SummaryFrom; $Stamp = Split-Path -Leaf $OutDir; $failed = @()
  $mj = Join-Path $OutDir 'machine.json'
  if (Test-Path $mj) { $info = Get-Content $mj -Raw | ConvertFrom-Json; $Quick = [bool]$info.Quick; $Runs = $info.Runs } else { $info = [pscustomobject]@{ Windows = '?'; Cpu = '?'; Gpu = @('?'); RamGb = '?' } }
  New-Summary; Say "Resumen regenerado en $OutDir" Green; exit 0
}

# ------------------------------------------------------------------ 1. Requisitos
Say "== 1/7 Requisitos ==" Cyan
$problems = @(); $warnings = @()
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { Fail 'Este script solo funciona en Windows.' 2 }
$cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$build = [int]$cv.CurrentBuildNumber
Say ("Windows: {0} {1} build {2}.{3}" -f $cv.ProductName, $cv.DisplayVersion, $cv.CurrentBuildNumber, $cv.UBR)
if ($build -lt 22000) { $problems += "Se requiere Windows 11 (build >= 22000). Detectado: $build." }
elseif ($build -lt 26100) { $warnings += "El mínimo del proyecto es Windows 11 24H2 (build 26100). Detectado: $build. Se ejecuta igual, como dato adicional." }

$dotnetOk = $false
$dn = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dn) {
  $sdks = & dotnet --list-sdks 2>$null
  $maj = @($sdks | ForEach-Object { try { [int](($_ -split '\.')[0]) } catch { 0 } })
  if ($maj | Where-Object { $_ -ge 10 }) { $dotnetOk = $true; Say ("SDK de .NET: " + (($sdks | Select-Object -Last 1))) }
}
if (-not $dotnetOk) { $problems += "Falta el SDK de .NET 10 (o superior). Instálalo tú desde https://dotnet.microsoft.com/download/dotnet/10.0 (el script NO instala nada) y vuelve a ejecutar." }

if (-not (Test-Path (Join-Path $Spikes 'WinMac.Spikes.slnx'))) { $problems += "No se encuentra spikes\WinMac.Spikes.slnx. Ejecuta el script desde un clon completo del repositorio." }
$drv = Get-PSDrive -Name ($Root.Substring(0, 1)) -ErrorAction SilentlyContinue
if ($drv -and $drv.Free -lt 2GB) { $problems += ("Menos de 2 GB libres en la unidad del repositorio ({0:N1} GB)." -f ($drv.Free / 1GB)) }
try {
  $bat = Get-CimInstance Win32_Battery -ErrorAction Stop
  if ($bat -and $bat.BatteryStatus -eq 1) { $warnings += 'Equipo con batería y SIN conectar a corriente: las métricas de CPU/frames pueden verse afectadas. Conéctalo.' }
} catch { }
$warnings += 'Cierra otras aplicaciones pesadas y no uses el equipo durante las ejecuciones (cada prototipo aparece ~35 s en la parte inferior de la pantalla).'
foreach ($w in $warnings) { Say "AVISO: $w" Yellow }
if ($problems) { foreach ($p in $problems) { Say "FALTA: $p" Red }; exit 2 }
Say 'Requisitos OK.' Green
if ($CheckOnly) { exit 0 }

# ------------------------------------------------------------------ 2. Consentimiento
Say "`n== 2/7 Consentimiento ==" Cyan
if (-not $SkipBuild) {
  Say @"
La compilación ejecuta 'dotnet restore', que DESCARGARÁ paquetes NuGet a tu caché de usuario (%USERPROFILE%\.nuget\packages):
  Microsoft.WindowsAppSDK 2.6.4 (+ Microsoft.Windows.SDK.BuildTools) para el prototipo WinUI 3,
  Vortice.Direct3D11 / Direct2D1 / DirectComposition / DXGI 3.8.3 para el prototipo Win32+DirectComposition.
No se instala nada en el sistema ni se modifica ninguna configuración. Los binarios van a '$Art'.
"@
  if (-not $AcceptRestore) {
    if (-not [Environment]::UserInteractive) { Fail 'Sin consentimiento: ejecuta con -AcceptRestore o en una consola interactiva.' 3 }
    $a = Read-Host '¿Permites esa descarga? (escribe SI para continuar)'
    if ($a -ne 'SI') { Fail 'Cancelado por el usuario. No se hizo ningún cambio.' 3 }
  }
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ------------------------------------------------------------------ 3. Compilación
Say "`n== 3/7 Compilación ==" Cyan
$components = @(
  @{ Name = 'S7.PlatformProbe'; Extra = @() },
  @{ Name = 'S1.WindowTracker'; Extra = @() },
  @{ Name = 'S6.UiaMenus';      Extra = @() },
  @{ Name = 'S2.DockWpf';       Extra = @() },
  @{ Name = 'S3.DockWinUI';     Extra = @('-p:Platform=x64') },
  @{ Name = 'S4.DockDComp';     Extra = @() }
)
$built = @{}
foreach ($c in $components) {
  $n = $c.Name; $dest = Join-Path $Art $n; $exe = Join-Path $dest "$n.exe"
  if ($SkipBuild) { $built[$n] = (Test-Path $exe); continue }
  Say "Compilando $n ..."
  $log = Join-Path $OutDir "build-$n.log"
  & dotnet build (Join-Path $Spikes "$n\$n.csproj") -c Release -o $dest --nologo @($c.Extra) *> $log
  $built[$n] = ($LASTEXITCODE -eq 0) -and (Test-Path $exe)
  if ($built[$n]) { Say "  OK" Green } else { Say "  FALLÓ (ver $log)" Red }
}

# ------------------------------------------------------------------ utilidades de ejecución
function Start-Child($exe, $argList, $wd) {
  $so = Join-Path $OutDir ("stdout-" + [IO.Path]::GetFileNameWithoutExtension($exe) + "-" + (Get-Random) + ".txt")
  $p = Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $wd -PassThru -RedirectStandardOutput $so
  $script:Children.Add($p.Id) | Out-Null
  return $p
}
function Stop-Child($p) { try { if (-not $p.HasExited) { $p.Kill() } } catch { } }
function Run-Bench($name, $variant) {
  $exe = Join-Path (Join-Path $Art $name) "$name.exe"
  $a2 = @('--variant', $variant, '--out', "`"$OutDir`"")
  if ($Quick) { $a2 += '--quick' }
  $p = Start-Child $exe $a2 (Join-Path $Art $name)
  $limit = if ($Quick) { 60 } else { 120 }
  if (-not $p.WaitForExit($limit * 1000)) { Stop-Child $p; Say "  TIEMPO AGOTADO ($limit s): $name/$variant" Red; return $false }
  if ($p.ExitCode -ne 0) { Say "  Salió con código $($p.ExitCode): $name/$variant" Red; return $false }
  return $true
}

# ------------------------------------------------------------------ 4. Pruebas
Say "`n== 4/7 Pruebas seguras ==" Cyan
$info = [ordered]@{
  Stamp = $Stamp; Windows = "$($cv.ProductName) $($cv.DisplayVersion) build $($cv.CurrentBuildNumber).$($cv.UBR)"
  Cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
  Gpu = @(Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) (driver $($_.DriverVersion))" })
  RamGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
  Quick = [bool]$Quick; Runs = $Runs
  Built = $built
}
$info | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutDir 'machine.json') -Encoding UTF8

if ($built['S7.PlatformProbe']) {
  Say 'S7 sonda de plataforma (solo lectura) ...'
  & (Join-Path $Art 'S7.PlatformProbe\S7.PlatformProbe.exe') *> (Join-Path $OutDir 'S7-platform.txt')
}
if ($IncludeWindowTracker -and $built['S1.WindowTracker']) {
  Say 'S1 seguimiento pasivo de ventanas, 30 s (usa el equipo con normalidad) ...'
  Push-Location $OutDir; & (Join-Path $Art 'S1.WindowTracker\S1.WindowTracker.exe') 30 *> (Join-Path $OutDir 'S1-stdout.txt'); Pop-Location
}
if ($IncludeUia -and $built['S6.UiaMenus']) {
  Say 'S6 UIA: pon en primer plano la app a probar durante la cuenta atrás de 8 s ...'
  Push-Location $OutDir; & (Join-Path $Art 'S6.UiaMenus\S6.UiaMenus.exe') 8; Pop-Location
}

$matrix = @(
  @{ Name = 'S2.DockWpf';   Variants = @('layered', 'dwm') },
  @{ Name = 'S3.DockWinUI'; Variants = @('acrylic', 'plain') },
  @{ Name = 'S4.DockDComp'; Variants = @('glass') }
)
$failed = @()
try {
  for ($r = 1; $r -le $Runs; $r++) {
    foreach ($m in $matrix) {
      if (-not $built[$m.Name]) { continue }
      foreach ($v in $m.Variants) {
        Say ("[{0}/{1}] {2} / {3} ..." -f $r, $Runs, $m.Name, $v)
        if (-not (Run-Bench $m.Name $v)) { $failed += "$($m.Name)/$v" }
        Start-Sleep -Seconds 3
      }
    }
  }

  # -------------------------------------------------------------- 5. Capturas de fidelidad
  if (-not $NoScreenshots) {
    Say "`n== 5/7 Capturas de fidelidad (recorte inferior de la pantalla principal) ==" Cyan
    Say 'AVISO: la captura incluye lo que haya detrás del dock (tu escritorio). Se guarda solo en local. Usa -NoScreenshots para omitirla.' Yellow
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    Add-Type -Namespace W -Name D -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
    [W.D]::SetProcessDPIAware() | Out-Null
    $shots = Join-Path $OutDir 'screenshots'; New-Item -ItemType Directory -Force -Path $shots | Out-Null
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $cw = [math]::Min($b.Width, 1800); $ch = [math]::Min($b.Height, 380)
    foreach ($m in $matrix) {
      if (-not $built[$m.Name]) { continue }
      foreach ($v in $m.Variants) {
        foreach ($pose in 'rest', 'hover', 'hidden') {
          $exe = Join-Path (Join-Path $Art $m.Name) "$($m.Name).exe"
          $p = Start-Child $exe @('--variant', $v, '--pose', $pose) (Join-Path $Art $m.Name)
          Start-Sleep -Seconds 3
          $bmp = New-Object System.Drawing.Bitmap $cw, $ch
          $g = [System.Drawing.Graphics]::FromImage($bmp)
          $g.CopyFromScreen($b.X + [int](($b.Width - $cw) / 2), $b.Y + $b.Height - $ch, 0, 0, $bmp.Size)
          $bmp.Save((Join-Path $shots "$($m.Name)-$v-$pose.png"), [System.Drawing.Imaging.ImageFormat]::Png)
          $g.Dispose(); $bmp.Dispose(); Stop-Child $p
          Start-Sleep -Milliseconds 800
        }
      }
    }
  }
}
finally {
  # -------------------------------------------------------------- 7. Limpieza
  foreach ($id in $script:Children) { try { $pr = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($pr) { $pr.Kill() } } catch { } }
}

Say "`n== 6/7 Resumen ==" Cyan
New-Summary

# ------------------------------------------------------------------ 7. Estado final
Say "`n== 7/7 Limpieza ==" Cyan
Say 'Procesos lanzados: terminados. Configuración de Windows: no modificada. Cambios temporales a restaurar: ninguno.' Green
Say "Resultados: $OutDir" Green
Say "Envíame la carpeta (o al menos summary.md, machine.json, *.json y screenshots/)." Green
if ($failed.Count) { exit 1 } else { exit 0 }
