<#
.SYNOPSIS
  Diagnóstico de SOLO LECTURA de una instalación de Hatsu Desktop en tu Windows. No cambia nada, no pide desactivar ninguna protección y no envía nada a ningún sitio.
.DESCRIPTION
  Reúne: versión y ruta, firma Authenticode y «marca de la web» de los ejecutables, estado de Smart App Control (solo lectura), eventos recientes de Integridad de código / Defender / AppLocker que nombren
  archivos de Hatsu Desktop, procesos en ejecución, un resumen de la configuración (módulos elegidos y estado del asistente; NO ciudad, calendario ni contenidos), inicio automático, barra de tareas y las
  líneas del registro del host sobre cada módulo. Escribe un archivo de texto que tú revisas antes de compartirlo. Las rutas de tu perfil aparecen como %USERPROFILE%.
  Funciona con Windows PowerShell 5.1 y con PowerShell 7. No necesita administrador (si un registro de eventos no se puede leer sin él, el informe lo dice).
  Uso:   powershell -ExecutionPolicy Bypass -File .\Diagnose-Install.ps1   (o clic derecho › Ejecutar con PowerShell)
         El parámetro -ExecutionPolicy Bypass solo afecta a ESTE script (no cambia la política del equipo); si prefieres, ábrelo y léelo antes: es texto.
#>
[CmdletBinding()]
param([string]$InstallDir = '', [string]$OutFile = '')
$ErrorActionPreference = 'Continue'
$sb = New-Object System.Text.StringBuilder
$profileDir = $env:USERPROFILE; $userName = $env:USERNAME; $machine = $env:COMPUTERNAME
function Redact([string]$t) {
  if ([string]::IsNullOrEmpty($t)) { return $t }
  if ($profileDir) { $t = $t.Replace($profileDir, '%USERPROFILE%') }
  if ($userName -and $userName.Length -ge 3) { $t = [regex]::Replace($t, '(?<=[\\/]Users[\\/])' + [regex]::Escape($userName) + '(?=[\\/]|$)', '<usuario>', 'IgnoreCase') }
  if ($machine -and $machine.Length -ge 3) { $t = [regex]::Replace($t, [regex]::Escape($machine), '<equipo>', 'IgnoreCase') }
  return $t
}
function Out-Line([string]$s = '') { [void]$sb.AppendLine((Redact $s)) }
function Section([string]$s) { [void]$sb.AppendLine(); [void]$sb.AppendLine('== ' + $s) }

# ── ubicar la instalación (sin suponer: primero lo que se pasó, luego esta carpeta, luego la ruta estándar del MSI, luego el registro de desinstalación)
function Find-Install {
  if ($InstallDir -and (Test-Path (Join-Path $InstallDir 'HatsuDesktop.exe'))) { return (Resolve-Path $InstallDir).Path }
  if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'HatsuDesktop.exe'))) { return $PSScriptRoot }
  $std = Join-Path $env:LOCALAPPDATA 'Programs\Hatsu Desktop'; if (Test-Path (Join-Path $std 'HatsuDesktop.exe')) { return $std }
  foreach ($root in 'HKCU:', 'HKLM:') { try { foreach ($k in (Get-ChildItem "$root\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction Stop)) { $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
      if ($p.DisplayName -eq 'Hatsu Desktop' -and $p.InstallLocation -and (Test-Path (Join-Path $p.InstallLocation 'HatsuDesktop.exe'))) { return $p.InstallLocation } } } catch { } }
  return $null
}
function Get-PeInfo([string]$path) {   # cabeceras PE a mano: máquina, fecha de enlazado, subsistema, ¿.NET?, ¿tabla de certificados?
  try {
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite'); $br = New-Object System.IO.BinaryReader($fs)
    try {
      $fs.Position = 0x3C; $pe = $br.ReadInt32(); $fs.Position = $pe; if ($br.ReadUInt32() -ne 0x4550) { return 'no es PE' }
      $machine = $br.ReadUInt16(); $null = $br.ReadUInt16(); $stamp = $br.ReadUInt32(); $fs.Position = $pe + 20; $optSize = $br.ReadUInt16(); $null = $br.ReadUInt16()
      $opt = $pe + 24; $fs.Position = $opt; $magic = $br.ReadUInt16(); $isPlus = ($magic -eq 0x20B); $fs.Position = $opt + 68; $subsystem = $br.ReadUInt16()
      $ddStart = if ($isPlus) { $opt + 112 } else { $opt + 96 }
      $fs.Position = $ddStart + 4 * 8; $certRva = $br.ReadUInt32(); $certSize = $br.ReadUInt32()
      $fs.Position = $ddStart + 14 * 8; $clr = $br.ReadUInt32()
      $when = [DateTimeOffset]::FromUnixTimeSeconds([int64]$stamp).UtcDateTime.ToString('yyyy-MM-dd')
      return ('{0}; enlazado {1}; subsistema {2}; {3}; tabla de certificados {4} bytes' -f $(if ($machine -eq 0x8664) { 'x64' } elseif ($machine -eq 0xAA64) { 'arm64' } else { '0x{0:X}' -f $machine }), $when, $(if ($subsystem -eq 2) { 'ventana' } elseif ($subsystem -eq 3) { 'consola' } else { $subsystem }), $(if ($clr -ne 0) { '.NET (administrado)' } else { 'nativo' }), $certSize)
    } finally { $br.Close(); $fs.Close() }
  } catch { return 'no legible (' + $_.Exception.Message + ')' }
}
function Get-Mark([string]$path) { try { $z = Get-Content -LiteralPath $path -Stream Zone.Identifier -ErrorAction Stop | Where-Object { $_ -like 'ZoneId=*' } | Select-Object -First 1; if ($z) { return $z } else { return 'sin marca' } } catch { return 'sin marca' } }

