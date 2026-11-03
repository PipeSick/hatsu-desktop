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
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class BarRgn {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] static extern int GetWindowRgnBox(IntPtr h, out RC r);
  public struct RC { public int L, T, R, B; }
  /// 0 = la barra no existe o su ventana está completa (sin región); 1 = NULLREGION; 2 = región simple; 3 = región compleja (con huecos)
  public static int Kind() { var h = FindWindow("WinMacBar", null); if (h == IntPtr.Zero) return -1; RC r; int k = GetWindowRgnBox(h, out r); return k; }
}
"@
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
$env:WINMAC_PILL_SELFTEST = Join-Path $OutDir 'pill-selftest.log'   # SOLO CI: la Barra envía una secuencia de muestra a la Isla y la Isla anota lo que muestra (docs/21)
Remove-Item $env:WINMAC_PILL_SELFTEST -ErrorAction SilentlyContinue
$r = RunHost 'real' $null
Remove-Item Env:WINMAC_PILL_SELFTEST
$up = WaitFor { (LogHas $r.Log '[bar] Running') -and (LogHas $r.Log '[dock] Running') } 60
Check 'Barra y Dock REALES llegan a Running con la Isla habilitada' $up
$notchRunning = WaitFor { LogHas $r.Log '[notch] Running' } 40
Info 'NotchWin REAL (WinUI 3) llega a Running bajo el host y se enlaza' $notchRunning ("estados: " + ((Select-String -Path $r.Log -Pattern '\[notch\]' -ErrorAction SilentlyContinue | ForEach-Object { $_.Line -replace '^\S+ \S+ ', '' } | Select-Object -First 8) -join ' | '))
if ($notchRunning) { Info 'NotchWin REAL sigue vivo tras unos segundos' (WaitFor { (Get-Process -Name 'NotchWin.App' -ErrorAction SilentlyContinue).Count -ge 1 } 5) }
if ($notchRunning) {
  # La Isla informativa: volumen, silencio, brillo y una notificación llegan por el canal real (Barra → host → Isla), el árbitro decide y la ventana cambia de tamaño.
  $pillLog = Join-Path $OutDir 'pill-selftest.log'
  $null = WaitFor { LogHas $pillLog 'pill Notification' } 40
  $null = WaitFor { (Test-Path $pillLog) -and ((Get-Content $pillLog | Select-Object -Last 1) -match 'pill none$') -and ((Select-String -Path $pillLog -SimpleMatch 'pill Notification').Count -ge 2) } 25
  $pl = @(if (Test-Path $pillLog) { Get-Content $pillLog } else { @() })
  $pl | ForEach-Object { Write-Host "  isla> $_" }
  function Idx([string]$t) { for ($i = 0; $i -lt $pl.Count; $i++) { if ($pl[$i].Contains($t)) { return $i } }; return -1 }
  $v65 = Idx 'pill Volume level=65 muted=False'; $mute = Idx 'pill Volume level=65 muted=True'; $br = Idx 'pill Brightness level=80'; $n1 = Idx 'pill Notification'; $v30 = Idx 'pill Volume level=30'
  $n2 = -1; for ($i = [Math]::Max(0, $v30 + 1); $v30 -ge 0 -and $i -lt $pl.Count; $i++) { if ($pl[$i].Contains('pill Notification')) { $n2 = $i; break } }
  Check 'Isla: indicador de volumen real en la píldora (40→45→65 en el sitio, 280 DIP)' ($v65 -ge 0 -and $pl[$v65].Contains('size=280x')) ($(if ($v65 -ge 0) { $pl[$v65] }))
  Check 'Isla: estado silenciado' ($mute -gt $v65)
  Check 'Isla: indicador de brillo' ($br -gt $mute)
  Check 'Isla: notificación con icono; la píldora crece (380×52 DIP)' ($n1 -gt $br -and $pl[$n1].Contains('icon=yes') -and $pl[$n1].Contains('size=380x52')) ($(if ($n1 -ge 0) { $pl[$n1] }))
  Check 'Isla: un cambio de volumen interrumpe la notificación y ésta vuelve después (no se pierde)' ($v30 -gt $n1 -and $n2 -gt $v30)
  Check 'Isla: al terminar vuelve al contenido habitual' ($pl.Count -gt 0 -and $pl[-1].EndsWith('pill none'))

  # La Barra recorta su ventana donde la Isla ocupa espacio (sin depender del orden Z); informativo porque depende de que el runner tenga monitor (si no, la Isla no tiene huella).
  $cut = WaitFor { [BarRgn]::Kind() -ge 2 } 15
  Info 'La Barra REAL recorta su ventana en el espacio de la Isla (región de ventana con hueco)' $cut ("tipo de región: $([BarRgn]::Kind()) (3 = con huecos)")
  if ($cut) {
    Get-Process -Name 'NotchWin.App' -ErrorAction SilentlyContinue | Stop-Process -Force   # la Isla «se pierde»
    $freed = WaitFor { [BarRgn]::Kind() -le 0 } 4
    Info 'Al perder la Isla, la Barra recupera su ventana completa (el host libera el espacio)' $freed ("tipo de región: $([BarRgn]::Kind()) (0 = completa)")
  }
}
Check 'Aunque la Isla falle o se reinicie, la Barra y el Dock no se reinician' ((Select-String -Path $r.Log -Pattern '\[(bar|dock)\] (Crashed|Failed)' -ErrorAction SilentlyContinue).Count -eq 0)
QuitHost $r
Check 'Cierre ordenado: el host termina' $r.Proc.HasExited
Check 'Cierre ordenado: no queda ningún proceso (host, Barra, Dock, Isla)' (WaitFor { (Alive) -eq 0 } 15) ("quedan: $(Alive)")
if ($script:fails -gt 0 -or -not $notchRunning) { Get-Content $r.Log -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  host.log> $_" } }
if ($script:fails -gt 0) {
  # Diagnóstico de bloqueos («sin latido»): las últimas líneas de la Barra y de la Isla (sin contenido del usuario)
  $bl = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'; if (Test-Path $bl) { Get-Content $bl -Tail 40 | ForEach-Object { Write-Host "  bar.log> $_" } }
  $cl = Join-Path $env:LOCALAPPDATA 'NotchWin\crash.log'; if (Test-Path $cl) { Get-Content $cl -Tail 30 | ForEach-Object { Write-Host "  notch-crash> $_" } }
}
# Diagnóstico de un cierre de la Isla: su propio registro de excepciones y los eventos de Windows (.NET Runtime / Application Error) de NotchWin.App.
$crash = Join-Path $env:LOCALAPPDATA 'NotchWin\crash.log'
if (Test-Path $crash) { Get-Content $crash -TotalCount 60 | ForEach-Object { Write-Host "  crash.log> $_" } }
if (LogHas $r.Log '[notch] Crashed') {
  Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime', 'Application Error'; StartTime = (Get-Date).AddMinutes(-15) } -MaxEvents 3 -ErrorAction SilentlyContinue |
    ForEach-Object { $m = $_.Message; if ($m.Length -gt 2500) { $m = $m.Substring(0, 2500) }; Write-Host "  evento $($_.ProviderName)> $m" }
}
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
