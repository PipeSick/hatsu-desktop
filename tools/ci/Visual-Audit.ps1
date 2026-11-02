<#
  Auditoría visual en el runner de Windows (docs/24): WinMac COMPLETO (host + Barra + Dock + Isla REALES, de dist/WinMac) y capturas de la PANTALLA REAL
  (Graphics.CopyFromScreen) de cada estado. No se dibuja nada: los estados se provocan por los caminos reales:
    · Isla: WINMAC_PILL_SELFTEST → la Barra real envía volumen/silencio/brillo/notificación por el canal del host; la Isla real los dibuja.
      Las capturas se disparan al ver en su registro que la Isla ya muestra ese estado. Expandida: clic del ratón sobre la Isla.
    · Global Menu: el Bloc de notas real; WINMAC_MENU_DIAG da la posición de las etiquetas y de las filas para pulsar con el ratón.
    · Launchpad / Spotlight: «WinMac.Bar.App --launchpad/--spotlight» (la misma función que la tecla y el atajo) y teclado real (SendKeys).
  Salida: PNG individuales, mosaico.png, informe-automatico.md (tiempos, tamaños de ventana, líneas de registro, limitaciones del runner) y, si existe,
  docs/24-auditoria-visual.md (revisión hecha a mano de las imágenes). Las imágenes se repiten en el registro (AUDIT_PNG|…) para poder revisarlas sin descargar artefactos.
  Informativa: nunca falla el CI.
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'auditoria')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path; $DistDir = (Resolve-Path $DistDir).Path
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Au {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RC { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] struct IN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MI mi; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int size);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  public struct PT { public int X, Y; }
  /// Movimiento por la ruta de entrada de Windows (como un ratón físico), en pasos, de donde esté el puntero a (x, y).
  public static void Glide(int x, int y, int steps) {
    PT p; GetCursorPos(out p); int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    for (int k = 1; k <= steps; k++) {
      int nx = p.X + (x - p.X) * k / steps, ny = p.Y + (y - p.Y) * k / steps;
      var i = new IN[1]; i[0].type = 0; i[0].mi.dx = nx * 65535 / (w - 1); i[0].mi.dy = ny * 65535 / (h - 1); i[0].mi.flags = 0x8001; // MOVE | ABSOLUTE
      SendInput(1, i, Marshal.SizeOf(typeof(IN))); System.Threading.Thread.Sleep(15);
    }
  }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  public static bool Visible(string cls) { var h = FindWindow(cls, null); return h != IntPtr.Zero && IsWindowVisible(h); }
  public static string RectOf(string cls) { var h = FindWindow(cls, null); if (h == IntPtr.Zero) return "(no existe)"; RC r; GetWindowRect(h, out r); return r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T); }
  /// Ventanas visibles de un proceso: clase y rectángulo (para medir la Isla).
  public static string WindowsOf(int pid) {
    var sb = new StringBuilder();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); var c = new StringBuilder(64); GetClassName(h, c, 64); sb.Append(c + " " + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + "; "); } return true; }, IntPtr.Zero);
    return sb.ToString();
  }
}
"@
$sc = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$report = New-Object System.Collections.Generic.List[string]
$shots = New-Object System.Collections.Generic.List[object]
function Note([string]$s) { Write-Host $s; $report.Add($s) }
function WaitFor([scriptblock]$cond, [int]$sec = 20) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 100 }; return [bool](& $cond) }
function LogHas($log, [string]$t) { (Test-Path $log) -and (Select-String -Path $log -SimpleMatch -Pattern $t -Quiet) }
function NotchRects { (Get-Process -Name 'NotchWin.App' -ErrorAction SilentlyContinue | ForEach-Object { [Au]::WindowsOf($_.Id) }) -join ' ' }
function Shot([string]$name, [string]$what, [int]$x = $sc.X, [int]$y = $sc.Y, [int]$w = $sc.Width, [int]$h = $sc.Height) {
  $x = [Math]::Max($sc.X, $x); $y = [Math]::Max($sc.Y, $y); $w = [Math]::Min($w, $sc.Right - $x); $h = [Math]::Min($h, $sc.Bottom - $y)
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h), [System.Drawing.CopyPixelOperation]::SourceCopy); $g.Dispose()
  $path = Join-Path $OutDir "$name.png"; $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  $shots.Add([pscustomobject]@{ Name = $name; What = $what; Path = $path }) | Out-Null
  Note ("- **{0}.png** ({1}x{2} en {3},{4}): {5}" -f $name, $w, $h, $x, $y, $what)
}
$cx = $sc.X + [int]($sc.Width / 2)
function IslaShot([string]$name, [string]$what) { Shot $name $what ($cx - 350) $sc.Y 700 120; Note "  - ventanas de la Isla: $(NotchRects)" }

