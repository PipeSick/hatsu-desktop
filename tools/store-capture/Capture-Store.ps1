<#
  Capturas para Microsoft Store con los BINARIOS REALES (dist\WinMac: host, Barra, Dock, Hatsu Notch) sobre la sesión de escritorio REAL del runner (Graphics.CopyFromScreen).
  No dibuja ni simula nada de Hatsu. Lo único preparado es el ENTORNO, como haría cualquier persona: resolución, fondo de pantalla de Windows, aplicaciones reales abiertas y
  Chrome reproduciendo un tono generado aquí con metadatos (sesión multimedia REAL de Windows que Hatsu Notch lee). Informativo: nunca falla el CI; todo queda en informe.md.
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'capturas-store')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path; $DistDir = (Resolve-Path $DistDir).Path
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Cs {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettingsW(IntPtr dm, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string dev, int mode, IntPtr dm);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool SystemParametersInfoW(uint a, uint b, string c, uint d);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RC { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] struct IN { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MI mi; }
  [DllImport("user32.dll")] static extern uint SendInput(uint n, IN[] i, int size);
  [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  public struct PT { public int X, Y; }
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(150); mouse_event(2, 0, 0, 0, IntPtr.Zero); mouse_event(4, 0, 0, 0, IntPtr.Zero); }
  /// Movimiento por la ruta de entrada de Windows (como un ratón físico), en pasos.
  public static void Glide(int x, int y, int steps) {
    PT p; GetCursorPos(out p); int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
    for (int i = 1; i <= steps; i++) { int cx = p.X + (x - p.X) * i / steps, cy = p.Y + (y - p.Y) * i / steps; var a = new IN[1]; a[0].type = 0; a[0].mi.dx = (int)(cx * 65535L / (sw - 1)); a[0].mi.dy = (int)(cy * 65535L / (sh - 1)); a[0].mi.flags = 0x8001; SendInput(1, a, Marshal.SizeOf(typeof(IN))); System.Threading.Thread.Sleep(20); }
  }
  /// Modos de pantalla admitidos (DEVMODEW de 220 bytes: ancho@172, alto@176, bits@168).
  public static string Modes() {
    var set = new SortedSet<string>(); IntPtr p = Marshal.AllocHGlobal(220);
    for (int i = 0; ; i++) { for (int k = 0; k < 220; k += 4) Marshal.WriteInt32(p, k, 0); Marshal.WriteInt16(p, 68, 220); if (!EnumDisplaySettingsW(null, i, p)) break; set.Add(Marshal.ReadInt32(p, 172) + "x" + Marshal.ReadInt32(p, 176)); }
    Marshal.FreeHGlobal(p); return string.Join(", ", set);
  }
  /// flags 2 = solo comprobar (CDS_TEST), 0 = aplicar. Devuelve DISP_CHANGE_* (0 = correcto).
  public static int SetMode(int w, int h, uint flags) {
    IntPtr p = Marshal.AllocHGlobal(220); for (int k = 0; k < 220; k += 4) Marshal.WriteInt32(p, k, 0); Marshal.WriteInt16(p, 68, 220);
    EnumDisplaySettingsW(null, -1, p); Marshal.WriteInt32(p, 172, w); Marshal.WriteInt32(p, 176, h); Marshal.WriteInt32(p, 72, 0x180000);
    int r = ChangeDisplaySettingsW(p, flags); Marshal.FreeHGlobal(p); return r;
  }
  public static bool Wallpaper(string path) { return SystemParametersInfoW(0x14, 0, path, 3); }
  public static string WindowsOf(int pid) {
    var sb = new StringBuilder();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); var c = new StringBuilder(64); GetClassName(h, c, 64); sb.Append(c + " " + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + "; "); } return true; }, IntPtr.Zero);
    return sb.ToString();
  }
  public static IntPtr MainWindowOf(int pid, string cls) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var c = new StringBuilder(64); GetClassName(h, c, 64); if (cls == null || c.ToString() == cls) { found = h; return false; } } return true; }, IntPtr.Zero);
    return found;
  }
  public static string TitleOf(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
}
"@
$report = New-Object System.Collections.Generic.List[string]
function Note([string]$s) { Write-Host $s; $report.Add($s) }
function WaitFor([scriptblock]$cond, [int]$sec = 20) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 150 }; return [bool](& $cond) }
function LogHas($log, [string]$t) { (Test-Path $log) -and (Select-String -Path $log -SimpleMatch -Pattern $t -Quiet) }
Note "# Capturas Store · informe automático"; Note ''
Note "Commit: $env:GITHUB_SHA · $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') UTC-local del runner"