Add-Type -TypeDefinition @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class HatsuMon {
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MI { public uint Size; public RC Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
  delegate bool Proc(IntPtr h, IntPtr hdc, IntPtr rc, IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, Proc p, IntPtr data);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr h, ref MI mi);
  [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
  public static string[] Read() {
    SetProcessDPIAware(); var l = new List<string>();
    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr h, IntPtr hdc, IntPtr rc, IntPtr data) {
      MI mi = new MI(); mi.Size = (uint)Marshal.SizeOf(typeof(MI)); if (GetMonitorInfoW(h, ref mi)) l.Add(string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}", mi.Device, (mi.Flags & 1) != 0 ? "principal" : "", mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B, mi.Work.T, mi.Work.B, mi.Work.L));
      return true; }, IntPtr.Zero);
    return l.ToArray(); }
}
'@

$dir = Find-Install
[void]$sb.AppendLine('Hatsu Desktop — diagnóstico de la instalación · ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
[void]$sb.AppendLine('SOLO LECTURA: este script no cambia nada, no desactiva ni añade excepciones a ninguna protección, y no envía datos. Revisa el informe antes de compartirlo.')
[void]$sb.AppendLine('Las rutas de tu perfil, tu nombre de usuario y el nombre del equipo se ocultan en el informe.')
Section 'Sistema'
Out-Line ('Windows: ' + [System.Environment]::OSVersion.VersionString + ' · PowerShell ' + $PSVersionTable.PSVersion)
try { $sac = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction Stop).VerifiedAndReputablePolicyState
  $sacText = switch ($sac) { 0 { 'desactivado' } 1 { 'ACTIVADO (bloquea ejecutables sin firma ni reputación)' } 2 { 'en evaluación' } default { "valor $sac" } } } catch { $sacText = 'sin dato legible' }
Out-Line ('Smart App Control: ' + $sacText + '  (solo se LEE; no se pide cambiarlo)')