Note "# Auditoría visual automática (runner de Windows)"
Note ""
Note "Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') · commit $env:GITHUB_SHA"
Note "Pantalla principal: $($sc.Width)x$($sc.Height) · DPI del sistema: $([System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero).DpiX)"
Note ""
Note "## Capturas"

Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App', 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$d = Join-Path ([IO.Path]::GetTempPath()) ('winmac-audit-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)); New-Item -ItemType Directory -Force -Path $d | Out-Null
@{ modules = @{ bar = @{ enabled = $true; args = @('--no-config', '--no-reserve') }; dock = @{ enabled = $true; args = @('--no-config') } }; bar = @{ appMenus = $true } } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d 'winmac.json') -Encoding UTF8
$pillLog = Join-Path $OutDir 'isla-registro.log'; Remove-Item $pillLog -ErrorAction SilentlyContinue
$barLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'; Remove-Item $barLog -ErrorAction SilentlyContinue
$env:WINMAC_PILL_SELFTEST = $pillLog; $env:WINMAC_MENU_DIAG = '1'
$hostLog = Join-Path $d 'host.log'; $hostExe = Join-Path $DistDir 'WinMac.exe'; $bar = Join-Path $DistDir 'WinMac.Bar.App.exe'
$hp = Start-Process -FilePath $hostExe -ArgumentList @('--no-tray', '--fast', '--notchwin-settings', "`"$(Join-Path $d 'sin.json')`"", '--config', "`"$(Join-Path $d 'winmac.json')`"", '--log', "`"$hostLog`"") -PassThru -WindowStyle Hidden
Remove-Item Env:WINMAC_PILL_SELFTEST; Remove-Item Env:WINMAC_MENU_DIAG
$t0 = Get-Date
$up = WaitFor { (LogHas $hostLog '[notch] Running') -and (LogHas $hostLog '[bar] Running') } 60
Note "  (host, Barra e Isla en marcha: $up, $([int]((Get-Date) - $t0).TotalSeconds) s)"
[Au]::SetCursorPos($sc.X + 60, $sc.Y + [int]($sc.Height / 2)) | Out-Null

