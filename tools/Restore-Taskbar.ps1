<#
.SYNOPSIS
  WM-034: vuelve a mostrar la barra de tareas de Windows si WinMac la dejó oculta y no puede arrancar.
.DESCRIPTION
  No necesita HatsuDesktop.exe ni permisos de administrador. Muestra las barras de tareas de Explorer (todas las pantallas) y, si existe el registro
  %LOCALAPPDATA%\WinMac\taskbar-restore.json, devuelve «Ocultar automáticamente la barra de tareas» al valor que tenías antes. Después borra ese registro.
  Alternativas equivalentes: «HatsuDesktop.exe --restore-taskbar», reiniciar el Explorador de Windows o cerrar sesión (y, si hiciera falta,
  Configuración › Personalización › Barra de tareas › Comportamientos › «Ocultar automáticamente la barra de tareas»).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Restore-Taskbar.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -Namespace WinMac -Name Tb -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public struct APPBARDATA { public uint cbSize; public System.IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public int l, t, r, b; public System.IntPtr lParam; }
[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern System.IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr FindWindowExW(System.IntPtr parent, System.IntPtr after, string cls, string title);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetShellWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@
$shellPid = [uint32]0; [void][WinMac.Tb]::GetWindowThreadProcessId([WinMac.Tb]::GetShellWindow(), [ref]$shellPid)
$bars = @()
foreach ($cls in 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd') {
  $h = [IntPtr]::Zero
  while (($h = [WinMac.Tb]::FindWindowExW([IntPtr]::Zero, $h, $cls, $null)) -ne [IntPtr]::Zero) {
    $p = [uint32]0; [void][WinMac.Tb]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $shellPid) { $bars += $h }   # solo las de Explorer (la Barra de WinMac tiene una ventana propia con la misma clase)
  }
}
if ($bars.Count -eq 0) { Write-Host 'Explorer no está en marcha: al iniciarse, su barra de tareas aparece visible.' }
$lease = Join-Path $env:LOCALAPPDATA 'WinMac\taskbar-restore.json'
if ((Test-Path $lease) -and $bars.Count -gt 0) {
  try {
    $l = Get-Content $lease -Raw | ConvertFrom-Json
    $d = New-Object WinMac.Tb+APPBARDATA; $d.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($d); $d.hWnd = $bars[0]
    $d.lParam = [IntPtr]$(if ($l.OriginalAutoHide) { 1 } else { 2 })
    [void][WinMac.Tb]::SHAppBarMessage(10, [ref]$d)   # ABM_SETSTATE: el valor que tenías
    Write-Host ("«Ocultar automáticamente» devuelto a: " + $(if ($l.OriginalAutoHide) { 'activado' } else { 'desactivado' }))
  } catch { Write-Host 'El registro de WinMac no se pudo leer: «Ocultar automáticamente» no se cambió.' }
}
foreach ($h in $bars) { [void][WinMac.Tb]::ShowWindow($h, 8) }   # SW_SHOWNA
if (Test-Path $lease) { Remove-Item $lease -Force }
Write-Host "Barra de tareas visible ($($bars.Count) pantalla(s))."
