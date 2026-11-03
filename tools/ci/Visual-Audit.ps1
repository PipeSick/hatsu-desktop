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
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr h, uint flags);
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
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  /// Texto del documento del Bloc de notas clásico (su control Edit), para comprobar que una orden del menú se ejecutó de verdad.
  public static string NotepadText(IntPtr main) {
    var e = FindWindowEx(main, IntPtr.Zero, "Edit", null); if (e == IntPtr.Zero) e = FindWindowEx(main, IntPtr.Zero, "RichEditD2DPT", null); if (e == IntPtr.Zero) return "(sin control de texto)";
    int n = (int)SendMessage(e, 0x0E, IntPtr.Zero, IntPtr.Zero); var sb = new StringBuilder(n + 1); SendMessage(e, 0x0D, (IntPtr)(n + 1), sb); return sb.ToString();
  }
  public static void Down() { mouse_event(2, 0, 0, 0, IntPtr.Zero); }
  public static void Up() { mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(PT p);
  public static string ClassAt(int x, int y) { var p = new PT(); p.X = x; p.Y = y; var h = WindowFromPoint(p); var c = new StringBuilder(64); GetClassName(h, c, 64); return c.ToString(); }
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
# WinMac 2.0 — datos de PRUEBA por caminos reales: PNG reales en la carpeta de capturas de Windows (la galería los lee de disco).
$shotsDir = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Screenshots'; New-Item -ItemType Directory -Force -Path $shotsDir | Out-Null
$dlDir = Join-Path $env:USERPROFILE 'Downloads'; New-Item -ItemType Directory -Force -Path $dlDir | Out-Null
function TestPng([string]$path, [int]$n, [string]$label) {
  $b = New-Object System.Drawing.Bitmap 960, 540; $g = [System.Drawing.Graphics]::FromImage($b)
  $hues = @(@(30, 60, 160), @(20, 110, 190), @(90, 40, 150), @(25, 140, 120), @(170, 80, 40), @(60, 60, 70))
  $c1 = $hues[$n % $hues.Count]; $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 960, 540), ([System.Drawing.Color]::FromArgb(255, $c1[0], $c1[1], $c1[2])), ([System.Drawing.Color]::FromArgb(255, 10, 12, 30))
  $g.FillRectangle($br, 0, 0, 960, 540); $f = New-Object System.Drawing.Font 'Segoe UI', 40; $g.DrawString($label, $f, [System.Drawing.Brushes]::White, 40, 220); $g.Dispose(); $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}
for ($k = 1; $k -le 5; $k++) { $p = Join-Path $shotsDir "captura-de-prueba-$k.png"; TestPng $p $k "Captura de prueba $k"; (Get-Item $p).LastWriteTime = (Get-Date).AddHours(-6 * $k) }
$barLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'; Remove-Item $barLog -ErrorAction SilentlyContinue
$env:WINMAC_PILL_SELFTEST = $pillLog; $env:WINMAC_MENU_DIAG = '1'; $env:WINMAC_QUICKLOOK_IN_CAPTURES = '1'   # Quick Look se excluye de las capturas: solo aquí se deja ver para fotografiarlo (el registro lo dice)
$hostLog = Join-Path $d 'host.log'; $hostExe = Join-Path $DistDir 'WinMac.exe'; $bar = Join-Path $DistDir 'WinMac.Bar.App.exe'
# Extensiones de demostración (docs/30): se «instalan» como lo haría Ajustes (copia a %LOCALAPPDATA%\WinMac\extensions) y se activan las dos
$extRoot = Join-Path $env:LOCALAPPDATA 'WinMac\extensions'
if (Test-Path (Join-Path $DistDir 'extensions')) {
  New-Item -ItemType Directory -Force -Path $extRoot | Out-Null
  foreach ($id in 'pomodoro', 'uptime') { $src = Join-Path $DistDir "extensions\$id"; if (Test-Path $src) { Copy-Item $src $extRoot -Recurse -Force } }
  '["pomodoro","uptime"]' | Set-Content (Join-Path $extRoot 'extensions.json') -Encoding UTF8
}
$hp = Start-Process -FilePath $hostExe -ArgumentList @('--no-tray', '--fast', '--notchwin-settings', "`"$(Join-Path $d 'sin.json')`"", '--config', "`"$(Join-Path $d 'winmac.json')`"", '--log', "`"$hostLog`"") -PassThru -WindowStyle Hidden
Remove-Item Env:WINMAC_PILL_SELFTEST; Remove-Item Env:WINMAC_MENU_DIAG; Remove-Item Env:WINMAC_QUICKLOOK_IN_CAPTURES
$t0 = Get-Date
$up = WaitFor { (LogHas $hostLog '[notch] Running') -and (LogHas $hostLog '[bar] Running') } 60
Note "  (host, Barra e Isla en marcha: $up, $([int]((Get-Date) - $t0).TotalSeconds) s)"
[Au]::SetCursorPos($sc.X + 60, $sc.Y + [int]($sc.Height / 2)) | Out-Null
# Medición en reposo (docs/32: medir antes de optimizar). Runner sin GPU (WARP): valores orientativos, no de un PC real.
function Measure-Idle([string]$label, [int]$seconds = 10) {
  $names = 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App', 'WinMac.Ext.Demos'
  $a = @{}; Get-Process -Name $names -ErrorAction SilentlyContinue | ForEach-Object { $a["$($_.Name)#$($_.Id)"] = $_.TotalProcessorTime.TotalMilliseconds }
  Start-Sleep -Seconds $seconds
  Note ''; Note "### Medición en reposo: $label ($seconds s, $([Environment]::ProcessorCount) núcleos)"
  Note '| Proceso | CPU media (% de un núcleo) | Memoria de trabajo (MB) | Privada (MB) | Hilos |'; Note '|---|---|---|---|---|'
  Get-Process -Name $names -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
    $k = "$($_.Name)#$($_.Id)"; $cpu = if ($a.ContainsKey($k)) { ($_.TotalProcessorTime.TotalMilliseconds - $a[$k]) / ($seconds * 10) } else { 0 }
    Note ("| {0} | {1:N2} | {2:N1} | {3:N1} | {4} |" -f $_.Name, $cpu, ($_.WorkingSet64 / 1MB), ($_.PrivateMemorySize64 / 1MB), $_.Threads.Count)
  }
}

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

