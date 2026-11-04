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