Section 'Instalación'
if (-not $dir) { Out-Line 'No se encontró HatsuDesktop.exe (ni en esta carpeta, ni en %LOCALAPPDATA%\Programs\Hatsu Desktop, ni en Aplicaciones instaladas).' }
else {
  Out-Line ('Carpeta: ' + $dir); $isMsi = $dir -like '*\Programs\Hatsu Desktop'; Out-Line ('Tipo: ' + $(if ($isMsi) { 'instalación MSI por usuario (ruta estándar)' } else { 'otra carpeta (copia portable o ruta personalizada)' }))
  $main = Join-Path $dir 'HatsuDesktop.exe'; try { Out-Line ('Versión del ejecutable: ' + (Get-Item $main).VersionInfo.FileVersion + ' · Producto: ' + (Get-Item $main).VersionInfo.ProductName) } catch { }
}
$arp = @(); foreach ($root in 'HKCU:', 'HKLM:') { try { foreach ($k in (Get-ChildItem "$root\Software\Microsoft\Windows\CurrentVersion\Uninstall" -ErrorAction Stop)) { $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue; if ($p.DisplayName -eq 'Hatsu Desktop') { $arp += ("{0}: versión {1}" -f ($root + '\...\Uninstall\' + $k.PSChildName), $p.DisplayVersion) } } } catch { } }
Out-Line ('Entrada en Aplicaciones instaladas: ' + $(if ($arp.Count -eq 0) { 'ninguna (¿copia portable?)' } else { $arp -join ' | ' }))

Section 'Archivos del paquete: firma, marca de descarga y cabeceras'
if ($dir) {
  $files = 'HatsuDesktop.exe', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe', 'NotchWin\NotchWin.App.exe'
  foreach ($f in $files) {
    $p = Join-Path $dir $f
    if (-not (Test-Path $p)) { Out-Line ("{0}: NO está" -f $f); continue }
    $item = Get-Item $p; $sig = Get-AuthenticodeSignature -FilePath $p
    $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '—' }
    Out-Line ("{0}" -f $f); Out-Line ("    tamaño {0:N0} bytes · SHA-256 {1}" -f $item.Length, (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant())
    Out-Line ("    firma Authenticode: {0} · firmante: {1}" -f $sig.Status, $signer); Out-Line ("    marca de descarga (Zone.Identifier): " + (Get-Mark $p)); Out-Line ("    cabeceras: " + (Get-PeInfo $p))
  }
  $own = Get-ChildItem $dir -Recurse -File -Include *.exe, *.dll -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(HatsuDesktop|WinMac|NotchWin)' }
  $unsigned = @($own | Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -eq 'NotSigned' })
  Out-Line ("Binarios propios (HatsuDesktop*, WinMac*, NotchWin*): {0} · sin firma: {1}" -f @($own).Count, $unsigned.Count)
  $all = @(Get-ChildItem $dir -Recurse -File -Include *.exe, *.dll -ErrorAction SilentlyContinue); Out-Line ("Total de .exe/.dll en la carpeta: {0} (los del runtime de .NET y de Windows App SDK vienen firmados por Microsoft)" -f $all.Count)
}

Section 'Eventos de seguridad recientes que nombran archivos de Hatsu Desktop (últimos 14 días)'
function Read-Events([string]$log, [int[]]$ids) {
  try {
    $ev = Get-WinEvent -FilterHashtable @{ LogName = $log; Id = $ids; StartTime = (Get-Date).AddDays(-14) } -MaxEvents 400 -ErrorAction Stop | Where-Object { $_.Message -match 'HatsuDesktop|Hatsu Desktop|NotchWin|WinMac' }
    if (-not $ev) { Out-Line ("  {0}: ninguno" -f $log) } else { foreach ($e in ($ev | Select-Object -First 15)) { $m = ($e.Message -replace '\s+', ' '); if ($m.Length -gt 320) { $m = $m.Substring(0, 320) + '…' }; Out-Line ("  {0} · {1} · evento {2}: {3}" -f $log, $e.TimeCreated.ToString('yyyy-MM-dd HH:mm'), $e.Id, $m) } }
  } catch { if ($_.Exception.Message -match 'No events were found|No se encontraron') { Out-Line ("  {0}: ninguno" -f $log) } else { Out-Line ("  {0}: no se pudo leer ({1}); algunos registros solo se leen como administrador — el informe sigue sin ese dato" -f $log, ($_.Exception.Message -replace '\s+', ' ')) } }
}
Read-Events 'Microsoft-Windows-CodeIntegrity/Operational' @(3033, 3034, 3076, 3077, 3089)
Read-Events 'Microsoft-Windows-Windows Defender/Operational' @(1116, 1117)
Read-Events 'Microsoft-Windows-AppLocker/EXE and DLL' @(8003, 8004)
Out-Line '  (3076/3077 = Integridad de código auditó/bloqueó un archivo; 3033/3034 = una DLL no cumplía la directiva; 1116/1117 = Defender detectó/actuó.) El archivo nombrado es el que Windows rechazó.'

Section 'Reserva del borde superior (Barra superior) y área de trabajo de Windows'
$reserve = 'sí (valor de fábrica)'; try { $jj = Get-Content (Join-Path $env:APPDATA 'WinMac\winmac.json') -Raw | ConvertFrom-Json; if ($jj.bar -and $null -ne $jj.bar.reserveSpace) { $reserve = $(if ($jj.bar.reserveSpace) { 'sí' } else { 'NO (la Barra se superpone y no reserva espacio)' }) } } catch { }
Out-Line ('  Reservar el borde superior (ajuste de la Barra): ' + $reserve)
$barRunning = @(Get-Process -Name 'HatsuDesktop.Bar' -ErrorAction SilentlyContinue).Count -gt 0; Out-Line ('  Barra superior en ejecución: ' + $(if ($barRunning) { 'sí' } else { 'no' }))
$anyTop = $false
try {
  foreach ($line in [HatsuMon]::Read()) { $f = $line.Split('|'); $top = [int]$f[6] - [int]$f[3]; $bottom = [int]$f[5] - [int]$f[7]; if ($top -gt 0) { $anyTop = $true }
    Out-Line ('  Monitor {0}{1}: {2}x{3} px en ({4},{5}) · Windows reservó arriba {6} px y abajo {7} px (área de trabajo desde y={8})' -f $f[0].TrimStart('\', '.'), $(if ($f[1]) { ' (principal)' } else { '' }), ([int]$f[4] - [int]$f[2]), ([int]$f[5] - [int]$f[3]), $f[2], $f[3], $top, $bottom, $f[6]) }
  if ($barRunning) { if ($anyTop) { Out-Line '  Lectura: Windows SÍ tiene una banda reservada arriba en algún monitor (coherente con la reserva de la Barra; otra barra también podría causarla).' } else { Out-Line '  Lectura: con la Barra en ejecución, Windows NO tiene ninguna banda reservada arriba: la reserva no está aplicada (mira las líneas de bar.log).' } }
} catch { Out-Line ('  no se pudo leer el área de trabajo: ' + $_.Exception.Message) }
$barLog = Join-Path $env:LOCALAPPDATA 'WinMac\logs\bar.log'
if (Test-Path $barLog) { $bl = @(Get-Content $barLog -Tail 400 -ErrorAction SilentlyContinue | Where-Object { $_ -match 'AppBar|reserva|área de trabajo|ABM_|Explorer' } | Select-Object -Last 25); if ($bl.Count -eq 0) { Out-Line '  bar.log: sin líneas sobre la reserva' } else { Out-Line '  bar.log (reserva):'; foreach ($l in $bl) { Out-Line ('    ' + $l) } } } else { Out-Line '  bar.log no existe' }
Out-Line '  Nota: Hatsu Desktop reserva espacio con la API estándar de barras de aplicación (AppBar); NO mueve ni reordena los iconos del escritorio. Si Windows no recoloca iconos que ya estaban en esa zona, se quedan donde estaban.'

Section 'Integridad de código: qué archivos se bloquearon y por qué política (resumen)'
$policyNames = @{ '0283ac0f-fff1-49ae-ada1-8a933130cad6' = 'Smart App Control, modo APLICADO (VerifiedAndReputableDesktop; Microsoft: «inbox App Control policies»)'; '1283ac0f-fff1-49ae-ada1-8a933130cad6' = 'Smart App Control, modo EVALUACIÓN (VerifiedAndReputableDesktopEvaluation; solo audita, no bloquea)' }
try {
  $ci = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; Id = @(3033, 3034, 3076, 3077); StartTime = (Get-Date).AddDays(-30) } -MaxEvents 1500 -ErrorAction Stop | Where-Object { $_.Message -match 'HatsuDesktop|Hatsu Desktop|NotchWin|WinMac' })
  if ($ci.Count -eq 0) { Out-Line '  Ningún evento 3033/3034/3076/3077 nombra archivos de Hatsu Desktop en los últimos 30 días.' }
  else {
    $rows = foreach ($e in $ci) { $m = ($e.Message -replace '\s+', ' '); $pol = ([regex]::Matches($m, '\{([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})\}') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Select-Object -First 1)
      $files = [regex]::Matches($m, '([^\\/\s"]+\.(?:dll|exe))', 'IgnoreCase') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -match '^(HatsuDesktop|NotchWin|WinMac)' } | Select-Object -Unique
      foreach ($f in $files) { [pscustomobject]@{ Id = $e.Id; File = $f; Policy = $pol; Time = $e.TimeCreated } } }
    $groups = @($rows | Group-Object Id, File, Policy | Sort-Object { ($_.Group | Measure-Object Time -Maximum).Maximum } -Descending)
    Out-Line ('  Eventos que nombran archivos de Hatsu Desktop: {0} (en {1} combinaciones evento/archivo/política)' -f $ci.Count, $groups.Count)
    foreach ($g in ($groups | Select-Object -First 40)) { $a = $g.Group[0]; $first = ($g.Group | Measure-Object Time -Minimum).Minimum; $last = ($g.Group | Measure-Object Time -Maximum).Maximum
      $pn = if ($a.Policy -and $policyNames.ContainsKey($a.Policy)) { $policyNames[$a.Policy] } elseif ($a.Policy) { 'política desconocida para este informe' } else { 'sin política en el mensaje' }
      Out-Line ('  evento {0} · {1} · {2} veces · {3} → {4} · política {{{5}}} = {6}' -f $a.Id, $a.File, $g.Count, $first.ToString('yyyy-MM-dd HH:mm'), $last.ToString('HH:mm'), $a.Policy, $pn) }
    Out-Line '  Significado: 3077 = una directiva APLICADA bloqueó un archivo; 3033 = un archivo no cumplía el nivel de firma exigido (suele acompañar a 3077); 3076/3034 = lo mismo en modo auditoría/evaluación (no bloquea).'
  }
} catch { Out-Line ('  no se pudo leer (' + ($_.Exception.Message -replace '\s+', ' ') + '); algunos registros solo se leen como administrador.') }
try {
  $cit = Get-Command citool.exe -ErrorAction Stop; $tmp = Join-Path $env:TEMP ('hatsu-citool-' + [guid]::NewGuid().ToString('N') + '.txt')
  # citool puede quedarse esperando (visto en un runner de CI): se ejecuta con un tiempo máximo y se descarta si no responde
  $proc = Start-Process -FilePath $cit.Source -ArgumentList '-lp' -NoNewWindow -PassThru -RedirectStandardOutput $tmp -RedirectStandardError ($tmp + '.err')
  if ($proc.WaitForExit(15000)) { $lines = @(Get-Content $tmp -ErrorAction SilentlyContinue | Where-Object { $_ -match 'Policy ID|Friendly Name|Is Enforced|Is Currently Enforced|Base Policy ID' })
    Out-Line '  Directivas de integridad de código activas (citool.exe -lp, solo lectura):'; if ($lines.Count -eq 0) { Out-Line '    (sin datos legibles sin administrador)' } else { foreach ($l in ($lines | Select-Object -First 40)) { Out-Line ('    ' + $l.Trim()) } } }
  else { try { $proc.Kill() } catch { }; Out-Line '  citool.exe no respondió en 15 s: se omite (no es un fallo de Hatsu Desktop).' }
  foreach ($f in @($tmp, ($tmp + '.err'))) { try { [System.IO.File]::Delete($f) } catch { } }   # solo sus propios temporales de citool
} catch { Out-Line '  citool.exe no está disponible (o no se pudo ejecutar): se omite.' }
$cip0 = Join-Path $env:SystemRoot 'System32\CodeIntegrity\CiPolicies\Active\{0283ac0f-fff1-49ae-ada1-8a933130cad6}.cip'; $cip1 = Join-Path $env:SystemRoot 'System32\CodeIntegrity\CiPolicies\Active\{1283ac0f-fff1-49ae-ada1-8a933130cad6}.cip'
Out-Line ('  Archivo de la directiva Smart App Control APLICADA ({0283ac0f…}): ' + $(if (Test-Path $cip0) { 'existe' } else { 'no existe' }) + ' · de EVALUACIÓN ({1283ac0f…}): ' + $(if (Test-Path $cip1) { 'existe' } else { 'no existe' }))
if ($dir) { $ownUnsigned = @(Get-ChildItem $dir -Recurse -File -Include *.exe, *.dll -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(HatsuDesktop|WinMac|NotchWin)' } | Where-Object { (Get-AuthenticodeSignature -FilePath $_.FullName).Status -ne 'Valid' })
  Out-Line ('  Binarios propios sin firma válida en la instalación: ' + $ownUnsigned.Count); foreach ($f in ($ownUnsigned | Select-Object -First 40)) { Out-Line ('    - ' + $f.FullName.Substring($dir.Length).TrimStart('\')) } }

Section 'Procesos de Hatsu Desktop en ejecución'
$procs = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(HatsuDesktop|NotchWin\.App|WinMac)' } | Sort-Object ProcessName | ForEach-Object { $_.ProcessName })
Out-Line ($(if ($procs.Count -eq 0) { '  ninguno' } else { '  ' + (($procs | Select-Object -Unique) -join ', ') }))

Section 'Configuración (resumen; no se leen ciudad, calendario ni aplicaciones ancladas)'
$cfgPath = Join-Path $env:APPDATA 'WinMac\winmac.json'
if (-not (Test-Path $cfgPath)) { Out-Line '  winmac.json no existe (instalación nueva o el host aún no la guardó)' }
else {
  try {
    $j = Get-Content $cfgPath -Raw | ConvertFrom-Json
    foreach ($m in 'dock', 'bar', 'notch', 'connect') { $en = $null; if ($j.modules -and $j.modules.$m) { $en = $j.modules.$m.enabled }; Out-Line ("  módulo {0}: elegido = {1}" -f $m, $(if ($null -eq $en) { 'sí (valor de fábrica)' } elseif ($en) { 'sí' } else { 'no' })) }
    if ($j.bar) { Out-Line ("  Peripherals (función de la Barra): {0} · Cleanup: {1}" -f [bool]$j.bar.showPeripherals, [bool]$j.bar.showCleanup) }
    Out-Line ("  inicio automático (marca de la configuración): {0} · ocultar barra de tareas: {1}" -f [bool]$j.autostart, [bool]$j.hideTaskbar)
    if ($j.onboarding) { Out-Line ("  asistente: estado = {0} · origen = {1} · bienvenida de migración mostrada = {2}" -f $j.onboarding.status, $j.onboarding.origin, $(if ($j.onboarding.welcomeShownUtc) { 'sí' } else { 'no' })) } else { Out-Line '  asistente: sin registro (configuración anterior al asistente)' }
  } catch { Out-Line ('  no se pudo leer winmac.json: ' + $_.Exception.Message) }
}
$legacy = @(); foreach ($f in 'bar.json', 'dock.json') { if (Test-Path (Join-Path $env:APPDATA ('WinMac\' + $f))) { $legacy += $f } }
if (Test-Path (Join-Path $env:LOCALAPPDATA 'NotchWin\settings.json')) { $legacy += 'NotchWin\settings.json' }
Out-Line ('  configuración heredada de WinMac: ' + $(if ($legacy.Count -eq 0) { 'no se encontró' } else { $legacy -join ', ' }))

Section 'Inicio automático y barra de tareas'
try { $run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name WinMac -ErrorAction Stop).WinMac; Out-Line ('  Run\WinMac = ' + $run) } catch { Out-Line '  Run\WinMac: no está activado' }
Out-Line ('  archivo de recuperación de la barra de tareas: ' + $(if (Test-Path (Join-Path $env:LOCALAPPDATA 'WinMac\taskbar-restore.json')) { 'EXISTE (la barra puede estar oculta; HatsuDesktop.exe --restore-taskbar la devuelve)' } else { 'no existe' }))

Section 'Registro del host: lo que dijo de cada módulo (últimas líneas)'
$logPath = Join-Path $env:LOCALAPPDATA 'WinMac\logs\host.log'
if (Test-Path $logPath) { $lines = @(Get-Content $logPath -Tail 600 -ErrorAction SilentlyContinue | Where-Object { $_ -match '\[(bar|dock|notch|connect)\]|asistente|no se pudo|bloque' } | Select-Object -Last 60); if ($lines.Count -eq 0) { Out-Line '  sin líneas relevantes' } else { foreach ($l in $lines) { Out-Line ('  ' + $l) } } }
else { Out-Line '  host.log no existe (el host aún no se ejecutó con esta cuenta)' }

Section 'Fin del informe'
$target = if ($OutFile) { $OutFile } else { Join-Path $env:TEMP ('Hatsu-diagnostico-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt') }
[System.IO.File]::WriteAllText($target, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ('Informe guardado en: ' + $target); Write-Host 'Revísalo con el Bloc de notas antes de compartirlo. No se envió nada.'
