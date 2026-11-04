<#
.SYNOPSIS
  Comprueba, en el runner de Windows, que la Barra superior RESERVA de verdad el borde superior (área de trabajo de Windows) y que lo DEVUELVE al cerrarse, también tras un cierre brusco.
.DESCRIPTION
  Usa la API de barras de aplicación (SHAppBarMessage) que ya usa el producto; aquí solo se LEE el área de trabajo (SPI_GETWORKAREA). No mueve iconos ni cambia el escritorio de forma persistente
  (la Barra se cierra al terminar). Entorno de CI: NO prueba dos monitores, escalas DPI mixtas, reinicio de Explorer ni la recolocación de los iconos del escritorio en un PC real.
#>
param([string]$BarExe = 'dist\WinMac\HatsuDesktop.Bar.exe', [string]$OutDir = 'appbar-test')
$ErrorActionPreference = 'Stop'; New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class WorkArea { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, ref R r, uint c); [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  public static string Get() { SetProcessDPIAware(); R r = new R(); SystemParametersInfo(0x30, 0, ref r, 0); return r.L + "," + r.T + "," + r.Rt + "," + r.B; } }
'@
function Top { [int](([WorkArea]::Get()).Split(',')[1]) }
$fails = 0; $rep = @(); function Check([string]$n, $ok, [string]$d = '') { $ok = [bool](@($ok) | Select-Object -Last 1); $script:rep += "| $(if ($ok) { 'OK' } else { 'FALLO' }) | $n | $d |"; Write-Host ("{0}  {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $n, $d); if (-not $ok) { $script:fails++ } }
$exe = (Resolve-Path $BarExe).Path; $before = Top; Write-Host "Área de trabajo antes: $([WorkArea]::Get())"
Get-Process -Name 'HatsuDesktop.Bar' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1
# 1) arranque con reserva (valores de fábrica): el borde superior sale del área de trabajo
$p = Start-Process $exe -ArgumentList @('--no-config') -PassThru -WindowStyle Hidden; $reserved = $false; for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; if ((Top) -gt $before) { $reserved = $true; break } }
$during = [WorkArea]::Get()
if (-not $reserved) {   # sin reserva: se reúne la evidencia antes de concluir nada (¿murió la Barra? ¿ABM_NEW falló? ¿el runner no tiene un shell que acepte barras de aplicación?)
  $barLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'; Write-Host ("Barra: terminó={0}{1}" -f $p.HasExited, $(if ($p.HasExited) { " código=$($p.ExitCode)" } else { '' }))
  $tail = if (Test-Path $barLog) { @(Get-Content $barLog -Tail 40) } else { @() }; $tail | ForEach-Object { Write-Host "    bar.log: $_" }
  Write-Host ("Procesos explorer: {0} · sesión interactiva: {1}" -f @(Get-Process explorer -ErrorAction SilentlyContinue).Count, [Environment]::UserInteractive)
  if (@($tail | Where-Object { $_ -match 'ABM_NEW falló' }).Count -gt 0) {
    Write-Host 'INFO  El shell de este runner NO acepta barras de aplicación (ABM_NEW falló): la prueba NO es concluyente aquí. Se valida en un Windows físico (docs/49 §10).'
    ($head = "# Reserva del borde superior en el runner de CI`n`n**No concluyente:** este runner no acepta barras de aplicación (ABM_NEW falló). Hay que probarlo en un Windows físico.`n") | Out-Null
    Set-Content (Join-Path $OutDir 'appbar-report.md') -Value ($head + "`n" + (($tail | ForEach-Object { '    ' + $_ }) -join "`n")) -Encoding UTF8; if (-not $p.HasExited) { $p | Stop-Process -Force }; exit 0 } }
Check 'la Barra reserva el borde superior: el área de trabajo de Windows baja' $reserved "antes y=$before · durante: $during"
# 2) cierre ordenado: se devuelve exactamente lo que había
$null = $p.CloseMainWindow(); if (-not $p.WaitForExit(8000)) { $p | Stop-Process -Force }; Start-Sleep 2
$restored = Top; if ($restored -ne $before) { Start-Sleep 3; $restored = Top }
Check 'al cerrarse (ordenado) el área de trabajo vuelve a como estaba' ($restored -eq $before) "antes y=$before · después y=$restored"
# 3) cierre brusco (proceso muerto): Windows debe liberar la reserva sin que la Barra pueda hacerlo
$p2 = Start-Process $exe -ArgumentList @('--no-config') -PassThru -WindowStyle Hidden; $r2 = $false; for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; if ((Top) -gt $before) { $r2 = $true; break } }
Check 'segundo arranque: vuelve a reservar' $r2; $p2 | Stop-Process -Force; Start-Sleep 3; $afterKill = Top
Check 'tras un cierre brusco (proceso terminado) Windows libera la reserva' ($afterKill -eq $before) "antes y=$before · tras terminar el proceso y=$afterKill"
# 4) con la reserva desactivada la Barra NO toca el área de trabajo
$p3 = Start-Process $exe -ArgumentList @('--no-config', '--no-reserve') -PassThru -WindowStyle Hidden; Start-Sleep 5; $noRes = Top
Check 'con «no reservar» el área de trabajo no cambia' ($noRes -eq $before) "y=$noRes"; $null = $p3.CloseMainWindow(); if (-not $p3.WaitForExit(8000)) { $p3 | Stop-Process -Force }
Get-Process -Name 'HatsuDesktop.Bar' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
Check 'al final el área de trabajo es la original' ((Top) -eq $before) "y=$(Top)"
$head = "# Reserva del borde superior en el runner de CI`n`n**Entorno efímero (1 monitor, sin DPI mixto, sin reinicio de Explorer): NO sustituye la prueba en tu PC. No comprueba la posición de los iconos del escritorio.**`n`n| Resultado | Comprobación | Detalle |`n|---|---|---|`n"
($head + ($rep -join "`n")) | Set-Content (Join-Path $OutDir 'appbar-report.md') -Encoding UTF8
if ($fails -gt 0) { Write-Host "::error title=AppBar::$fails comprobación(es) de la reserva del borde superior fallaron"; exit 1 }