# ── 1. Isla (la secuencia de muestra empieza ~12 s después de arrancar la Barra)
Start-Sleep -Seconds 5
IslaShot '01-isla-cerrada' 'Isla cerrada, estado normal (sin música: píldora en reposo), junto a la barra'
Shot '01b-barra-e-isla' 'Pantalla completa: barra superior + Isla + Dock'
function OnPill([string]$t, [int]$sec, [string]$name, [string]$what, [int]$delay = 450) {
  if (WaitFor { LogHas $pillLog $t } $sec) { Start-Sleep -Milliseconds $delay; IslaShot $name $what; Note "  - registro de la Isla: $((Select-String -Path $pillLog -SimpleMatch $t | Select-Object -First 1).Line)" }
  else { Note "- **$name**: NO capturada (la Isla no llegó a mostrar «$t» en $sec s)" }
}
OnPill 'level=65 muted=False' 30 '02-isla-volumen-65' 'Indicador de volumen al 65 % (llegó 40 → 45 → 65 por el canal real)'
OnPill 'muted=True' 8 '03-isla-silencio' 'Indicador de silencio'
OnPill 'pill Brightness level=80' 8 '04-isla-brillo-80' 'Indicador de brillo al 80 %'
OnPill 'pill Notification' 8 '05-isla-notificacion' 'Notificación con icono, título y mensaje (la píldora crece)' 650
if (Test-Path (Join-Path $OutDir '05-isla-notificacion.png')) { Shot '05b-barra-e-isla-notificacion' 'Pantalla completa con la notificación en la Isla' }
$back = WaitFor { (Test-Path $pillLog) -and ((Select-String -Path $pillLog -SimpleMatch 'pill Notification').Count -ge 2) -and ((Get-Content $pillLog | Select-Object -Last 1) -match 'pill none$') } 25
if ($back) { Start-Sleep -Milliseconds 900; IslaShot '06-isla-regreso' 'Regreso al estado anterior tras los eventos temporales (volumen interrumpió la notificación y ésta volvió)' } else { Note '- **06-isla-regreso**: NO capturada' }
[Au]::Click($cx, $sc.Y + 12); Start-Sleep -Milliseconds 1200
Shot '07-isla-expandida' 'Isla expandida (clic del ratón sobre ella)' ($cx - 450) $sc.Y 900 460
Note "  - ventanas de la Isla: $(NotchRects)"
# P2: ¿se contrae al salir el ratón? Se sale por la RUTA DE ENTRADA de Windows (SendInput, en pasos, como un ratón físico), no con SetCursorPos.
function NotchW { if ((NotchRects) -match ' (\d+)x(\d+);') { [int]$Matches[1] } else { 0 } }
$wIn = NotchW
[Au]::Glide($cx, $sc.Y + 150, 10); [Au]::Glide($sc.X + 60, $sc.Y + [int]($sc.Height * 0.6), 25)
$collapsed = WaitFor { (NotchW) -lt $wIn -and (NotchW) -gt 0 } 4
Note "  - P2: ancho de la Isla expandida $wIn px; tras salir el ratón por SendInput: $(NotchW) px → $(if ($collapsed) { 'se contrajo (comportamiento correcto)' } else { 'NO se contrajo' })"
Shot '07b-isla-tras-salir-raton' "Isla después de sacar el ratón con SendInput ($(if ($collapsed) { 'contraída' } else { 'sigue expandida' }))" ($cx - 450) $sc.Y 900 460
if (-not $collapsed) {
  [Au]::SetCursorPos($sc.X + 60, $sc.Y + [int]($sc.Height * 0.6)) | Out-Null; $c2 = WaitFor { (NotchW) -lt $wIn -and (NotchW) -gt 0 } 3
  Note "  - P2: con SetCursorPos además: $(if ($c2) { 'se contrajo' } else { 'tampoco' })"
  if (-not $c2) { [Au]::Click($cx, $sc.Y + 12); Start-Sleep -Milliseconds 1200; Note "  - P2: cerrada con otro clic: $(NotchW) px" }
}
[Au]::Glide($sc.X + 60, $sc.Y + [int]($sc.Height * 0.6), 2); Start-Sleep -Milliseconds 800
if (Test-Path $pillLog) { Note ''; Note '### Registro de la Isla (lo que mostró y el tamaño pedido a su ventana, DIP)'; Note '```'; Get-Content $pillLog | ForEach-Object { Note $_ }; Note '```' }

