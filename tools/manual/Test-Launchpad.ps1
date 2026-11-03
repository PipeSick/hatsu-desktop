<#
  Launchpad REAL en el runner de Windows (docs/22): barra real, índice de apps de Windows (shell:AppsFolder), apertura, búsqueda con el teclado, abrir una app con Intro
  y cerrar con Esc. La tecla Windows real NO se puede probar aquí: el gancho ignora a propósito las teclas inyectadas (SendInput), así que se abre con «--launchpad»
  (la misma función que llama el gancho). Se comprueba que el gancho se instala. Captura: launchpad-real.png.
#>
param([string]$PublishDir = 'ci-publish', [string]$OutDir = 'launchpad')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path; $PublishDir = (Resolve-Path $PublishDir).Path
$bar = "$PublishDir/Bar.App/WinMac.Bar.App.exe"
$log = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'
$script:fails = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') { $r = if ($ok) { 'PASS' } else { 'FAIL'; $script:fails++ }; Write-Host ("{0,-4} {1}  {2}" -f $r, $name, $detail) }
function WaitFor([scriptblock]$cond, [int]$sec = 20) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 200 }; return [bool](& $cond) }
function LogHas([string]$t) { (Test-Path $log) -and (Select-String -Path $log -SimpleMatch -Pattern $t -Quiet) }
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Lp {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  public struct RC { public int L, T, R, B; }
  public static bool Visible() { var h = FindWindow("WinMacLaunchpad", null); return h != IntPtr.Zero && IsWindowVisible(h); }
  public static bool SpotVisible() { var h = FindWindow("WinMacSpotlight", null); return h != IntPtr.Zero && IsWindowVisible(h); }
  public static bool Foreground() { var h = FindWindow("WinMacLaunchpad", null); return h != IntPtr.Zero && GetForegroundWindow() == h; }
  public static string Rect() { var h = FindWindow("WinMacLaunchpad", null); RC r; GetWindowRect(h, out r); return r.L + "," + r.T + "," + (r.R - r.L) + "x" + (r.B - r.T); }
}
"@

