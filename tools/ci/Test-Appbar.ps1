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

# ── EXPERIMENTO DE CONTROL Y LABORATORIO: una barra de aplicación mínima que sigue el protocolo documentado (ABM_NEW → QUERYPOS → SETPOS → mover la ventana), y variantes que añaden UNA a UNA las
#    características de la Barra real. Si el control (V0) cambia el área de trabajo y la Barra no, es un defecto del producto; la primera variante que falle señala la causa.
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class AppBarControl {
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct ABD { public uint cb; public IntPtr hWnd; public uint cbMsg; public uint edge; public RECT rc; public IntPtr lParam; }
  [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m, ref ABD d);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
  [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
  [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint a, uint b, ref RECT r, uint c);
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  static int WorkTop() { RECT r = new RECT(); SystemParametersInfo(0x30, 0, ref r, 0); return r.T; }
  /// ex: estilos extendidos; visibleAtAbm: la ventana ya es visible al registrar; tiny: nace de 100x30 y se coloca con SetWindowPos (como la Barra); setPos: usar SetWindowPos(TOPMOST|SHOWWINDOW|NOACTIVATE) en vez de MoveWindow
  public static string Run(int height, uint ex, bool visibleAtAbm, bool tiny, bool setPos) {
    SetProcessDPIAware(); int before = WorkTop(); int w = GetSystemMetrics(0);
    uint style = 0x80000000 | (visibleAtAbm ? 0x10000000u : 0u);
    IntPtr h = CreateWindowEx(ex, "Static", "control-appbar", style, 0, 0, tiny ? 100 : w, tiny ? 30 : height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    if (h == IntPtr.Zero) return "ventana=falló";
    ABD d = new ABD(); d.cb = (uint)Marshal.SizeOf(typeof(ABD)); d.hWnd = h; d.cbMsg = 0x8000 + 77;
    bool nw = SHAppBarMessage(0, ref d) != UIntPtr.Zero;
    d.edge = 1; d.rc = new RECT { L = 0, T = 0, R = w, B = height }; SHAppBarMessage(2, ref d); d.rc.B = d.rc.T + height; SHAppBarMessage(3, ref d);
    if (setPos) SetWindowPos(h, new IntPtr(-1), d.rc.L, d.rc.T, d.rc.R - d.rc.L, d.rc.B - d.rc.T, 0x0010 | 0x0040); else MoveWindow(h, d.rc.L, d.rc.T, d.rc.R - d.rc.L, d.rc.B - d.rc.T, true);
    int during = before; for (int i = 0; i < 12 && during == before; i++) { System.Threading.Thread.Sleep(250); during = WorkTop(); }
    SHAppBarMessage(1, ref d); DestroyWindow(h); System.Threading.Thread.Sleep(1500); int after = WorkTop();
    return "ABM_NEW=" + nw + ";antes=" + before + ";durante=" + during + ";despues=" + after;
  }
}
'@
$TOOL = 0x80; $TOP = 0x8; $NOACT = 0x08000000; $NOREDIR = 0x00200000
$variants = @(
  @('V0 control: visible al registrar, MoveWindow', ($TOOL -bor $TOP), $true, $false, $false),
  @('V1 oculta al registrar (se muestra después)', ($TOOL -bor $TOP), $false, $false, $false),
  @('V2 + WS_EX_NOACTIVATE', ($TOOL -bor $TOP -bor $NOACT), $true, $false, $false),
  @('V3 + WS_EX_NOREDIRECTIONBITMAP', ($TOOL -bor $TOP -bor $NOREDIR), $true, $false, $false),
  @('V4 nace de 100x30 y se coloca con SetWindowPos', ($TOOL -bor $TOP), $true, $true, $true),
  @('V5 como la Barra: oculta + NOACTIVATE + NOREDIRECTIONBITMAP + 100x30 + SetWindowPos', ($TOOL -bor $TOP -bor $NOACT -bor $NOREDIR), $false, $true, $true))
$lab = @(); foreach ($v in $variants) { $r = [AppBarControl]::Run(28, [uint32]$v[1], $v[2], $v[3], $v[4]); $ok = ($r -match 'durante=(\d+)' -and [int]$Matches[1] -gt $before); $lab += [pscustomobject]@{ Variante = $v[0]; Reserva = $ok; Detalle = $r }; Write-Host ("LAB  {0}: {1}  [{2}]" -f $v[0], $(if ($ok) { 'RESERVA' } else { 'NO reserva' }), $r) }
$ctl = $lab[0].Detalle; $controlWorks = $lab[0].Reserva
$script:rep += "| INFO | control V0 (AppBar mínimo según el protocolo documentado) | $ctl |"; foreach ($l in ($lab | Select-Object -Skip 1)) { $script:rep += "| INFO | laboratorio · $($l.Variante) | $(if ($l.Reserva) { 'reserva' } else { 'NO reserva' }) — $($l.Detalle) |" }
# 0) EXPERIMENTO: la misma Barra con la bandeja desactivada (sin su ventana «Shell_TrayWnd» propia). Si ESTA reserva y la normal no, la ventana-proxy de bandeja estorba a los mensajes de AppBar.
$noTrayCfg = Join-Path $OutDir 'bar-sin-bandeja.json'; '{"Version":1,"ShowTrayIcons":false}' | Set-Content $noTrayCfg -Encoding UTF8
$pt = Start-Process $exe -ArgumentList @('--config', "`"$noTrayCfg`"") -PassThru -WindowStyle Hidden; $noTrayReserved = $false; for ($i = 0; $i -lt 30; $i++) { Start-Sleep -Milliseconds 500; if ((Top) -gt $before) { $noTrayReserved = $true; break } }
Write-Host ("EXPERIMENTO bandeja desactivada: reserva={0} (y durante={1})" -f $noTrayReserved, [WorkArea]::Get()); $script:rep += "| INFO | experimento: Barra con ShowTrayIcons=false | reserva=$noTrayReserved |"
$null = $pt.CloseMainWindow(); if (-not $pt.WaitForExit(8000)) { $pt | Stop-Process -Force }; Get-Process -Name 'HatsuDesktop.Bar' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 3
# 1) arranque con reserva (valores de fábrica): el borde superior sale del área de trabajo
$p = Start-Process $exe -ArgumentList @('--no-config') -PassThru -WindowStyle Hidden; $reserved = $false; for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; if ((Top) -gt $before) { $reserved = $true; break } }
$during = [WorkArea]::Get()
if (-not $reserved) {   # sin reserva: se reúne la evidencia antes de concluir nada (¿murió la Barra? ¿ABM_NEW falló? ¿el runner no tiene un shell que acepte barras de aplicación?)
  $barLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'; Write-Host ("Barra: terminó={0}{1}" -f $p.HasExited, $(if ($p.HasExited) { " código=$($p.ExitCode)" } else { '' }))
  $tail = if (Test-Path $barLog) { @(Get-Content $barLog -Tail 40) } else { @() }; $tail | ForEach-Object { Write-Host "    bar.log: $_" }
  Write-Host ("Procesos explorer: {0} · sesión interactiva: {1}" -f @(Get-Process explorer -ErrorAction SilentlyContinue).Count, [Environment]::UserInteractive)
  if (-not $controlWorks) {
    Write-Host 'INFO  El shell de este runner NO acepta barras de aplicación ni siquiera con el control mínimo: la prueba de la Barra NO es concluyente aquí. Se valida en un Windows físico (docs/49 §10).'
    ($head = "# Reserva del borde superior en el runner de CI`n`n**No concluyente:** este runner no cambia el área de trabajo ni con una barra de aplicación mínima que sigue el protocolo documentado (control). Hay que probarlo en un Windows físico.`n") | Out-Null
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