# ── 1. Resolución: la mayor que admita la pantalla (Microsoft recomienda ≥ 1366×768)
Note ''; Note '## Pantalla'
Note "Modos admitidos por el adaptador: $([Cs]::Modes())"
$applied = $null
foreach ($m in @(@(2560, 1440), @(1920, 1080), @(1680, 1050), @(1600, 900), @(1440, 900), @(1366, 768), @(1280, 800), @(1280, 720))) {
  $t = [Cs]::SetMode($m[0], $m[1], 2); Note "- probar $($m[0])x$($m[1]) (CDS_TEST) → $t"
  if ($t -eq 0 -and -not $applied) { $r = [Cs]::SetMode($m[0], $m[1], 0); Note "  aplicar → $r"; if ($r -eq 0) { $applied = $m; break } }
}
Start-Sleep -Seconds 3
$sc = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; Note "Pantalla tras el ajuste: **$($sc.Width)x$($sc.Height)** (aplicado: $(if ($applied) { "$($applied[0])x$($applied[1])" } else { 'ningún modo mayor disponible' }))"
$wp = 'C:\Windows\Web\4K\Wallpaper\Windows\img0_1920x1200.jpg'; if (-not (Test-Path $wp)) { $wp = (Get-ChildItem 'C:\Windows\Web\Wallpaper' -Recurse -Filter *.jpg | Select-Object -First 1).FullName }
Note "Fondo de pantalla de Windows: $wp → $([Cs]::Wallpaper($wp))"; Start-Sleep -Seconds 2

# ── 2. Limpieza y configuración AISLADA (carpeta temporal; no toca %APPDATA%)
Get-Process -Name 'HatsuDesktop', 'HatsuDesktop.Bar', 'HatsuDesktop.Dock', 'HatsuDesktop.Connect', 'NotchWin.App', 'notepad', 'calc', 'CalculatorApp', 'chrome' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$d = Join-Path ([IO.Path]::GetTempPath()) ('hatsu-store-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)); New-Item -ItemType Directory -Force -Path $d | Out-Null
$cfg = @{ version = 3; hideTaskbar = $true; modules = @{ bar = @{ enabled = $true; args = @('--no-config') }; dock = @{ enabled = $true; args = @('--no-config') }; notch = @{ enabled = $true }; connect = @{ enabled = $false } }; onboarding = @{ status = 'Completed' } }
$cfg | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $d 'winmac.json') -Encoding UTF8
Note ''; Note "Configuración aislada: $d\winmac.json (módulos: Barra, Dock y Hatsu Notch activados; barra de tareas de Windows oculta —función real de Hatsu—; Connect desactivado: no hay iPhone)"