Get-Process -Name 'WinMac.Bar.App', 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }
Remove-Item (Join-Path $env:LOCALAPPDATA 'WinMac\launcher.json') -ErrorAction SilentlyContinue
$cfgFile = Join-Path $OutDir 'bar-ci.json'; '{ "AppMenus": true }' | Set-Content $cfgFile -Encoding UTF8   # Global Menu activado para la prueba de menús
$p = Start-Process -FilePath $bar -ArgumentList @('--config', "`"$cfgFile`"") -PassThru
Check 'La barra real arranca' (WaitFor { LogHas 'Barra superior iniciada' } 30)
Check 'Gancho de la tecla Windows instalado (solo deja pasar teclas; no bloquea ninguna)' (WaitFor { LogHas '[launchpad] tecla Windows: gancho instalado' } 15)
$idx = WaitFor { LogHas '[launchpad] índice de apps:' } 40
$line = if (Test-Path $log) { (Select-String -Path $log -SimpleMatch '[launchpad] índice de apps:' | Select-Object -Last 1).Line } else { '' }
$n = if ($line -match 'apps: (\d+)') { [int]$Matches[1] } else { 0 }
Check 'Índice de apps leído de Windows (shell:AppsFolder) con apps reales' ($idx -and $n -ge 5) $line

& $bar --launchpad | Out-Null
$open = WaitFor { [Lp]::Visible() } 10
Check 'Launchpad se abre y cubre la pantalla' $open ("ventana: " + $(if ($open) { [Lp]::Rect() } else { 'no visible' }))
Start-Sleep -Milliseconds 900
$sc = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $sc.Width, $sc.Height; $g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($sc.X, $sc.Y, 0, 0, $bmp.Size, [System.Drawing.CopyPixelOperation]::SourceCopy); $g.Dispose()
$bmp.Save((Join-Path $OutDir 'launchpad-real.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$colors = @{}; for ($y = [int]($sc.Height * 0.25); $y -lt [int]($sc.Height * 0.85); $y += 7) { for ($x = [int]($sc.Width * 0.1); $x -lt [int]($sc.Width * 0.9); $x += 7) { $colors[$bmp.GetPixel($x, $y).ToArgb()] = 1 } }
$bmp.Dispose()
Check 'La cuadrícula se dibuja (muchos colores distintos: iconos y nombres)' ($colors.Count -ge 40) "colores distintos: $($colors.Count)"
$fg = WaitFor { [Lp]::Foreground() } 3
Write-Host "Launchpad en primer plano: $fg"

[System.Windows.Forms.SendKeys]::SendWait('notepad'); Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
$launched = WaitFor { (Get-Process -Name 'notepad' -ErrorAction SilentlyContinue).Count -ge 1 } 15
Check 'Escribir «notepad» + Intro abre el Bloc de notas (búsqueda + lanzador real)' $launched
Check 'Launchpad se cierra al abrir una app' (WaitFor { -not [Lp]::Visible() } 5)
Check 'El uso queda registrado para ordenar la búsqueda (launcher.json)' (WaitFor { (Test-Path (Join-Path $env:LOCALAPPDATA 'WinMac\launcher.json')) -and (Get-Content (Join-Path $env:LOCALAPPDATA 'WinMac\launcher.json') -Raw) -match '(?i)notepad' } 5)
Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

& $bar --launchpad | Out-Null
$again = WaitFor { [Lp]::Visible() } 10
if ($again) { $null = WaitFor { [Lp]::Foreground() } 3; [System.Windows.Forms.SendKeys]::SendWait('{ESC}') }
Check 'Esc cierra Launchpad' ($again -and (WaitFor { -not [Lp]::Visible() } 5))
& $bar --launchpad | Out-Null; $null = WaitFor { [Lp]::Visible() } 10
& $bar --launchpad | Out-Null
Check 'Pedirlo otra vez lo cierra (como la tecla Windows en macOS)' (WaitFor { -not [Lp]::Visible() } 5)

# ── Spotlight (mismo índice, búsqueda y lanzador)
Check 'Atajo de Spotlight (Alt+Espacio) registrado o, si está ocupado, dicho en el registro' ((LogHas '[spotlight] atajo Alt+Espacio registrado') -or (LogHas '[spotlight] el atajo Alt+Espacio no está libre'))
& $bar --spotlight | Out-Null
$sv = WaitFor { [Lp]::SpotVisible() } 10
Check 'Spotlight se abre' $sv
if ($sv) {
  $null = WaitFor { [Lp]::GetForegroundWindow() -eq [Lp]::FindWindow('WinMacSpotlight', $null) } 3
  [System.Windows.Forms.SendKeys]::SendWait('12*12'); Start-Sleep -Milliseconds 500
  $shot = New-Object System.Drawing.Bitmap $sc.Width, $sc.Height; $gg = [System.Drawing.Graphics]::FromImage($shot); $gg.CopyFromScreen($sc.X, $sc.Y, 0, 0, $shot.Size, [System.Drawing.CopyPixelOperation]::SourceCopy); $gg.Dispose()
  $shot.Save((Join-Path $OutDir 'spotlight-real.png'), [System.Drawing.Imaging.ImageFormat]::Png); $shot.Dispose()
  Set-Clipboard -Value 'vacío'
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  $clip = ''; $null = WaitFor { $script:clip = (Get-Clipboard -Raw); $script:clip -match '144' } 5
  Check 'Calculadora: «12*12» + Intro copia 144 al portapapeles' ($clip -match '^144\s*$') "portapapeles: $clip"
  Check 'Spotlight se cierra tras usar un resultado' (WaitFor { -not [Lp]::SpotVisible() } 5)
  & $bar --spotlight | Out-Null; $null = WaitFor { [Lp]::SpotVisible() } 10; Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('notepad'); Start-Sleep -Milliseconds 500; [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Check 'Spotlight: «notepad» + Intro abre el Bloc de notas (índice y lanzador compartidos con Launchpad)' (WaitFor { (Get-Process -Name 'notepad' -ErrorAction SilentlyContinue).Count -ge 1 } 15)
  Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  & $bar --spotlight | Out-Null; $null = WaitFor { [Lp]::SpotVisible() } 10; Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Check 'Esc cierra Spotlight' (WaitFor { -not [Lp]::SpotVisible() } 5)
}
if (Test-Path $log) { Select-String -Path $log -Pattern '\[spotlight\]' | Select-Object -Last 8 | ForEach-Object { Write-Host "  bar.log> $($_.Line)" } }

# ── Global Menu (opción A): la barra descubre los menús REALES de la app activa sin quitárselos
$np = Start-Process notepad.exe -PassThru
$menus = WaitFor { (Test-Path $log) -and (Select-String -Path $log -Pattern '\[menús\] .*: \d+ menús \((Win32|Automation)\)' -Quiet) } 20
$ml = if (Test-Path $log) { (Select-String -Path $log -Pattern '\[menús\]' | Select-Object -Last 3 | ForEach-Object Line) -join ' | ' } else { '' }
Check 'Global Menu: los menús del Bloc de notas (app real) aparecen en la barra' $menus $ml
Get-Process -Name 'notepad' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

& $bar --quit | Out-Null
Check 'La barra se cierra y el gancho se va con ella' (WaitFor { $p.HasExited } 20)
Get-Process -Name 'WinMac.Bar.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $log) { Select-String -Path $log -Pattern '\[launchpad\]' | Select-Object -Last 12 | ForEach-Object { Write-Host "  bar.log> $($_.Line)" } }
if ($script:fails) { Write-Host "::error title=Launchpad::$($script:fails) comprobación(es) fallaron"; exit 1 }
Write-Host 'Launchpad: todas las comprobaciones pasaron.'; exit 0