# ── 1b. WinMac 2.0: Isla compacta y Centro de actividades (docs/25)
Note ''; Note '### WinMac 2.0 — Isla compacta y Centro de actividades'
function NotchWide { (NotchW) -gt 300 }
function TabsLine { if (Test-Path $pillLog) { (Select-String -Path $pillLog -SimpleMatch ' tabs ' | Select-Object -Last 1).Line } else { '' } }
function ClickTab([string]$id) { $m = [regex]::Match((TabsLine), [regex]::Escape($id) + '@(\d+),(\d+)'); if ($m.Success) { [Au]::Click([int]$m.Groups[1].Value, [int]$m.Groups[2].Value); Start-Sleep -Milliseconds 900; return $true }; Note "  - pestaña «$id» no encontrada en el registro"; return $false }
function Expand { if (-not (NotchWide)) { [Au]::Click($cx, $sc.Y + 12); $null = WaitFor { NotchWide } 4; Start-Sleep -Milliseconds 900 } }
function Collapse { if (NotchWide) { $km = [regex]::Match((TabsLine), 'collapse@(\d+),(\d+)'); if ($km.Success) { [Au]::Click([int]$km.Groups[1].Value, [int]$km.Groups[2].Value) } else { [Au]::Click($cx, $sc.Y + 14) }; $null = WaitFor { -not (NotchWide) } 4; [Au]::Glide($sc.X + 60, $sc.Y + [int]($sc.Height * 0.6), 4); Start-Sleep -Milliseconds 900 } }
$null = WaitFor { LogHas $barLog 'música de prueba enviada' } 10; Start-Sleep -Milliseconds 800
IslaShot '20-isla-compacta-musica-prueba' 'Isla compacta con la música de PRUEBA (API interna; sin pista real en el runner)'
Expand
Shot '21-centro-en-curso-musica' 'Isla expandida: encabezado multimedia (Datos de prueba, sin controles) y «En curso»' ($cx - 330) $sc.Y 660 520
if (ClickTab 'history') { Shot '22-centro-historial-vacio' 'Estado vacío: «Historial» sin elementos' ($cx - 330) $sc.Y 660 520 }
Collapse
$dl = Join-Path $dlDir 'Descarga de prueba.iso.crdownload'; $fs = [IO.File]::Open($dl, 'Create'); $fs.Write((New-Object byte[] (3MB)), 0, 3MB); $fs.Flush()
$null = WaitFor { (NotchW) -ge 290 } 6; Start-Sleep -Milliseconds 1500
IslaShot '23-isla-compacta-descarga' 'Isla compacta con una descarga en curso (archivo .crdownload real en Descargas)'
$newShot = Join-Path $shotsDir 'captura-de-prueba-nueva.png'; TestPng $newShot 0 'Captura de prueba nueva'
if (WaitFor { LogHas $pillLog 'pill Capture' } 8) { Start-Sleep -Milliseconds 500; IslaShot '24-isla-captura-guardada' 'Captura guardada: miniatura en la Isla (archivo nuevo en la carpeta de capturas)' } else { Note '- **24-isla-captura-guardada**: NO capturada' }
Set-Clipboard -Value 'Texto de prueba del portapapeles (CI)'; Start-Sleep -Milliseconds 400; Set-Clipboard -Value 'https://github.com/PipeSick/win11-macos'
Start-Sleep -Milliseconds 2600
Expand
$null = ClickTab 'activity'   # la Isla recuerda la última pestaña: se vuelve a «En curso»
Shot '25-centro-varias-actividades' 'Centro de actividades: música de prueba, descarga en curso (y captura en el historial)' ($cx - 330) $sc.Y 660 560
Shot '25b-barra-e-isla-expandida' 'Pantalla completa: barra superior con la Isla expandida'
foreach ($t in @(@('captures', '26-centro-capturas', 'Sección «Capturas»: galería de la carpeta de capturas'), @('clipboard', '27-centro-portapapeles', 'Sección «Portapapeles»'), @('downloads', '28-centro-descargas', 'Sección «Descargas»'))) {
  if (ClickTab $t[0]) { Shot $t[1] $t[2] ($cx - 330) $sc.Y 660 560 }
}
$fs.Dispose(); Rename-Item $dl 'Descarga de prueba.iso' -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 2600
if (ClickTab 'history') { Shot '29-centro-historial' 'Sección «Historial»: la captura y la descarga terminada' ($cx - 330) $sc.Y 660 560 }
if (Test-Path $pillLog) { Note "  - pestañas: $(TabsLine)" }
Collapse
Remove-Item (Join-Path $dlDir 'Descarga de prueba.iso*') -ErrorAction SilentlyContinue