# ── 2. Global Menu con el Bloc de notas real
Note ''
$np = Start-Process notepad.exe -PassThru
$menusOk = WaitFor { (LogHas $barLog '[menús] diag') -and ((Select-String -Path $barLog -SimpleMatch '[menús] diag' | Select-Object -Last 1).Line -match 'AppMenu:') } 20
Start-Sleep -Milliseconds 600
Shot '08-menu-barra' 'Barra superior con el Bloc de notas activo y sus menús' $sc.X $sc.Y $sc.Width 70
Shot '08b-menu-pantalla' 'Pantalla completa: barra con los menús del Bloc de notas y su ventana (menú original intacto)'
function MenuDiagLine { if (Test-Path $barLog) { (Select-String -Path $barLog -SimpleMatch '[menús] diag' | Select-Object -Last 1).Line } else { '' } }
$labels = @(); if ($menusOk) { $labels = [regex]::Matches((MenuDiagLine), 'AppMenu:([^@|]+)@(\d+),(\d+)') | ForEach-Object { [pscustomobject]@{ Text = $_.Groups[1].Value.Trim(); X = [int]$_.Groups[2].Value; Y = [int]$_.Groups[3].Value } } }
Note "  - etiquetas de menú en la barra: $(($labels | ForEach-Object { "$($_.Text) ($($_.X),$($_.Y))" }) -join ', ')"
$subDone = $false; $i = 0
foreach ($lb in $labels) {
  $i++
  # La posición se relee justo antes de cada clic (si la barra cambia el nombre de la app, las etiquetas se desplazan) y el Bloc de notas vuelve al frente.
  if ($np.MainWindowHandle -ne [IntPtr]::Zero) { [Au]::SetForegroundWindow($np.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 700 }
  $cur = [regex]::Matches((MenuDiagLine), 'AppMenu:([^@|]+)@(\d+),(\d+)') | Where-Object { $_.Groups[1].Value.Trim() -eq $lb.Text } | Select-Object -Last 1
  if ($cur) { $lb = [pscustomobject]@{ Text = $lb.Text; X = [int]$cur.Groups[2].Value; Y = [int]$cur.Groups[3].Value } }
  $barPid = (Get-Process -Name 'WinMac.Bar.App' -ErrorAction SilentlyContinue | Select-Object -First 1).Id
  [Au]::Click($lb.X, $lb.Y); Start-Sleep -Milliseconds 900
  $barPid2 = (Get-Process -Name 'WinMac.Bar.App' -ErrorAction SilentlyContinue | Select-Object -First 1).Id
  Note "  - clic en «$($lb.Text)» en ($($lb.X),$($lb.Y)); proceso de la barra antes $barPid, después $barPid2$(if ($barPid -ne $barPid2) { ' → LA BARRA SE REINICIÓ' })"
  $rows = [regex]::Matches((MenuDiagLine), 'fila:([^:]+):([^@|]*)@(\d+),(\d+)') | ForEach-Object { [pscustomobject]@{ Id = $_.Groups[1].Value; Text = $_.Groups[2].Value.Trim(); X = [int]$_.Groups[3].Value; Y = [int]$_.Groups[4].Value } }
  $safe = ($lb.Text -replace '[^\w]', '')
  Shot ("09-menu-{0}-{1}" -f $i, $safe) "Menú «$($lb.Text)» desplegado con el estilo de WinMac" ([Math]::Max($sc.X, $lb.X - 60)) $sc.Y 520 560
  Note "  - filas: $(($rows | ForEach-Object { $_.Text }) -join ' · ')"
  $sub = $rows | Where-Object { $_.Text.EndsWith('›') } | Select-Object -First 1
  if (-not $subDone -and $sub) {
    [Au]::Click($sub.X, $sub.Y); Start-Sleep -Milliseconds 800
    Shot '10-submenu' "Submenú «$($sub.Text.TrimEnd('›').Trim())» abierto dentro del panel (con ‹ para volver)" ([Math]::Max($sc.X, $lb.X - 60)) $sc.Y 520 560
    $subDone = $true
  }
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400
  if ([Au]::GetForegroundWindow() -ne $np.MainWindowHandle -and $np.MainWindowHandle -ne [IntPtr]::Zero) { [Au]::SetForegroundWindow($np.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 500 }
}
if (-not $subDone) { Note '- **10-submenu**: no hay submenús en los menús de esta app (o no se pudieron abrir)' }
Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# ── 3. Launchpad
Note ''
& $bar --launchpad | Out-Null
if (WaitFor { [Au]::Visible('WinMacLaunchpad') } 10) {
  Start-Sleep -Milliseconds 1500   # fundido de entrada y primeros iconos
  Shot '11a-launchpad-1s' "Launchpad 1,5 s después de abrir (iconos que ya estaban)"
  $null = WaitFor { LogHas $barLog 'iconos (3 s después)' } 6; Start-Sleep -Milliseconds 300
  Note "  - $(((Select-String -Path $barLog -SimpleMatch '[launchpad] iconos' | Select-Object -Last 2 | ForEach-Object { $_.Line -replace '^\S+ ', '' }) -join ' / '))"
  Shot '11-launchpad' "Launchpad: página principal (ventana $([Au]::RectOf('WinMacLaunchpad')))"
  Shot '11b-launchpad-iconos' 'Detalle a tamaño real de la cuadrícula (iconos y nombres)' ($sc.X + [int]($sc.Width * 0.08)) ($sc.Y + [int]($sc.Height * 0.18)) 520 300
  Shot '11c-launchpad-busqueda-vacia' 'Detalle del campo de búsqueda' ($cx - 200) $sc.Y 400 140
  [System.Windows.Forms.SendKeys]::SendWait('notepad'); Start-Sleep -Milliseconds 700
  Shot '12-launchpad-notepad' 'Launchpad con la búsqueda «notepad»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 200; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500
} else { Note '- **11-launchpad**: NO capturado (la ventana no apareció)' }

# ── 4. Spotlight
Note ''
function SpotOpen { & $bar --spotlight | Out-Null; return (WaitFor { [Au]::Visible('WinMacSpotlight') } 10) }
function SpotShot([string]$name, [string]$what) { Shot $name "$what (ventana $([Au]::RectOf('WinMacSpotlight')))"; Shot ($name + 'b') "$($what): detalle" ($cx - 400) ($sc.Y + [int]($sc.Height * 0.18)) 800 520 }
if (SpotOpen) {
  Start-Sleep -Milliseconds 500; SpotShot '13-spotlight-inicial' 'Spotlight recién abierto'
  [System.Windows.Forms.SendKeys]::SendWait('notepad'); Start-Sleep -Milliseconds 900; SpotShot '14-spotlight-app' 'Spotlight buscando «notepad»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('12*12'); Start-Sleep -Milliseconds 700; SpotShot '15-spotlight-12x12' 'Spotlight con «12*12»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('con'); Start-Sleep -Milliseconds 1200; SpotShot '16-spotlight-resultados' 'Spotlight con «con»: varias secciones de resultados'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 200; [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
} else { Note '- **13-spotlight**: NO capturado (la ventana no apareció)' }
Start-Sleep -Milliseconds 600

Start-Process -FilePath $hostExe -ArgumentList '--quit' -Wait -WindowStyle Hidden | Out-Null; [void]$hp.WaitForExit(30000)
Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App', 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ── Registros útiles
Note ''; Note '## Líneas de registro relevantes'; Note '```'
if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[(isla|launchpad|spotlight|menús)\]' | Select-Object -Last 40 | ForEach-Object { Note ($_.Line -replace '^\S+ ', '') } }
if (Test-Path $hostLog) { Select-String -Path $hostLog -Pattern '\[(notch|bar|dock)\] (Crashed|Failed|sin latido|terminó)' | ForEach-Object { Note $_.Line } }
if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[panel\]|\[menús\] (clic|abrir|«)|CIERRE INESPERADO' -Context 0, 12 | Select-Object -Last 30 | ForEach-Object { Note $_.Line; if ($_.Line -match 'CIERRE') { $_.Context.PostContext | ForEach-Object { Note "    $_" } } } }
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime', 'Application Error'; StartTime = $t0 } -MaxEvents 6 -ErrorAction SilentlyContinue |
  Where-Object { $_.Message -match 'WinMac' } | ForEach-Object { $m = $_.Message; if ($m.Length -gt 3000) { $m = $m.Substring(0, 3000) }; Note "evento $($_.ProviderName): $m" }
Note '```'

Note ''; Note '## Limitaciones de captura del runner'
Note "- Un solo monitor virtual de $($sc.Width)x$($sc.Height) al 100 % de escala: no hay pruebas de varias pantallas, DPI alto ni HDR."
Note '- Sin salida de audio, sin panel interno con brillo y sin permiso de notificaciones de Windows: volumen, brillo y notificación de la Isla son la secuencia de muestra (WINMAC_PILL_SELFTEST) por el canal real Barra → host → Isla, no la detección real.'
Note '- Sin GPU real (render por software/WARP): las animaciones se capturan en un instante; no se mide su fluidez.'
Note '- Fondo de pantalla del runner (no el del usuario): el vidrio desenfocado depende de él.'
Note '- Windows Search no responde en el runner: Spotlight solo muestra recientes y carpetas, no el índice.'
Note '- La tecla Windows y Alt+Espacio físicos no se pueden pulsar (SendInput se marca como inyectado y el gancho lo ignora a propósito): se abren con --launchpad / --spotlight.'

# ── Mosaico
$cols = 3; $tw = 420; $pad = 14; $cap = 34
$items = $shots | ForEach-Object { $img = [System.Drawing.Image]::FromFile($_.Path); $th = [int]($img.Height * $tw / $img.Width); [pscustomobject]@{ S = $_; Img = $img; H = [Math]::Min($th, 420) } }
$rowsH = @(); for ($r = 0; $r -lt [Math]::Ceiling($items.Count / $cols); $r++) { $rowsH += (($items | Select-Object -Skip ($r * $cols) -First $cols | Measure-Object -Property H -Maximum).Maximum + $cap + $pad) }
$W = $cols * ($tw + $pad) + $pad; $H = ($rowsH | Measure-Object -Sum).Sum + $pad
$m = New-Object System.Drawing.Bitmap $W, $H; $g = [System.Drawing.Graphics]::FromImage($m); $g.Clear([System.Drawing.Color]::FromArgb(30, 30, 32)); $g.InterpolationMode = 'HighQualityBicubic'
$font = New-Object System.Drawing.Font 'Segoe UI', 9; $y = $pad
for ($k = 0; $k -lt $items.Count; $k++) {
  $r = [Math]::Floor($k / $cols); $c = $k % $cols; if ($c -eq 0 -and $k -gt 0) { $y += $rowsH[$r - 1] }
  $it = $items[$k]; $x = $pad + $c * ($tw + $pad)
  $g.DrawString($it.S.Name, $font, [System.Drawing.Brushes]::White, $x, $y + 2)
  $g.DrawImage($it.Img, $x, $y + $cap - 14, $tw, $it.H)
}
$g.Dispose(); $m.Save((Join-Path $OutDir 'mosaico.png'), [System.Drawing.Imaging.ImageFormat]::Png); $m.Dispose(); $items | ForEach-Object { $_.Img.Dispose() }
Note ''; Note "Mosaico: mosaico.png ($($shots.Count) capturas)"
$review = Join-Path $PSScriptRoot '..\..\docs\24-auditoria-visual.md'
if (Test-Path $review) { Copy-Item $review (Join-Path $OutDir 'informe-revision-visual.md') }
$report | Set-Content (Join-Path $OutDir 'informe-automatico.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY ($report -join "`n") }

# ── Las imágenes también al registro (para revisarlas sin descargar el artefacto)
foreach ($f in (Get-ChildItem $OutDir -Filter '*.png' | Sort-Object Name)) {
  $b = [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))
  for ($o = 0; $o -lt $b.Length; $o += 16000) { Write-Host ("AUDIT_PNG|{0}|{1}" -f $f.Name, $b.Substring($o, [Math]::Min(16000, $b.Length - $o))) }
}
Write-Host 'Auditoría visual terminada.'; exit 0
