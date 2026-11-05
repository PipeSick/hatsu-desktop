<#
  Reconocimiento del entorno de captura (solo lectura, informativo: nunca falla). Responde: ¿hay sesión de escritorio interactiva?, ¿qué resoluciones admite la pantalla?,
  ¿hay composición de escritorio (DWM)?, ¿fondos de pantalla de Windows?, ¿audio?, ¿puede existir una sesión multimedia REAL (SMTC) para Hatsu Notch?
#>
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Pr {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)] public struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields; public int dmPositionX, dmPositionY; public int dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
  [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
  [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled(out bool on);
  [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
}
"@
function P([string]$s) { Write-Host "PROBE| $s" }
P "OS: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
$sc = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; P "Pantalla principal: $($sc.Width)x$($sc.Height) · pantallas: $([System.Windows.Forms.Screen]::AllScreens.Count) · DPI: $([System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero).DpiX)"
$vc = Get-CimInstance Win32_VideoController | Select-Object -First 3; foreach ($v in $vc) { P "Adaptador: $($v.Name) · driver $($v.DriverVersion) · modo actual $($v.CurrentHorizontalResolution)x$($v.CurrentVerticalResolution)" }
$on = $false; [void][Pr]::DwmIsCompositionEnabled([ref]$on); P "Composición DWM activa: $on"
P "Sesión: id del proceso $([Diagnostics.Process]::GetCurrentProcess().SessionId) · sesión de consola $([Pr]::WTSGetActiveConsoleSessionId()) · UserInteractive=$([Environment]::UserInteractive)"
try { P ("Procesos de escritorio: " + ((Get-Process explorer, dwm, winlogon -ErrorAction SilentlyContinue | ForEach-Object { "$($_.Name)#$($_.Id)" }) -join ', ')) } catch { }
$dm = New-Object Pr+DEVMODE; $dm.dmSize = [int16]([Runtime.InteropServices.Marshal]::SizeOf($dm)); $modes = @{}
for ($i = 0; [Pr]::EnumDisplaySettings($null, $i, [ref]$dm); $i++) { $modes["$($dm.dmPelsWidth)x$($dm.dmPelsHeight)"] = 1 }
P ("Modos de pantalla admitidos: " + (($modes.Keys | Sort-Object { [int]($_ -split 'x')[0] } ) -join ', '))
$wp = @('C:\Windows\Web\Wallpaper', 'C:\Windows\Web\4K\Wallpaper') | Where-Object { Test-Path $_ }; foreach ($w in $wp) { P "Fondos en $w : $((Get-ChildItem $w -Recurse -Include *.jpg, *.png -ErrorAction SilentlyContinue | Select-Object -First 8 | ForEach-Object { $_.FullName.Replace($w, '') }) -join ' | ')" }
try { P "Audio (Win32_SoundDevice): $((Get-CimInstance Win32_SoundDevice | ForEach-Object { $_.Name }) -join ', ')" } catch { P "Audio: no consultable" }
P "Media Foundation: mfplat.dll $(Test-Path C:\Windows\System32\mfplat.dll) · mfmediaengine.dll $(Test-Path C:\Windows\System32\mfmediaengine.dll)"
$apps = @('C:\Windows\System32\notepad.exe', 'C:\Windows\System32\calc.exe', 'C:\Windows\explorer.exe', 'C:\Windows\System32\mspaint.exe', 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe', "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "$env:ProgramFiles\Mozilla Firefox\firefox.exe", "$env:ProgramFiles\Windows Media Player\wmplayer.exe", "$env:ProgramFiles\VideoLAN\VLC\vlc.exe", "$env:ProgramFiles\Notepad++\notepad++.exe", "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe", "$env:ProgramFiles\Microsoft VS Code\Code.exe")
P ("Aplicaciones presentes: " + (($apps | Where-Object { Test-Path $_ } | ForEach-Object { Split-Path $_ -Leaf }) -join ', '))
P ("Accesos del menú Inicio: " + (Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter *.lnk -ErrorAction SilentlyContinue).Count)
# ¿Puede existir una sesión multimedia REAL (GSMTC)? Windows PowerShell 5.1 + WinRT: un MediaPlayer real reproduce un WAV propio (tono generado) con título y artista.
$ps = @'
$ErrorActionPreference = 'Continue'
function P([string]$s) { Write-Host "PROBE| $s" }
try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $null = [Windows.Media.Playback.MediaPlayer, Windows.Media.Playback, ContentType = WindowsRuntime]
  $null = [Windows.Media.Core.MediaSource, Windows.Media.Core, ContentType = WindowsRuntime]
  $null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
  $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
  $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
  function Await($op, [Type]$t) { $m = $asTask.MakeGenericMethod($t); $task = $m.Invoke($null, @($op)); $task.Wait(15000) | Out-Null; $task.Result }
  # WAV de 6 s (tono de 440 Hz, generado aquí: sin derechos de terceros)
  $wav = Join-Path $env:TEMP 'hatsu-tono.wav'; $sr = 44100; $n = $sr * 6; $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
  $bw.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $bw.Write([int](36 + $n * 2)); $bw.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $bw.Write([int]16); $bw.Write([int16]1); $bw.Write([int16]1); $bw.Write([int]$sr); $bw.Write([int]($sr * 2)); $bw.Write([int16]2); $bw.Write([int16]16); $bw.Write([Text.Encoding]::ASCII.GetBytes('data')); $bw.Write([int]($n * 2))
  for ($i = 0; $i -lt $n; $i++) { $bw.Write([int16](8000 * [Math]::Sin(2 * [Math]::PI * 440 * $i / $sr))) }; [IO.File]::WriteAllBytes($wav, $ms.ToArray())
  $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($wav)) ([Windows.Storage.StorageFile])
  $mp = New-Object Windows.Media.Playback.MediaPlayer
  $mp.Source = [Windows.Media.Core.MediaSource]::CreateFromStorageFile($file)
  $mp.Play(); Start-Sleep -Seconds 3
  P "MediaPlayer creado: estado de reproducción = $($mp.PlaybackSession.PlaybackState)"
  $mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
  $ss = $mgr.GetSessions(); P "Sesiones multimedia (GSMTC) visibles: $($ss.Count)"
  foreach ($s in $ss) { P "  sesión: $($s.SourceAppUserModelId)" }
} catch { P "MediaPlayer/GSMTC no disponible: $($_.Exception.GetType().Name): $($_.Exception.Message)" }
'@
$tmp = Join-Path $env:TEMP 'probe-media.ps1'; $ps | Set-Content $tmp -Encoding UTF8
$pp = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$tmp`"") -PassThru -Wait -WindowStyle Hidden -RedirectStandardOutput (Join-Path $env:TEMP 'probe-media.out') -RedirectStandardError (Join-Path $env:TEMP 'probe-media.err')
Get-Content (Join-Path $env:TEMP 'probe-media.out') -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
Get-Content (Join-Path $env:TEMP 'probe-media.err') -ErrorAction SilentlyContinue | Select-Object -First 5 | ForEach-Object { P "stderr: $_" }
# captura de prueba de la pantalla real (¿CopyFromScreen devuelve algo distinto de negro?)
$bmp = New-Object System.Drawing.Bitmap $sc.Width, $sc.Height; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($sc.X, $sc.Y, 0, 0, $bmp.Size); $g.Dispose()
$px = @(); foreach ($pt in @(@(5, 5), @([int]($sc.Width / 2), [int]($sc.Height / 2)), @(($sc.Width - 5), ($sc.Height - 5)))) { $px += ($bmp.GetPixel($pt[0], $pt[1]).Name) }; P "Píxeles de muestra de CopyFromScreen: $($px -join ' ')"; $bmp.Dispose()
P "Fin del reconocimiento"