Measure-Idle 'en reposo tras las pruebas de la Isla' 10
# ── 1c. WinMac Screenshot (docs/26): overlay REAL sobre la pantalla congelada; región arrastrando con el ratón, ventana, pantalla completa
Note ''; Note '### WinMac Screenshot'
Collapse; Start-Sleep -Milliseconds 600   # la Isla abierta del paso anterior taparía la barra de captura y el editor
$capBefore = @(Get-ChildItem $shotsDir -Filter 'Captura *' -ErrorAction SilentlyContinue).Count
function Saved([int]$n) { WaitFor { (Select-String -Path $barLog -SimpleMatch '[captura] guardada' -ErrorAction SilentlyContinue).Count -ge $n } 8 }
$np2 = Start-Process notepad.exe -PassThru; Start-Sleep -Milliseconds 1500
& $bar --capture region | Out-Null
if (WaitFor { [Au]::Visible('WinMacCapture') } 8) {
  Start-Sleep -Milliseconds 500; [Au]::Glide(260, 220, 5); Start-Sleep -Milliseconds 200; [Au]::Down(); [Au]::Glide(700, 520, 20); Start-Sleep -Milliseconds 400
  Shot '30-captura-region-seleccionando' 'Captura de región: pantalla congelada y atenuada, selección con su tamaño en píxeles y la barra de modos'
  [Au]::Up(); Note "  - región guardada: $(Saved 1)"
} else { Note '- **30-captura-region**: la ventana de captura no apareció' }
Start-Sleep -Milliseconds 800
& $bar --capture window | Out-Null
if (WaitFor { [Au]::Visible('WinMacCapture') } 8) {
  $nr = if ($np2.MainWindowHandle -ne [IntPtr]::Zero) { [Au]::RectOf('Notepad') } else { '' }
  [Au]::Glide(400, 300, 10); Start-Sleep -Milliseconds 500
  Shot '31-captura-ventana' 'Captura de ventana: la ventana bajo el cursor resaltada con su título y tamaño'
  [Au]::Click(400, 300); Note "  - ventana guardada: $(Saved 2)"
}
Start-Sleep -Milliseconds 800
& $bar --capture screen | Out-Null
if (WaitFor { [Au]::Visible('WinMacCapture') } 8) { Start-Sleep -Milliseconds 400; Shot '32-captura-pantalla' 'Captura de pantalla completa (modo monitor): todo el monitor seleccionado'; [Au]::Click(500, 300); Note "  - pantalla guardada: $(Saved 3)" }
# ── Quick Look (sección 11): miniatura tras guardar, vista previa al pulsarla, Copiar y Esc
Note ''; Note '### Quick Look'
# pwsh 7 no lee imágenes del portapapeles: se pregunta a Windows PowerShell en STA con un script temporal (sin comillas anidadas)
function ClipImage {
  $f = Join-Path $env:TEMP 'winmac-clip-image.ps1'
  Set-Content -Path $f -Encoding UTF8 -Value @('Add-Type -AssemblyName System.Windows.Forms', '$i = [Windows.Forms.Clipboard]::GetImage()', 'if ($i) { [string]$i.Width + "x" + [string]$i.Height }')
  ((& powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $f) | Out-String).Trim()
}
function BarLine([string]$t) { (Select-String -Path $barLog -SimpleMatch $t -ErrorAction SilentlyContinue | Select-Object -Last 1).Line }
if (WaitFor { LogHas $barLog '[quicklook] miniatura' } 6) {
  Start-Sleep -Milliseconds 400; Note "  - $((BarLine '[quicklook] miniatura') -replace '^\S+ ', '') · $((BarLine '[quicklook] excluida') -replace '^\S+ ', '')"
  Shot '32c-quicklook-miniatura' 'Quick Look: miniatura flotante abajo a la derecha, por encima del Dock, tras guardar (desaparece sola a los 5 s)'
  $m = [regex]::Match((BarLine 'diag miniatura@'), 'miniatura@(\d+),(\d+)')
  if ($m.Success) {
    [Au]::Click([int]$m.Groups[1].Value, [int]$m.Groups[2].Value)
    if (WaitFor { LogHas $barLog '[quicklook] vista previa abierta' } 6) {
      Start-Sleep -Milliseconds 600; Shot '32d-quicklook-vista-previa' 'Quick Look: vista previa centrada con Copiar, Editar, Anotar, Recortar, Compartir, Guardar como, Abrir y Eliminar'
      $c = [regex]::Match((BarLine '[quicklook] diag close@'), 'copy@(\d+),(\d+)')
      if ($c.Success) { [Au]::Click([int]$c.Groups[1].Value, [int]$c.Groups[2].Value); Start-Sleep -Milliseconds 700; $img = ClipImage; Note "  - Copiar: $(if ($img) { 'imagen en el portapapeles (' + $img + ')' } else { 'NO hay imagen en el portapapeles' })"; Shot '32e-quicklook-copiada' 'Quick Look tras «Copiar» (aviso en la vista previa)' }
      [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 600
      Note "  - Esc cierra la vista previa: $(-not [Au]::Visible('WinMacQuickLook'))"
    } else { Note '  - la vista previa NO se abrió con el clic' }
  }
  $gone = WaitFor { -not [Au]::Visible('WinMacQuickLookThumb') } 8; Note "  - la miniatura ya no está: $gone"
} else { Note '- Quick Look: NO apareció la miniatura tras guardar' }
# ── WM-002: cuenta atrás de 3 s y un menú abierto en el Bloc de notas: la pantalla se congela con el menú todavía abierto
Note ''; Note '### WM-002 — captura con retardo y menú abierto'
$npd = Start-Process notepad.exe -PassThru; Start-Sleep -Milliseconds 1500
& $bar --capture region-delay3 | Out-Null
if (WaitFor { LogHas $barLog 'cuenta atrás de 3 s' } 5) {
  Note "  - $((BarLine 'cuenta atrás de') -replace '^\S+ ', '')"
  $null = [Au]::SetForegroundWindow($npd.MainWindowHandle); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('%f'); Start-Sleep -Milliseconds 500   # Alt+F: menú Archivo/File abierto con el teclado
  Shot '30a-menu-abierto-durante-cuenta' 'WM-002: menú del Bloc de notas abierto durante la cuenta atrás (el indicador está excluido de las capturas)'
  if (WaitFor { [Au]::Visible('WinMacCapture') } 6) {
    Start-Sleep -Milliseconds 500; Shot '30b-captura-con-menu-congelado' 'WM-002: al acabar la cuenta, la pantalla congelada CONTIENE el menú abierto'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500
  } else { Note '  - la superposición de captura no apareció tras la cuenta atrás' }
} else { Note '  - la cuenta atrás no empezó' }
Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
# ── WM-001: una ventana MAXIMIZADA no queda bajo el Dock (el Dock reserva su franja con SHAppBarMessage; no se toca la opacidad ni el orden Z)
Note ''; Note '### WM-001 — Dock y ventanas maximizadas'
$dockLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\dock.log'
$res = if (Test-Path $dockLog) { (Select-String -Path $dockLog -SimpleMatch 'espacio reservado (AppBar)' | Select-Object -Last 1).Line } else { '' }
Note "  - registro del Dock: $(if ($res) { $res } else { 'sin línea de reserva' })"
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea; Note "  - área de trabajo del monitor principal: $($wa.X),$($wa.Y) $($wa.Width)x$($wa.Height) (pantalla $($sc.Width)x$($sc.Height))"
$npm = Start-Process notepad.exe -PassThru -WindowStyle Maximized; Start-Sleep -Milliseconds 2000
$mr = [Au]::RectOf('Notepad'); $dr = [Au]::RectOf('WinMacDock')
Note "  - Bloc de notas maximizado: $mr · ventana del Dock: $dr"
if ($res -match '\(-?\d+,(-?\d+)\)-\(-?\d+,-?\d+\)' -and $mr -match '^(-?\d+),(-?\d+) (\d+)x(\d+)') {
  $bandTop = [int]([regex]::Match($res, '\(-?\d+,(-?\d+)\)-').Groups[1].Value); $mBottom = [int]$Matches[2] + [int]$Matches[4]
  # una ventana maximizada sobresale unos píxeles de borde invisible (DWM): se tolera hasta 16 px
  Note "  - WM-001: borde inferior de la ventana $mBottom px; franja del Dock desde $bandTop px → $(if ($mBottom -le $bandTop + 16) { 'NO la tapa (correcto)' } else { 'la ventana sigue bajo el Dock' })"
} else { Note '  - WM-001: no se pudo medir (falta la línea de reserva o la ventana)' }
Shot '32b-dock-ventana-maximizada' 'WM-001: ventana maximizada con el Dock reservando su franja (los controles inferiores quedan visibles)'
# ── WM-017: menú contextual oscuro del Dock (clic derecho real sobre el primer icono)
$dkr = [Au]::RectOf('WinMacDock')
if ($dkr -match '^(-?\d+),(-?\d+) (\d+)x(\d+)') {
  $dx = [int]$Matches[1] + [int]([int]$Matches[3] * 0.28); $dy = [int]$Matches[2] + [int]$Matches[4] - 48
  [Au]::Glide($dx, $dy, 12); Start-Sleep -Milliseconds 500   # por la ruta de entrada de Windows (como un ratón físico)
  [Au]::mouse_event(8, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 90; [Au]::mouse_event(16, 0, 0, 0, [IntPtr]::Zero)   # RIGHTDOWN / RIGHTUP
  if (WaitFor { [Au]::Visible('WinMacDockMenu') } 4) {
    Start-Sleep -Milliseconds 400; Note "  - WM-017: menú oscuro del Dock abierto ($([Au]::RectOf('WinMacDockMenu')))"
    Shot '32f-dock-menu-oscuro' 'WM-017: menú contextual del Dock con el estilo oscuro de WinMac (mismas acciones que antes)'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400
    Note "  - WM-017: Esc lo cierra: $(-not [Au]::Visible('WinMacDockMenu'))"
  } else {
    Note "  - WM-017: el menú del Dock NO apareció con el clic derecho en $dx,$dy (menú nativo visible: $([Au]::Visible('#32768')))"
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    if (Test-Path $dockLog) { Get-Content $dockLog -Tail 8 | ForEach-Object { Note ('    dock.log> ' + $_) } }
    Note "  - WM-017: ventana bajo el punto: $([Au]::ClassAt($dx, $dy))"
  }
}
Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 1500
$capAfter = @(Get-ChildItem $shotsDir -Filter 'Captura *' -ErrorAction SilentlyContinue)
Note "  - archivos nuevos en la carpeta de capturas: $($capAfter.Count - $capBefore) ($((($capAfter | ForEach-Object { $_.Name + ' ' + $_.Length + ' B' }) -join '; ')))"
if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[captura\]|Captura \w+: |No hay ninguna barra' | Select-Object -Last 14 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) } }

# ── 1d. Editor de capturas (docs/26): se abre sobre la región recién guardada; rectángulo, flecha y texto con el ratón y el teclado; «Guardar» crea una copia
Note ''; Note '### Editor de capturas'
$src = Get-ChildItem $shotsDir -Filter 'Captura *' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch 'editada' } | Sort-Object LastWriteTime | Select-Object -First 1
if ($src) {
  & $bar --edit $src.FullName | Out-Null
  if (WaitFor { [Au]::Visible('WinMacCaptureEditor') } 10) {
    $null = WaitFor { LogHas $barLog '[editor] diag' } 5; Start-Sleep -Milliseconds 800
    $ed = (Select-String -Path $barLog -SimpleMatch '[editor] diag' | Select-Object -Last 1).Line
    function EdBtn([string]$id) { $m = [regex]::Match($ed, '(?:^|\s|\|)' + [regex]::Escape($id) + '@(-?\d+),(-?\d+)'); if ($m.Success) { return @([int]$m.Groups[1].Value, [int]$m.Groups[2].Value) }; return $null }
    $er = [Au]::RectOf('WinMacCaptureEditor'); Note "  - ventana del editor: $er"
    Shot '34-editor-abierto' 'Editor de capturas recién abierto sobre la región guardada (barra de herramientas como la del mockup)'
    $m = [regex]::Match($er, '(-?\d+),(-?\d+) (\d+)x(\d+)'); $ex = [int]$m.Groups[1].Value; $ey = [int]$m.Groups[2].Value; $ew = [int]$m.Groups[3].Value; $eh = [int]$m.Groups[4].Value
    $mx = $ex + [int]($ew / 2); $my = $ey + [int]($eh / 2)
    if ($b = EdBtn 'rect') { [Au]::Click($b[0], $b[1]); Start-Sleep -Milliseconds 300; [Au]::Glide($mx - 150, $my - 70, 5); [Au]::Down(); [Au]::Glide($mx - 20, $my + 30, 12); [Au]::Up(); Start-Sleep -Milliseconds 300 }
    if ($b = EdBtn 'arrow') { [Au]::Click($b[0], $b[1]); Start-Sleep -Milliseconds 300; [Au]::Glide($mx + 140, $my + 90, 5); [Au]::Down(); [Au]::Glide($mx, $my - 10, 12); [Au]::Up(); Start-Sleep -Milliseconds 300 }
    if ($b = EdBtn 'highlight') { [Au]::Click($b[0], $b[1]); Start-Sleep -Milliseconds 300; [Au]::Glide($mx + 30, $my - 90, 5); [Au]::Down(); [Au]::Glide($mx + 170, $my - 60, 10); [Au]::Up(); Start-Sleep -Milliseconds 300 }
    if ($b = EdBtn 'text') { [Au]::Click($b[0], $b[1]); Start-Sleep -Milliseconds 300; [Au]::Click($mx - 140, $my + 60); Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait('Hola WinMac'); Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); Start-Sleep -Milliseconds 400 }
    Shot '35-editor-anotaciones' 'Editor con rectángulo, flecha, resaltado y texto hechos con el ratón y el teclado'
    if ($b = EdBtn 'save') { [Au]::Click($b[0], $b[1]) }
    $edited = WaitFor { @(Get-ChildItem $shotsDir -Filter '*(editada)*' -ErrorAction SilentlyContinue).Count -ge 1 } 8
    Note "  - copia editada guardada: $edited ($((Get-ChildItem $shotsDir -Filter '*(editada)*' -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + ' ' + $_.Length + ' B' }) -join '; ')); original intacto: $((Get-Item $src.FullName).Length -eq $src.Length)"
    Start-Sleep -Milliseconds 600; Shot '36-editor-guardado' 'Editor tras «Guardar»: la barra de estado confirma la copia (el original no cambia)'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); $closed = WaitFor { -not [Au]::Visible('WinMacCaptureEditor') } 5; Note "  - editor cerrado con Esc tras guardar: $closed"
  } else { Note '- **34-editor**: la ventana del editor no apareció' }
  if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[editor\]' | Where-Object { $_.Line -notmatch 'diag' } | Select-Object -Last 8 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) } }
} else { Note '- **34-editor**: no hay ninguna captura de WinMac para editar' }

