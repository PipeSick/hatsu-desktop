<#
  Integración WinMac + NotchWin en el runner de Windows: submódulo resuelto, pruebas de NotchWin en Windows, Build-Local.ps1 con la Isla, y arranque REAL bajo WinMac.exe.
  Pasa/falla (PASS/FAIL, sale con 1 si alguna falla): clon y submódulo, pruebas de NotchWin, publicación de los 4 ejecutables, la Barra y el Dock REALES llegan a Running aunque la Isla
  falle, «Isla no instalada» no rompe nada, y el cierre ordenado no deja procesos.
  Informativo (INFO, nunca falla el CI): si NotchWin REAL (WinUI 3) llega a Running en el runner. El runner no tiene escritorio interactivo ni monitor; que la Isla arranque y se enlace
  de verdad se valida a mano en el PC (docs/16 §7.5, M13–M24).
#>
param([string]$DistDir = 'dist\WinMac', [string]$NotchPath = 'C:\nw', [string]$OutDir = 'smoke')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path
$DistDir = (Resolve-Path $DistDir).Path
$script:fails = 0; $script:rows = @()
function Check([string]$name, [bool]$ok, [string]$detail = '') { $r = if ($ok) { 'PASS' } else { 'FAIL'; $script:fails++ }; Write-Host ("{0,-4} {1}  {2}" -f $r, $name, $detail); $script:rows += [pscustomobject]@{ Prueba = $name; Resultado = $r; Detalle = $detail } }
function Info([string]$name, [bool]$ok, [string]$detail = '') { $r = if ($ok) { 'PASS (info)' } else { 'INFO' }; Write-Host ("{0,-11} {1}  {2}" -f $r, $name, $detail); $script:rows += [pscustomobject]@{ Prueba = $name; Resultado = $r; Detalle = $detail } }
function WaitFor([scriptblock]$cond, [int]$sec = 20) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 200 }; return [bool](& $cond) }
function Cleanup { Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400 }
function Alive { @(Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App' -ErrorAction SilentlyContinue).Count }
function LogHas($log, [string]$t) { (Test-Path $log) -and (Select-String -Path $log -SimpleMatch -Pattern $t -Quiet) }

# ── 1. Clon de NotchWin, submódulo y pruebas en Windows
$sub = (git -C $NotchPath submodule status 2>&1) -join ' '
Check 'Submódulo external/winmac resuelto desde un clon limpio' ($sub -match '^\s*[0-9a-f]{40} external/winmac' -and -not $sub.StartsWith('-')) $sub
Check 'WinMac.Ipc del submódulo presente' (Test-Path (Join-Path $NotchPath 'external\winmac\src\WinMac.Ipc\WinMac.Ipc.csproj'))
$t = (& dotnet test (Join-Path $NotchPath 'tests\NotchWin.Core.Tests') -c Release 2>&1 | Out-String)
$m = [regex]::Match($t, 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)')
Check 'Pruebas de NotchWin.Core en Windows (76)' ($m.Success -and $m.Groups[1].Value -eq '0' -and [int]$m.Groups[4].Value -ge 76) $m.Value
if (-not $m.Success -or $m.Groups[1].Value -ne '0') { ($t -split "`n" | Select-String -Pattern 'Failed |error' | Select-Object -First 15) | ForEach-Object { Write-Host "  $_" } }

# ── 2. Salida de Build-Local.ps1 (se ejecuta antes, en el workflow)
foreach ($f in 'WinMac.exe', 'WinMac.Bar.App.exe', 'WinMac.Dock.App.exe', 'NotchWin\NotchWin.App.exe') { Check "Build-Local dejó $f" (Test-Path (Join-Path $DistDir $f)) }
$pri = @(Get-ChildItem (Join-Path $DistDir 'NotchWin') -Filter '*.pri' -ErrorAction SilentlyContinue)   # el nombre del .pri lo decide el SDK (no es necesariamente NotchWin.App.pri)
Check 'Build-Local dejó el recurso .pri de NotchWin (WinUI 3 sin empaquetar)' ($pri.Count -gt 0) (($pri | ForEach-Object Name) -join ', ')
Check 'NotchWin publicado autocontenido (trae el runtime de Windows App SDK)' ((Get-ChildItem (Join-Path $DistDir 'NotchWin') -Filter 'Microsoft.WindowsAppRuntime*.dll' -ErrorAction SilentlyContinue).Count -gt 0)
$hostExe = Join-Path $DistDir 'WinMac.exe'

# ── 3. Arranque real bajo el host (Barra y Dock reales + NotchWin real)
function RunHost([string]$name, $notchModule) {
  $d = Join-Path ([IO.Path]::GetTempPath()) ("winmac-notch-" + $name + [Guid]::NewGuid().ToString('N').Substring(0, 6)); New-Item -ItemType Directory -Force -Path $d | Out-Null
  $mods = @{ bar = @{ enabled = $true; args = @('--no-config', '--no-reserve') }; dock = @{ enabled = $true; args = @('--no-config') } }
  if ($notchModule) { $mods.notch = $notchModule }
  @{ modules = $mods } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d 'winmac.json') -Encoding UTF8
  $log = Join-Path $d 'host.log'
  $p = Start-Process -FilePath $hostExe -ArgumentList @('--no-tray', '--fast', '--notchwin-settings', "`"$(Join-Path $d 'sin-notchwin.json')`"", '--config', "`"$(Join-Path $d 'winmac.json')`"", '--log', "`"$log`"") -PassThru -WindowStyle Hidden; $null = $p.Handle
  [pscustomobject]@{ Dir = $d; Log = $log; Proc = $p }
}
function QuitHost($r) { Start-Process -FilePath $hostExe -ArgumentList '--quit' -Wait -WindowStyle Hidden | Out-Null; [void]$r.Proc.WaitForExit(45000) }

Cleanup
# 3a. NotchWin REAL junto a la Barra y el Dock reales (la Isla va con sus ajustes por defecto)
$r = RunHost 'real' $null
$up = WaitFor { (LogHas $r.Log '[bar] Running') -and (LogHas $r.Log '[dock] Running') } 60
Check 'Barra y Dock REALES llegan a Running con la Isla habilitada' $up
$notchRunning = WaitFor { LogHas $r.Log '[notch] Running' } 40
Info 'NotchWin REAL (WinUI 3) llega a Running bajo el host y se enlaza' $notchRunning ("estados: " + ((Select-String -Path $r.Log -Pattern '\[notch\]' -ErrorAction SilentlyContinue | ForEach-Object { $_.Line -replace '^\S+ \S+ ', '' } | Select-Object -First 8) -join ' | '))
if ($notchRunning) { Info 'NotchWin REAL sigue vivo tras unos segundos' (WaitFor { (Get-Process -Name 'NotchWin.App' -ErrorAction SilentlyContinue).Count -ge 1 } 5) }
Check 'Aunque la Isla falle o se reinicie, la Barra y el Dock no se reinician' ((Select-String -Path $r.Log -Pattern '\[(bar|dock)\] (Crashed|Failed)' -ErrorAction SilentlyContinue).Count -eq 0)
QuitHost $r
Check 'Cierre ordenado: el host termina' $r.Proc.HasExited
Check 'Cierre ordenado: no queda ningún proceso (host, Barra, Dock, Isla)' (WaitFor { (Alive) -eq 0 } 15) ("quedan: $(Alive)")
if ($script:fails -gt 0 -or -not $notchRunning) { Get-Content $r.Log -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  host.log> $_" } }
Cleanup

# 3b. Isla «no instalada» (ejecutable inexistente): WinMac sigue y lo dice
$r2 = RunHost 'noinst' @{ enabled = $true; path = (Join-Path ([IO.Path]::GetTempPath()) 'no-existe\NotchWin.App.exe') }
Check 'Con la Isla no instalada la Barra y el Dock arrancan igual' (WaitFor { (LogHas $r2.Log '[bar] Running') -and (LogHas $r2.Log '[dock] Running') } 60)
Check 'La Isla figura como no instalada (no como fallo)' ((LogHas $r2.Log 'notch=NotInstalled') -and -not (LogHas $r2.Log '[notch] Failed'))
QuitHost $r2; Check 'Cierre limpio sin la Isla' (WaitFor { (Alive) -eq 0 } 15); Cleanup

$md = "### Integración con NotchWin`n`n| Prueba | Resultado | Detalle |`n|---|---|---|`n" + (($script:rows | ForEach-Object { "| $($_.Prueba) | $($_.Resultado) | $($_.Detalle) |" }) -join "`n")
$md | Set-Content (Join-Path $OutDir 'notch-tests.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $md }
if ($script:fails) { Write-Host "::error title=NotchWin::$($script:fails) comprobación(es) fallaron"; exit 1 }
Write-Host 'Integración con NotchWin: todas las comprobaciones obligatorias pasaron.'; exit 0