# ── 3. Sesión multimedia REAL: Chrome reproduce un tono generado aquí (sin derechos de terceros) con título/artista/carátula (Media Session API → SMTC de Windows)
$sr = 44100; $n = $sr * 30; $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
$bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $n * 2)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$sr); $bw.Write([int]($sr * 2)); $bw.Write([int16]2); $bw.Write([int16]16); $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]($n * 2))
$freqs = @(220.0, 277.18, 329.63, 440.0)
for ($i = 0; $i -lt $n; $i++) { $t = $i / $sr; $f = $freqs[[int]([Math]::Floor($t * 2)) % 4]; $env = [Math]::Exp(-3 * (($t * 2) % 1)); $bw.Write([int16](9000 * $env * [Math]::Sin(2 * [Math]::PI * $f * $t))) }
[IO.File]::WriteAllBytes((Join-Path $d 'tono.wav'), $ms.ToArray())
$logo = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'assets\brand\hatsu-flux-256.png')))
@"
<!doctype html><meta charset="utf-8"><title>Tono de demostración</title><body style="font:16px Segoe UI;padding:2em"><h3>Tono de demostración (generado para las capturas)</h3>
<audio id="a" src="tono.wav" loop controls></audio>
<script>
navigator.mediaSession.metadata = new MediaMetadata({ title: 'Tono de demostración', artist: 'Hatsu Desktop', album: 'Capturas de la Microsoft Store', artwork: [{ src: 'data:image/png;base64,$logo', sizes: '256x256', type: 'image/png' }] });
navigator.mediaSession.setActionHandler('play', () => a.play()); navigator.mediaSession.setActionHandler('pause', () => a.pause());
navigator.mediaSession.setActionHandler('previoustrack', () => { a.currentTime = 0 }); navigator.mediaSession.setActionHandler('nexttrack', () => { a.currentTime = 0 });
a.play().catch(e => { document.title = 'ERROR ' + e });
</script>
"@ | Set-Content (Join-Path $d 'demo.html') -Encoding UTF8
$chrome = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
$page = 'file:///' + ((Join-Path $d 'demo.html') -replace '\\', '/')
$cp = Start-Process $chrome -ArgumentList @("--user-data-dir=`"$(Join-Path $d 'chrome')`"", '--no-first-run', '--no-default-browser-check', '--autoplay-policy=no-user-gesture-required', '--allow-file-access-from-files', '--window-position=20,80', '--window-size=640,300', $page) -PassThru
Start-Sleep -Seconds 6
Note ''; Note '## Medios'; Note "Chrome (pid $($cp.Id)) reproduciendo el tono: ventana principal «$([Cs]::TitleOf([Cs]::MainWindowOf($cp.Id, $null)))»"

# Entorno del runner: se minimizan las ventanas ajenas (consola del agente del runner, Chrome) y se ocultan los iconos del escritorio (como «Ver › Mostrar iconos del escritorio»)
(New-Object -ComObject Shell.Application).MinimizeAll(); Start-Sleep -Seconds 1
$prog = [Cs]::FindWindow('Progman', $null); [Cs]::PostMessage($prog, 0x111, [IntPtr]0x7402, [IntPtr]::Zero) | Out-Null; Start-Sleep -Seconds 1
# ── 4. Aplicaciones reales abiertas (para Dock y menú global)
$apps = @()
$apps += Start-Process explorer.exe -ArgumentList 'C:\Windows\Web' -PassThru
$apps += Start-Process notepad.exe -PassThru
$apps += Start-Process calc.exe -PassThru
Start-Sleep -Seconds 4

# ── 5. Hatsu REAL: host + Barra + Dock + Hatsu Notch
$hostLog = Join-Path $d 'host.log'; $hostExe = Join-Path $DistDir 'HatsuDesktop.exe'
$hp = Start-Process -FilePath $hostExe -ArgumentList @('--fast', '--no-onboarding', '--notchwin-settings', "`"$(Join-Path $d 'notch-settings.json')`"", '--bar-json', "`"$(Join-Path $d 'bar.json')`"", '--config', "`"$(Join-Path $d 'winmac.json')`"", '--log', "`"$hostLog`"") -PassThru
$up = WaitFor { (LogHas $hostLog '[notch] Running') -and (LogHas $hostLog '[bar] Running') -and (LogHas $hostLog '[dock] Running') } 90
Note ''; Note '## Procesos reales'; Note "Host, Barra, Dock y Hatsu Notch en estado Running: **$up**"
Start-Sleep -Seconds 8
foreach ($n2 in 'HatsuDesktop', 'HatsuDesktop.Bar', 'HatsuDesktop.Dock', 'NotchWin.App') { $p = Get-Process -Name $n2 -ErrorAction SilentlyContinue | Select-Object -First 1; Note "- $n2 : $(if ($p) { "pid $($p.Id), $([int]($p.WorkingSet64 / 1MB)) MB, ruta $($p.Path)" } else { 'NO está en ejecución' })" }
Note "- Hatsu Notch, ventanas: $((Get-Process NotchWin.App -ErrorAction SilentlyContinue | ForEach-Object { [Cs]::WindowsOf($_.Id) }) -join ' ')"
Note "- Dock, ventanas: $((Get-Process HatsuDesktop.Dock -ErrorAction SilentlyContinue | ForEach-Object { [Cs]::WindowsOf($_.Id) }) -join ' ')"
Note "- Barra, ventanas: $((Get-Process HatsuDesktop.Bar -ErrorAction SilentlyContinue | ForEach-Object { [Cs]::WindowsOf($_.Id) }) -join ' ')"

# Disposición de las ventanas (ajuste del ESCENARIO, no de Hatsu): Bloc de notas a la izquierda, Calculadora encima, Explorador a la derecha hasta cerca del borde inferior
# (cubre la marca de agua «Test Mode / Windows Server» que pinta el sistema del runner sobre el fondo)
$W = $sc.Width; $H = $sc.Height
function Place([IntPtr]$h, [int]$x, [int]$y, [int]$w, [int]$hh) { if ($h -ne [IntPtr]::Zero) { [Cs]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 300; [Cs]::SetWindowPos($h, [IntPtr]::Zero, $x, $y, $w, $hh, 0x44) | Out-Null } }
$hNote = [Cs]::FindWindow('Notepad', $null); $hCalc = [Cs]::FindWindow('ApplicationFrameWindow', 'Calculator'); $hExp = [Cs]::FindWindow('CabinetWClass', $null)
Note ''; Note "## Ventanas de las aplicaciones: Bloc de notas=$hNote · Calculadora=$hCalc · Explorador=$hExp"
for ($k = 0; $k -lt 2; $k++) {
  Place $hExp ([int]($W * 0.50)) 80 ([int]($W * 0.47)) ($H - 80 - 85)
  Place $hNote 50 80 ([int]($W * 0.52)) ([int]($H * 0.62))
  Place $hCalc 90 ([int]($H * 0.40)) 330 ([int]($H * 0.50))
  Start-Sleep -Milliseconds 700
}
foreach ($pair in @(@('Explorador', $hExp), @('Bloc de notas', $hNote), @('Calculadora', $hCalc))) { if ($pair[1] -ne [IntPtr]::Zero) { $r = New-Object Cs+RC; [Cs]::GetWindowRect($pair[1], [ref]$r) | Out-Null; Note "- $($pair[0]): $($r.L),$($r.T) $($r.R - $r.L)x$($r.B - $r.T)" } }
# Foco real en el Bloc de notas (clic de ratón en su área de texto) para que la Barra muestre su menú global
$cx = $sc.X + [int]($W / 2)
if ($hNote -ne [IntPtr]::Zero) { $r = New-Object Cs+RC; [Cs]::GetWindowRect($hNote, [ref]$r) | Out-Null; [Cs]::Click([int](($r.L + $r.R) / 2), [int]($r.T + ($r.B - $r.T) * 0.5)) }
[Cs]::SetCursorPos($sc.X + 40, $sc.Y + [int]($H * 0.5)) | Out-Null; Start-Sleep -Seconds 3

function Shot([string]$name, [string]$what) {
  $bmp = New-Object System.Drawing.Bitmap $sc.Width, $sc.Height; $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($sc.X, $sc.Y, 0, 0, $bmp.Size, [System.Drawing.CopyPixelOperation]::SourceCopy); $g.Dispose()
  $path = Join-Path $OutDir "$name.png"; $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  Note ("- **{0}.png** ({1}x{2}, {3:N0} KB): {4}" -f $name, $sc.Width, $sc.Height, ((Get-Item $path).Length / 1KB), $what)
}
Note ''; Note '## Capturas (pantalla real completa)'
Shot '00-diagnostico-estado-inicial' 'estado tras arrancar (diagnóstico: no es una captura para la Store)'
# Captura 1 — escritorio completo
Shot '01-escritorio' 'Escritorio con Barra, Dock y Hatsu Notch (reposo) y aplicaciones reales abiertas'
# Captura 2 — Hatsu Notch expandido con la reproducción real
Note "- Hatsu Notch antes de expandir: $((Get-Process NotchWin.App -ErrorAction SilentlyContinue | ForEach-Object { [Cs]::WindowsOf($_.Id) }) -join ' ')"
[Cs]::Glide($cx, $sc.Y + 12, 12); Start-Sleep -Milliseconds 500; [Cs]::Click($cx, $sc.Y + 12); Start-Sleep -Milliseconds 1800
Note "- Hatsu Notch tras el clic: $((Get-Process NotchWin.App -ErrorAction SilentlyContinue | ForEach-Object { [Cs]::WindowsOf($_.Id) }) -join ' ')"
Shot '02-hatsu-notch-expandido' 'Hatsu Notch expandido con la reproducción multimedia real (tono generado, sesión de Chrome)'
[Cs]::Glide($sc.X + 40, $sc.Y + [int]($H * 0.5), 25); Start-Sleep -Milliseconds 1500
Shot '02b-hatsu-notch-contraido' 'Hatsu Notch contraído (reproduciendo)'
# Captura 3 — Dock con el ratón sobre un icono (ampliación y vista previa reales del código)
$dockY = $sc.Bottom - 50
[Cs]::Glide($cx, $dockY, 20); Start-Sleep -Milliseconds 300; [Cs]::Glide(690, $dockY, 15); Start-Sleep -Milliseconds 1500
Shot '03-dock' 'Dock con el ratón sobre el icono del Explorador (ampliación y vista previa de ventana)'
[Cs]::Glide($sc.X + 40, $sc.Y + [int]($H * 0.5), 25); Start-Sleep -Milliseconds 1200
Shot '03b-dock-reposo' 'Dock en reposo'
# Captura 4 — Ajustes REAL: menú de Hatsu de la Barra (clic real en el logotipo) → «Ajustes de Hatsu Desktop»
[Cs]::Click(27, 14); Start-Sleep -Milliseconds 1500
Shot '05-diag-menu-hatsu' 'diagnóstico: menú de Hatsu de la Barra abierto con un clic real en el logotipo'
$mx = [int]$env:HATSU_SETTINGS_X; $my = [int]$env:HATSU_SETTINGS_Y
if ($mx -gt 0 -and $my -gt 0) {
  [Cs]::Click($mx, $my); $sw = [IntPtr]::Zero
  $ok = WaitFor { $script:sw = [Cs]::FindWindow('WinMacSettings2', $null); $script:sw -ne [IntPtr]::Zero -and [Cs]::IsWindowVisible($script:sw) } 10
  Note "- Ventana de Ajustes visible: $ok"
  if ($ok) { Start-Sleep -Milliseconds 1500; $r = New-Object Cs+RC; [Cs]::GetWindowRect($sw, [ref]$r) | Out-Null; Note "  posición: $($r.L),$($r.T) $($r.R - $r.L)x$($r.B - $r.T)" }
  Shot '04-ajustes' 'Ajustes de Hatsu Desktop (ventana real)'
}

# Diagnóstico final
Note ''; Note '## Registro del host (últimas líneas)'
if (Test-Path $hostLog) { Get-Content $hostLog | Select-Object -Last 25 | ForEach-Object { Note "    $_" } }
Note ''; Note '## Versión de los binarios'
foreach ($f in 'HatsuDesktop.dll', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'NotchWin\NotchWin.App.exe') { $p = Join-Path $DistDir $f; if (Test-Path $p) { Note "- $f · $(((Get-Item $p).VersionInfo.FileVersion)) · SHA-256 $((Get-FileHash $p).Hash.Substring(0, 16))…" } }
$report | Set-Content (Join-Path $OutDir 'informe.md') -Encoding UTF8
Start-Process -FilePath $hostExe -ArgumentList '--quit' -Wait -WindowStyle Hidden | Out-Null
Write-Host 'Capturas terminadas.'; exit 0