# ── 1e. Grabación de pantalla (docs/26): elegida por el usuario en la superposición, indicador visible, detener con un clic; solo vídeo, sin micrófono
Note ''; Note '### Grabación de pantalla'
& $bar --record screen | Out-Null
if (WaitFor { [Au]::Visible('WinMacCapture') } 8) {
  Start-Sleep -Milliseconds 500; Shot '40-grabacion-elegir' 'Grabación: la misma superposición, con «Grabar pantalla» (nada se graba hasta hacer clic)'
  [Au]::Click(500, 300)
  $recOk = WaitFor { (LogHas $barLog '[grabación] iniciada') -or (LogHas $barLog '[grabación] no se pudo iniciar') } 30
  if (LogHas $barLog '[grabación] iniciada') {
    Start-Sleep -Milliseconds 2500; Shot '41-grabando-indicador' 'Grabando: indicador rojo con el tiempo (excluido del vídeo) y la actividad en la Isla'
    Expand; $null = ClickTab 'activity'; Shot '41b-isla-grabando' 'Centro de actividades: «Grabando la pantalla» con el tiempo transcurrido' ($cx - 330) $sc.Y 660 560; Collapse
    Start-Sleep -Milliseconds 1500
    $im = [regex]::Match(((Select-String -Path $barLog -SimpleMatch '[grabación] diag indicador@' | Select-Object -Last 1).Line), 'indicador@(-?\d+),(-?\d+)')
    if ($im.Success) { [Au]::Click([int]$im.Groups[1].Value, [int]$im.Groups[2].Value) } else { & $bar --record stop | Out-Null }
    $saved = WaitFor { LogHas $barLog '[grabación] guardada' } 20
    Note "  - detenida con un clic en el indicador y guardada: $saved"
    $mp4 = Get-ChildItem (Join-Path ([Environment]::GetFolderPath('MyVideos')) 'Screen Recordings') -Filter '*.mp4' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($mp4) { Note "  - archivo: $($mp4.Name) ($($mp4.Length) B)" }
    $pm = [regex]::Match(((Select-String -Path $barLog -SimpleMatch '[grabación] muestra ' | Select-Object -Last 1).Line), 'muestra (.+)$')
    if ($pm.Success -and (Test-Path $pm.Groups[1].Value)) { try { $img = [System.Drawing.Image]::FromFile($pm.Groups[1].Value); $img.Save((Join-Path $OutDir '42-grabacion-fotograma.png'), [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose(); Note '- **42-grabacion-fotograma.png**: fotograma sacado del MP4 grabado (comprueba orientación y contenido)' } catch { Note "  - no se pudo convertir el fotograma: $_" } }
    Expand; $null = ClickTab 'history'; Shot '43-historial-grabacion' 'Historial: la grabación terminada con su archivo' ($cx - 330) $sc.Y 660 560; Collapse
  } else { Note '  - la grabación no se pudo iniciar en este equipo (ver registro)'; & $bar --record stop | Out-Null }   # nunca dejarla grabando
} else { Note '- **40-grabacion**: la superposición no apareció' }
if (Test-Path $barLog) { Select-String -Path $barLog -SimpleMatch '[grabación]' | Where-Object { $_.Line -notmatch 'diag|muestra' } | Select-Object -Last 8 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) } }

# ── 1e-bis. Memoria de la Barra con grabaciones repetidas (docs/34): tendencias, no una sola muestra
function BarProc { Get-Process -Name 'WinMac.Bar.App' -ErrorAction SilentlyContinue | Select-Object -First 1 }
$script:memRows = @(); $script:lastCpu = $null; $script:lastAt = $null
function BarRow([string]$label) {
  $p = BarProc; if (-not $p) { $script:memRows += "| $label | (la Barra no está en marcha) | | | | | | |"; return }
  $p.Refresh(); $now = Get-Date; $cpu = $p.TotalProcessorTime.TotalMilliseconds
  $pct = if ($script:lastCpu -ne $null) { ($cpu - $script:lastCpu) / [Math]::Max(1, ($now - $script:lastAt).TotalMilliseconds) * 100 } else { 0 }
  $script:lastCpu = $cpu; $script:lastAt = $now
  $gdi = [Au]::GetGuiResources($p.Handle, 0); $usr = [Au]::GetGuiResources($p.Handle, 1)
  $script:memRows += ("| {0} | {1:N1} | {2:N1} | {3:N1} | {4} | {5} | {6} | {7} |" -f $label, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $pct, $p.Threads.Count, $p.HandleCount, $gdi, $usr)
}
function OneRecording([int]$secs = 3) {
  $before = @(Select-String -Path $barLog -SimpleMatch '[grabación] guardada' -ErrorAction SilentlyContinue).Count
  & $bar --record screen | Out-Null
  if (-not (WaitFor { [Au]::Visible('WinMacCapture') } 8)) { return $false }
  Start-Sleep -Milliseconds 300; [Au]::Click(500, 300)
  $null = WaitFor { (Select-String -Path $barLog -SimpleMatch '[grabación] iniciada' -ErrorAction SilentlyContinue).Count -gt $script:recStarted } 20
  $script:recStarted = @(Select-String -Path $barLog -SimpleMatch '[grabación] iniciada' -ErrorAction SilentlyContinue).Count
  Start-Sleep -Seconds $secs; & $bar --record stop | Out-Null
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $ok = WaitFor { (Select-String -Path $barLog -SimpleMatch '[grabación] guardada' -ErrorAction SilentlyContinue).Count -gt $before } 20
  $script:releaseMs += $sw.ElapsedMilliseconds
  return $ok
}
Note ''; Note '### Memoria de la Barra con grabaciones repetidas (pantalla 1024×768, 15 fps, ~3 s cada una)'
$script:recStarted = @(Select-String -Path $barLog -SimpleMatch '[grabación] iniciada' -ErrorAction SilentlyContinue).Count; $script:releaseMs = 0
Collapse; Start-Sleep -Seconds 5; BarRow 'reposo inicial (tras la primera grabación de arriba)'
$okRec = 0
for ($i = 1; $i -le 20; $i++) { if (OneRecording 3) { $okRec++ }; if ($i -in 1, 3, 5, 10, 15, 20) { Start-Sleep -Seconds 2; BarRow "tras $i grabación(es) más" } }
Note "  - grabaciones guardadas en el bucle: $okRec de 20; tiempo medio de cierre del archivo: $([int]($script:releaseMs / [Math]::Max(1, $okRec))) ms"
Start-Sleep -Seconds 20; BarRow 'tras 20 s de reposo'
& $bar --record screen | Out-Null; if (WaitFor { [Au]::Visible('WinMacCapture') } 8) { Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Seconds 2 }
BarRow 'tras una grabación cancelada (Esc en la superposición)'
$failFlag = Join-Path $env:LOCALAPPDATA 'WinMac\ci-record-fail'; 'x' | Set-Content $failFlag
& $bar --record screen | Out-Null; if (WaitFor { [Au]::Visible('WinMacCapture') } 8) { Start-Sleep -Milliseconds 300; [Au]::Click(500, 300); $null = WaitFor { LogHas $barLog 'fallo simulado' } 10 }
Remove-Item $failFlag -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2
BarRow 'tras una grabación fallida (fallo de inicialización SIMULADO)'
Start-Sleep -Seconds 20; BarRow 'tras otros 20 s de reposo'
& $bar --diag gc | Out-Null; $null = WaitFor { LogHas $barLog '[diag] GC completo' } 8; Start-Sleep -Seconds 2
BarRow 'tras un GC completo pedido por el diagnóstico (¿basura o retenido?)'
Select-String -Path $barLog -SimpleMatch '[diag] GC completo' -ErrorAction SilentlyContinue | Select-Object -Last 1 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) }
Note '| Momento | Working Set (MB) | Privada (MB) | CPU desde la fila anterior (% de un núcleo) | Hilos | Handles | GDI | USER |'; Note '|---|---|---|---|---|---|---|---|'
$script:memRows | ForEach-Object { Note $_ }
Select-String -Path $barLog -SimpleMatch '[grabación] búferes' -ErrorAction SilentlyContinue | Select-Object -Last 2 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) }
$mem = @(Select-String -Path $barLog -SimpleMatch '[grabación] memoria:' -ErrorAction SilentlyContinue)
foreach ($k in 0, 4, 9, 14, ($mem.Count - 1)) { if ($k -ge 0 -and $k -lt $mem.Count) { Note ("  - grabación $($k + 1): " + ($mem[$k].Line -replace '^.*memoria: ', '')) } }
if (LogHas $barLog 'fallo simulado') { Note '  - el fallo simulado se registró y la Barra siguió funcionando' }

# ── 1g. Extensiones (docs/30): procesos aparte con su propio canal; actividades reales de la API en la Isla
Note ''; Note '### Extensiones'
$extOk = WaitFor { (LogHas $hostLog '[extensiones] pomodoro conectada') -and (LogHas $hostLog '[extensiones] uptime conectada') } 15
Note "  - las dos extensiones de demostración conectadas por su canal: $extOk"
if (Test-Path $hostLog) { Select-String -Path $hostLog -SimpleMatch '[extensiones]' | Select-Object -Last 6 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ \S+ ', '')) } }
Get-Process -Name 'WinMac.Ext.Demos' -ErrorAction SilentlyContinue | ForEach-Object { Note "  - proceso aparte: WinMac.Ext.Demos pid $($_.Id)" }
Expand; $null = ClickTab 'activity'; Start-Sleep -Milliseconds 800; Shot '47-extensiones-en-curso' 'Extensiones: «Concentración» (Pomodoro) y «Windows encendido» como actividades reales de la API' ($cx - 330) $sc.Y 660 560; Collapse

# ── 1f. Workspace Manager (docs/29): ventanas reales del Bloc de notas; cuadrícula, guardar, lado a lado, restaurar
Note ''; Note '### Espacios de trabajo'
$wsA = Start-Process notepad.exe -PassThru; $wsB = Start-Process notepad.exe -PassThru; $wsC = Start-Process notepad.exe -PassThru; Start-Sleep -Milliseconds 2500
[Au]::SetCursorPos($cx, [int]($sc.Height / 2)) | Out-Null
& $bar --workspace grid | Out-Null; $null = WaitFor { LogHas $barLog '[espacios] cuadrícula' } 6; Start-Sleep -Milliseconds 800
Shot '44-espacios-cuadricula' 'Workspace Manager: ventanas del monitor en cuadrícula (APIs públicas de Windows)'
& $bar --workspace save | Out-Null; $null = WaitFor { LogHas $barLog '[espacios] guardado' } 6
& $bar --workspace side | Out-Null; $null = WaitFor { LogHas $barLog '[espacios] lado a lado' } 6; Start-Sleep -Milliseconds 800
Shot '45-espacios-lado-a-lado' 'Workspace Manager: las dos ventanas del frente, lado a lado'
& $bar --workspace restore | Out-Null; $null = WaitFor { LogHas $barLog '[espacios] restaurado' } 6; Start-Sleep -Milliseconds 800
Shot '46-espacios-restaurado' 'Workspace Manager: el espacio guardado (la cuadrícula) restaurado'
if (Test-Path $barLog) { Select-String -Path $barLog -SimpleMatch '[espacios]' | Select-Object -Last 5 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) } }
$wsf = Join-Path $env:APPDATA 'WinMac\workspaces.json'; if (Test-Path $wsf) { Note "  - archivo de espacios: $((Get-Item $wsf).Length) B" }
foreach ($p in @($wsA, $wsB, $wsC)) { try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { } }
Start-Sleep -Milliseconds 800
Expand; $null = ClickTab 'captures'; Shot '33-galeria-tras-capturar' 'Galería de la Isla con las capturas recién hechas por WinMac' ($cx - 330) $sc.Y 660 560
# Galería completa: «Ver todas» con el ratón, búsqueda con el teclado, selección de la primera, vista ampliada
$gm = [regex]::Match((TabsLine), 'gallery@(\d+),(\d+)')
if ($gm.Success) {
  [Au]::Click([int]$gm.Groups[1].Value, [int]$gm.Groups[2].Value)
  $gOk = WaitFor { (Get-Content $pillLog -ErrorAction SilentlyContinue | Select-String -SimpleMatch ' gallery items=').Count -ge 1 } 8; Start-Sleep -Milliseconds 900
  function GalLine { (Get-Content $pillLog | Select-String -SimpleMatch ' gallery items=' | Select-Object -Last 1).Line }
  Note "  - galería abierta: $gOk · $((GalLine) -replace '^\S+ ', '')"
  Shot '37-galeria-completa' 'Galería completa: miniaturas de la carpeta de capturas, orden, búsqueda y panel de detalles'
  $fm = [regex]::Match((GalLine), 'first@(-?\d+),(-?\d+)')   # antes de escribir: tras la búsqueda el contenedor aún no está en el registro
  $sm = [regex]::Match((GalLine), 'search@(-?\d+),(-?\d+)')
  if ($sm.Success) { [Au]::Click([int]$sm.Groups[1].Value, [int]$sm.Groups[2].Value); Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait('Captura'); Start-Sleep -Milliseconds 1200 }
  if ($fm.Success) { [Au]::Click([int]$fm.Groups[1].Value, [int]$fm.Groups[2].Value); Start-Sleep -Milliseconds 1200 }
  Note "  - tras buscar «Captura» y elegir la primera: $((GalLine) -replace '^\S+ ', '')"
  Shot '38-galeria-busqueda-seleccion' 'Galería: búsqueda «Captura» y una captura seleccionada con sus acciones reales (abrir, editar, copiar, renombrar, eliminar)'
  if ($fm.Success) { [Au]::Click([int]$fm.Groups[1].Value, [int]$fm.Groups[2].Value); Start-Sleep -Milliseconds 120; [Au]::Click([int]$fm.Groups[1].Value, [int]$fm.Groups[2].Value); Start-Sleep -Milliseconds 1200; Shot '39-galeria-vista-ampliada' 'Galería: vista ampliada de la captura (doble clic; clic o Esc la cierra)'; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400 }
  $gw = [Au]::FindWindow($null, 'Capturas ' + [char]0x2014 + ' WinMac'); if ($gw -ne [IntPtr]::Zero) { $null = [Au]::PostMessage($gw, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) }; Start-Sleep -Milliseconds 600   # WM_CLOSE solo a la galería
} else { Note '  - botón «Ver todas» no encontrado en el registro de la Isla' }
Collapse
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

# Interacción real: «Edit» → «Time/Date» (o «Hora y fecha») desde el desplegable de WinMac; el Bloc de notas debe insertar la fecha en su documento.
$before = [Au]::NotepadText($np.MainWindowHandle)
$edit = $labels | Where-Object { $_.Text -match '^(Edit|Edición)$' } | Select-Object -First 1
if ($edit -and $np.MainWindowHandle -ne [IntPtr]::Zero) {
  [Au]::SetForegroundWindow($np.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 700
  $cur = [regex]::Matches((MenuDiagLine), 'AppMenu:([^@|]+)@(\d+),(\d+)') | Where-Object { $_.Groups[1].Value.Trim() -eq $edit.Text } | Select-Object -Last 1
  if ($cur) { [Au]::Click([int]$cur.Groups[2].Value, [int]$cur.Groups[3].Value); Start-Sleep -Milliseconds 900 }
  $rows = [regex]::Matches((MenuDiagLine), 'fila:([^:]+):([^@|]*)@(\d+),(\d+)') | ForEach-Object { [pscustomobject]@{ Text = $_.Groups[2].Value.Trim(); X = [int]$_.Groups[3].Value; Y = [int]$_.Groups[4].Value } }
  $td = $rows | Where-Object { $_.Text -match 'Time/Date|Hora y fecha|Fecha' } | Select-Object -First 1
  Shot '09x-menu-edit-antes-de-elegir' 'Menú «Edit» abierto: se elegirá «Time/Date»' $sc.X $sc.Y 600 560
  if ($td) { [Au]::Click($td.X, $td.Y); Start-Sleep -Milliseconds 900 }
  $after = [Au]::NotepadText($np.MainWindowHandle)
  Note "  - interacción: «$($td.Text)» elegido en el desplegable de WinMac → texto del Bloc de notas antes «$before», después «$after» → $(if ($after.Length -gt $before.Length) { 'LA ORDEN SE EJECUTÓ' } else { 'la orden NO se ejecutó' })"
  Shot '09y-menu-orden-ejecutada' 'Bloc de notas tras elegir «Time/Date» en el desplegable de WinMac' $sc.X $sc.Y 700 400
}
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
function SpotShot([string]$name, [string]$what) { Shot $name "$what (ventana $([Au]::RectOf('WinMacSpotlight')))"; Shot ($name + 'b') "$($what): detalle" ($cx - 470) ($sc.Y + [int]($sc.Height * 0.18)) 940 560 }
if (SpotOpen) {
  Start-Sleep -Milliseconds 500; SpotShot '13-spotlight-inicial' 'Spotlight recién abierto'
  [System.Windows.Forms.SendKeys]::SendWait('notepad'); Start-Sleep -Milliseconds 900; SpotShot '14-spotlight-app' 'Spotlight buscando «notepad»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('12*12'); Start-Sleep -Milliseconds 700; SpotShot '15-spotlight-12x12' 'Spotlight con «12*12»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('con'); Start-Sleep -Milliseconds 1200; SpotShot '16-spotlight-resultados' 'Spotlight con «con»: varias secciones de resultados y vista previa'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  # Spotlight Pro (docs/27): acciones de WinMac, herramientas (solo con Intro), «> comando» que nunca se ejecuta, porcentajes
  [System.Windows.Forms.SendKeys]::SendWait('captura'); Start-Sleep -Milliseconds 900; SpotShot '17-spotlight-acciones' 'Spotlight Pro: acciones de WinMac (capturar, grabar) con su vista previa'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('ip'); Start-Sleep -Milliseconds 900; SpotShot '18-spotlight-ip' 'Spotlight Pro: dirección IP local (dato real del equipo, sin tráfico de red)'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('ping 127.0.0.1'); Start-Sleep -Milliseconds 900; SpotShot '19a-spotlight-ping-antes' 'Spotlight Pro: «ping» ofrecido; no se ejecuta al escribir'
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}'); $null = WaitFor { LogHas $barLog '[spotlight] herramienta ping terminada' } 10; Start-Sleep -Milliseconds 500
  SpotShot '19b-spotlight-ping-resultado' 'Spotlight Pro: resultado real del ping tras pulsar Intro'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  # WM-021: escritura rápida y borrado repetido (no debe recrear la superficie ni cancelar el ping); destinos reales
  foreach ($k in 1..3) { [System.Windows.Forms.SendKeys]::SendWait('ping 8.8'); [System.Windows.Forms.SendKeys]::SendWait('{BACKSPACE}{BACKSPACE}{BACKSPACE}{BACKSPACE}{BACKSPACE}{BACKSPACE}{BACKSPACE}{BACKSPACE}') }
  [System.Windows.Forms.SendKeys]::SendWait('ping 127.0.0.1'); Start-Sleep -Milliseconds 700; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  $null = WaitFor { (Select-String -Path $barLog -SimpleMatch 'herramienta ping terminada' -ErrorAction SilentlyContinue).Count -ge 2 } 12; Start-Sleep -Milliseconds 400
  SpotShot '19g-spotlight-ping-panel' 'Spotlight: panel de ping real (destino, estado, paquetes, pérdida, latencia, Repetir/Copiar) tras escribir y borrar deprisa'
  foreach ($dest in 'google.com', 'no-existe.invalid', '10.255.255.1') {
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300; [System.Windows.Forms.SendKeys]::SendWait("ping $dest"); Start-Sleep -Milliseconds 600; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    $n = @(Select-String -Path $barLog -SimpleMatch 'herramienta ping terminada' -ErrorAction SilentlyContinue).Count; $null = WaitFor { (Select-String -Path $barLog -SimpleMatch 'herramienta ping terminada' -ErrorAction SilentlyContinue).Count -gt $n } 14
  }
  Start-Sleep -Milliseconds 400; SpotShot '19h-spotlight-ping-sin-respuesta' 'Spotlight: ping a un destino que no responde (errores comprensibles, nada inventado)'
  Select-String -Path $barLog -Pattern 'herramienta ping|spotlight\] cerrado' -ErrorAction SilentlyContinue | Select-Object -Last 8 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) }
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('> del /s prueba'); Start-Sleep -Milliseconds 900; SpotShot '19c-spotlight-comando' 'Spotlight Pro: «> …» solo ofrece abrir una terminal o copiar el texto (nunca lo ejecuta)'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('20% de 150'); Start-Sleep -Milliseconds 700; SpotShot '19d-spotlight-porcentaje' 'Spotlight Pro: «20% de 150»'
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
  $sp = [Au]::FindWindow($null, 'System Properties'); if ($sp -ne [IntPtr]::Zero) { $null = [Au]::PostMessage($sp, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Note '  - (se cerró un aviso del runner «System Properties» que quitaba el foco)'; Start-Sleep -Milliseconds 500 }
  if (-not [Au]::Visible('WinMacSpotlight')) { & $bar --spotlight | Out-Null; $null = WaitFor { [Au]::Visible('WinMacSpotlight') } 4 }
  [System.Windows.Forms.SendKeys]::SendWait('portapapeles'); Start-Sleep -Milliseconds 1200;
  Select-String -Path $barLog -SimpleMatch '[spotlight] portapapeles:' -ErrorAction SilentlyContinue | Select-Object -Last 1 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) }
  Note "  - (registro de la Isla: $((Get-Content $pillLog -ErrorAction SilentlyContinue).Count) líneas; últimas:)"; Get-Content $pillLog -ErrorAction SilentlyContinue | Select-Object -Last 3 | ForEach-Object { Note ('    ' + $_) }
  Start-Sleep -Milliseconds 500; Get-Content $pillLog -ErrorAction SilentlyContinue | Select-String -SimpleMatch ' clip query ' | Select-Object -Last 3 | ForEach-Object { Note ('  - Isla: ' + $_.Line) }; SpotShot '19e-spotlight-portapapeles' 'Spotlight Pro: historial del portapapeles de la Isla (solo lo admitido por la privacidad)'
  [System.Windows.Forms.SendKeys]::SendWait('{TAB}'); Start-Sleep -Milliseconds 300; SpotShot '19f-spotlight-tab' 'Spotlight Pro: Tab mueve la selección (la vista previa la sigue)'
  if (Test-Path $barLog) { Select-String -Path $barLog -SimpleMatch '[spotlight] herramienta' | Select-Object -Last 3 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ ', '')) } }
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 200; [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
} else { Note '- **13-spotlight**: NO capturado (la ventana no apareció)' }
Start-Sleep -Milliseconds 600

# ── 1h. Teclado en la Isla (docs/25): se abre con un clic real; ←/→ cambian de sección; Esc la cierra (pulsaciones reales)
Note ''; Note '### Teclado en la Isla'
Collapse; Start-Sleep -Milliseconds 600; $wasClosed = -not (NotchWide)
[Au]::Click($cx, $sc.Y + 12); $opened = WaitFor { NotchWide } 4; Start-Sleep -Milliseconds 1200
Note "  - antes del clic cerrada: $wasClosed; abierta tras el clic: $opened; ventana en primer plano: $([Au]::GetForegroundWindow())" 
[System.Windows.Forms.SendKeys]::SendWait('{RIGHT}'); $kOk = WaitFor { (Get-Content $pillLog -ErrorAction SilentlyContinue | Select-String -SimpleMatch 'teclado seccion').Count -ge 1 } 4
Start-Sleep -Milliseconds 600; Shot '48-isla-teclado' 'Isla abierta con un clic: → pasó a la sección siguiente (foco en la pestaña)' ($cx - 330) $sc.Y 660 560
[System.Windows.Forms.SendKeys]::SendWait('{ESC}'); $eOk = WaitFor { (Get-Content $pillLog -ErrorAction SilentlyContinue | Select-String -SimpleMatch 'teclado esc').Count -ge 1 } 4
Note "  - → cambió de sección: $kOk; Esc la cerró: $eOk"
Get-Content $pillLog -ErrorAction SilentlyContinue | Select-String -SimpleMatch ' teclado ' | Select-Object -Last 3 | ForEach-Object { Note ('  - ' + $_.Line) }

# ── 1i. Estabilidad prolongada (docs/34): ciclos repetidos de todo lo que puede bloquear un hilo de interfaz
Note ''; Note '### Estabilidad prolongada'
$hostBefore = @(Select-String -Path $hostLog -Pattern 'sin latido|Crashed|terminó de forma inesperada' -ErrorAction SilentlyContinue).Count
$spotMs = @(); $cycles = 6; $t1 = Get-Date
for ($c = 1; $c -le $cycles; $c++) {
  for ($k = 0; $k -lt 3; $k++) { [Au]::Click($cx, $sc.Y + 12); Start-Sleep -Milliseconds 500; $null = ClickTab 'captures'; $null = ClickTab 'activity'; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400; Collapse }
  & $bar --capture screen | Out-Null; if (WaitFor { [Au]::Visible('WinMacCapture') } 6) { Start-Sleep -Milliseconds 200; [Au]::Click(500, 300) }; Start-Sleep -Milliseconds 800
  $last = Get-ChildItem $shotsDir -Filter 'Captura *' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch 'editada' } | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($last) { & $bar --edit $last.FullName | Out-Null; if (WaitFor { [Au]::Visible('WinMacCaptureEditor') } 8) { Start-Sleep -Milliseconds 400; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); $null = WaitFor { -not [Au]::Visible('WinMacCaptureEditor') } 4 } }
  $null = OneRecording 2
  $sw = [Diagnostics.Stopwatch]::StartNew(); & $bar --spotlight | Out-Null
  if (WaitFor { [Au]::Visible('WinMacSpotlight') } 5) { $spotMs += $sw.ElapsedMilliseconds; [System.Windows.Forms.SendKeys]::SendWait('captura'); Start-Sleep -Milliseconds 400; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); [System.Windows.Forms.SendKeys]::SendWait('{ESC}') }
  & $bar --workspace grid | Out-Null; Start-Sleep -Milliseconds 500
  if ($c -eq 3) { Get-Process -Name 'WinMac.Ext.Demos' -ErrorAction SilentlyContinue | Select-Object -First 1 | Stop-Process -Force; Note '  - ciclo 3: se cerró a la fuerza un proceso de extensión (prueba de aislamiento)' }
}
$secs = [int]((Get-Date) - $t1).TotalSeconds
$hostAfter = @(Select-String -Path $hostLog -Pattern 'sin latido|Crashed|terminó de forma inesperada' -ErrorAction SilentlyContinue)
Note "  - $cycles ciclos en $secs s (Isla 18 veces, $cycles capturas, editor, grabaciones, Spotlight, cuadrícula)"
Note "  - Spotlight visible tras la orden: $(if ($spotMs.Count) { "$([int]($spotMs | Measure-Object -Average).Average) ms de media, máx. $(($spotMs | Measure-Object -Maximum).Maximum) ms ($($spotMs.Count) veces)" } else { 'no se abrió' })"
Note "  - reinicios o latidos perdidos del host durante la prueba: $($hostAfter.Count - $hostBefore)"
$hostAfter | Select-Object -Skip $hostBefore | Select-Object -First 6 | ForEach-Object { Note ('    ' + $_.Line) }
Select-String -Path $hostLog -SimpleMatch '[extensiones]' -ErrorAction SilentlyContinue | Select-Object -Last 4 | ForEach-Object { Note ('  - ' + ($_.Line -replace '^\S+ \S+ ', '')) }
$excL = @(Select-String -Path $barLog -Pattern 'CIERRE INESPERADO|ERROR|\] error' -ErrorAction SilentlyContinue); Note "  - errores en bar.log: $($excL.Count)"
$excL | Select-Object -Last 4 | ForEach-Object { Note ('    ' + (($_.Line -replace '^\S+ ', '') -replace '(.{220}).*', '$1…')) }
# ── 1j. Ajustes nuevos dibujados FUERA de pantalla con el pintor real (valida el dibujo, no el ratón): atajos, portapapeles, extensiones
Note ''; Note '### Ajustes (fuera de pantalla)'
foreach ($r in @(@('bar', 1300, '49a-ajustes-atajos'), @('spot', 0, '49b-ajustes-spotlight-espacios'), @('clip', 0, '49c-ajustes-portapapeles'), @('ext', 0, '49d-ajustes-extensiones'), @('dock', 0, '49e-ajustes-dock'), @('weather', 0, '49f-ajustes-clima'), @('capture', 0, '49g-ajustes-capturas'), @('general', 0, '49h-ajustes-general'))) {
  $png = Join-Path $OutDir ($r[2] + '.png')
  Start-Process -FilePath $hostExe -ArgumentList @('--render-settings', $r[0], '--scroll', $r[1], '--out', "`"$png`"", '--scale', '1') -Wait -WindowStyle Hidden | Out-Null
  Note "- **$($r[2]).png**: Ajustes › $($r[0]) desplazado $($r[1]) DIP (los atajos solo muestran su estado real en la ventana viva: aquí no hay Barra que informe)"
}
$trayPng = Join-Path $OutDir '49i-panel-bandeja-winmac.png'
Start-Process -FilePath $hostExe -ArgumentList @('--render-tray-panel', '--out', "`"$trayPng`"") -Wait -WindowStyle Hidden | Out-Null
Note "- **49i-panel-bandeja-winmac.png**: WM-011, panel del icono de WinMac en la bandeja (estados de MUESTRA: la Isla «falló»), fuera de pantalla con el código de dibujo real"
Measure-Idle 'al final, tras usar todo' 10
Start-Process -FilePath $hostExe -ArgumentList '--quit' -Wait -WindowStyle Hidden | Out-Null; [void]$hp.WaitForExit(30000)
Get-Process -Name 'WinMac', 'WinMac.Bar.App', 'WinMac.Dock.App', 'NotchWin.App', 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# ── Registros útiles
Note ''; Note '## Líneas de registro relevantes'; Note '```'
if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[(isla|launchpad|spotlight|menús)\]' | Select-Object -Last 40 | ForEach-Object { Note ($_.Line -replace '^\S+ ', '') } }
if (Test-Path $hostLog) { Select-String -Path $hostLog -Pattern '\[(notch|bar|dock)\] (Crashed|Failed|sin latido|terminó)' | ForEach-Object { Note $_.Line } }
if (Test-Path $barLog) { Select-String -Path $barLog -Pattern '\[panel\]|\[menús\] (clic|abrir|«)|CIERRE INESPERADO|ERROR' -Context 0, 14 | Select-Object -Last 30 | ForEach-Object { Note $_.Line; if ($_.Line -match 'CIERRE|ERROR') { $_.Context.PostContext | ForEach-Object { Note "    $_" } } } }
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
