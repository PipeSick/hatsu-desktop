<#
.SYNOPSIS
  Instalación, actualización y desinstalación REALES del MSI de Hatsu Desktop en el runner de Windows (perfil del runner, no tu PC). Informe en msi-report.md.
.DESCRIPTION
  Es una prueba en un entorno de CI efímero: demuestra que el MSI se instala, se actualiza sin duplicar, conserva los datos, convive con una copia portable y se desinstala limpiamente.
  NO sustituye una prueba manual en un Windows físico (ver docs/48, «Checklist de validación manual»). El runner es administrador; la instalación es por usuario igualmente (ALLUSERS vacío).
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'msi-test')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot; $root = Resolve-Path (Join-Path $here '..\..'); $dist = (Resolve-Path $DistDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $out = (Resolve-Path $OutDir).Path
$report = [Collections.Generic.List[string]]::new(); $fails = 0
function Check([string]$name, $okv, [string]$detail = '') { $ok = [bool](@($okv) | Select-Object -Last 1); $script:report.Add("| $(if ($ok) { 'OK' } else { 'FALLO' }) | $name | $detail |"); Write-Host ("{0}  {1} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail); if (-not $ok) { $script:fails++ } }
$report.Add('| Resultado | Comprobación | Detalle |'); $report.Add('|---|---|---|')

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Hatsu Desktop'; $exe = Join-Path $installDir 'HatsuDesktop.exe'
$menu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Hatsu Desktop\Hatsu Desktop.lnk'; $desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hatsu Desktop.lnk'
$dataDir = Join-Path $env:APPDATA 'WinMac'; $localData = Join-Path $env:LOCALAPPDATA 'WinMac'; $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function Arp([string]$pc) { Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$pc}" -ErrorAction SilentlyContinue }
function Arps { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall'); if (-not $k) { return }; foreach ($n in $k.GetSubKeyNames()) { $sk = $k.OpenSubKey($n); if ($sk -and $sk.GetValue('DisplayName') -eq 'Hatsu Desktop') { [pscustomobject]@{ Key = $n; DisplayName = $sk.GetValue('DisplayName'); DisplayVersion = $sk.GetValue('DisplayVersion') } } } }
function ArpNames { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall'); if ($k) { ($k.GetSubKeyNames() -join ',') } }
function Msi([string]$verb, [string]$target, [string]$log) { $p = Start-Process msiexec.exe -ArgumentList @($verb, "`"$target`"", '/qn', '/norestart', '/l*v', "`"$log`"") -Wait -PassThru; $p.ExitCode }
function HostRunning { @(Get-Process HatsuDesktop -ErrorAction SilentlyContinue) }
function Stop-AllHosts { & $exe --quit 2>$null | Out-Null; Get-Process HatsuDesktop -ErrorAction SilentlyContinue | ForEach-Object { if (-not $_.WaitForExit(15000)) { $_ | Stop-Process -Force } } }

# ── construcción: tres versiones del MISMO contenido (sin compresión: rápido) para probar instalar, actualizar y bajar de versión
$msis = @{}; foreach ($v in '2.0.0', '2.0.1', '2.0.2') { & (Join-Path $root 'installer\Build-Msi.ps1') -DistDir $dist -OutDir $out -Version $v -Compression none | Out-Host; $msis[$v] = Join-Path $out "HatsuDesktop-$v-win-x64.msi"; Check "construye el MSI $v" (Test-Path $msis[$v]) }
$pc = @{}; foreach ($v in $msis.Keys) { $pc[$v] = (Get-Content "$($msis[$v]).productcode").Trim() }
Check 'cada versión tiene su propio ProductCode, determinista' (($pc.Values | Select-Object -Unique).Count -eq 3)
& (Join-Path $root 'installer\Build-Msi.ps1') -DistDir $dist -OutDir (Join-Path $out 'again') -Version 2.0.0 -Compression none | Out-Null
Check 'dos compilaciones de la misma versión dan el mismo ProductCode' ((Get-Content (Join-Path $out 'again\HatsuDesktop-2.0.0-win-x64.msi.productcode')).Trim() -eq $pc['2.0.0'])

# ── estructura del MSI (tablas) sin instalar
$wi = New-Object -ComObject WindowsInstaller.Installer
function Prop($db, [string]$name) { $v = $db.OpenView("SELECT Value FROM Property WHERE Property='$name'"); [void]$v.Execute(); $r = $v.Fetch(); if ($r) { [string]$r.StringData(1) } else { $null } }
$db = $wi.OpenDatabase($msis['2.0.0'], 0)
Check 'MSI: nombre del producto «Hatsu Desktop»' ((Prop $db 'ProductName') -eq 'Hatsu Desktop') (Prop $db 'ProductName')
Check 'MSI: UpgradeCode estable' ((Prop $db 'UpgradeCode') -eq '{AE38CBF7-6543-463E-9067-50E88F162FA1}') (Prop $db 'UpgradeCode')
Check 'MSI: versión 2.0.0' ((Prop $db 'ProductVersion') -eq '2.0.0')
Check 'MSI: fabricante «Hatsu Desktop» (no WinMac)' ((Prop $db 'Manufacturer') -eq 'Hatsu Desktop')
Check 'MSI: instalación por usuario (ALLUSERS ausente)' ($null -eq (Prop $db 'ALLUSERS') -or (Prop $db 'ALLUSERS') -eq '') (Prop $db 'ALLUSERS')
$v = $db.OpenView("SELECT FileName FROM File"); [void]$v.Execute(); $files = @(); while ($r = $v.Fetch()) { $files += ([string]$r.StringData(1) -split '\|')[-1] }
foreach ($f in 'HatsuDesktop.exe', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe', 'NotchWin.App.exe', 'HatsuDesktop.ico', 'Restore-Taskbar.ps1', 'Migrate-FromWinMac.ps1', 'LICENSE') { Check "MSI incluye $f" ($files -contains $f) }
Check 'MSI no incluye ejecutables con el nombre antiguo' (-not ($files | Where-Object { $_ -in 'WinMac.exe', 'WinMac.Bar.App.exe', 'WinMac.Dock.App.exe', 'WinMac.Connect.App.exe' }))
Check 'MSI no incluye el desinstalador del ZIP' (-not ($files -contains 'Uninstall-HatsuDesktop.ps1'))
$db = $null; [GC]::Collect()

# ── datos de usuario y una copia «ajena» (portable / WinMac antiguo) que el MSI nunca debe tocar
New-Item -ItemType Directory -Force -Path $dataDir, $localData | Out-Null
$cfg = Join-Path $dataDir 'winmac.json'; '{"version":3,"autostart":false,"onboarding":{"status":"Completed","origin":"fresh"}}' | Set-Content $cfg -Encoding UTF8
'datos del usuario' | Set-Content (Join-Path $localData 'nota.txt'); $cfgHash = (Get-FileHash $cfg).Hash; $noteHash = (Get-FileHash (Join-Path $localData 'nota.txt')).Hash
if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
$otherDir = Join-Path $out 'otra copia'; New-Item -ItemType Directory -Force -Path $otherDir | Out-Null; Copy-Item "$env:SystemRoot\System32\more.com" (Join-Path $otherDir 'HatsuDesktop.exe')   # otra copia VIVA (existe en disco): el host no repara lo que no es huérfano
$foreign = "`"$otherDir\HatsuDesktop.exe`" --background"; New-ItemProperty -Path $runKey -Name 'WinMac' -Value $foreign -Force | Out-Null

# ── instalar 2.0.0
$code = Msi '/i' $msis['2.0.0'] (Join-Path $out 'install-2.0.0.log'); Check 'instala 2.0.0 (msiexec /qn)' ($code -eq 0) "código $code"
Check 'archivos en %LOCALAPPDATA%\Programs\Hatsu Desktop' ((Test-Path $exe) -and (Test-Path (Join-Path $installDir 'HatsuDesktop.Bar.exe')) -and (Test-Path (Join-Path $installDir 'NotchWin\NotchWin.App.exe')))
Check 'no hay instalación en Archivos de programa (por usuario)' (-not (Test-Path (Join-Path $env:ProgramFiles 'Hatsu Desktop')))
Check 'acceso directo del menú Inicio' (Test-Path $menu)
Check 'sin acceso directo en el escritorio por defecto' (-not (Test-Path $desktopLnk))
$arp = Arp $pc['2.0.0']; Check 'registrado en Aplicaciones instaladas (HKCU)' ($arp -and $arp.DisplayName -eq 'Hatsu Desktop' -and $arp.DisplayVersion -eq '2.0.0') ("$($arp.DisplayName) $($arp.DisplayVersion)")
Check 'Aplicaciones instaladas muestra el icono' ($arp -and $arp.DisplayIcon)
Check 'no activa el inicio automático ajeno ni crea el propio' ((Get-ItemProperty $runKey).WinMac -eq $foreign)
Check 'no ocultó la barra de tareas (sin registro de recuperación)' (-not (Test-Path (Join-Path $localData 'taskbar-restore.json')))
Check 'datos del usuario intactos tras instalar' ((Get-FileHash $cfg).Hash -eq $cfgHash)
Check 'sin servicios nuevos' (-not (Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*Hatsu*' -or $_.DisplayName -like '*Hatsu*' }))
Add-Type -AssemblyName System.Drawing; $ico = [System.Drawing.Icon]::ExtractAssociatedIcon($exe); Check 'el ejecutable instalado trae el icono Flux' ($ico.Width -ge 16)
Check 'la versión del ejecutable instalado' ((Get-Item $exe).VersionInfo.ProductName -eq 'Hatsu Desktop')

# ── el ejecutable instalado arranca (host sin módulos: no toca el escritorio del runner) y se cierra
$hcfg = Join-Path $out 'host-msi.json'; '{"version":3,"modules":{"bar":{"enabled":false},"dock":{"enabled":false},"notch":{"enabled":false},"connect":{"enabled":false}},"onboarding":{"status":"Completed"}}' | Set-Content $hcfg -Encoding UTF8
$hostArgs = @('--no-tray', '--fast', '--no-onboarding', '--config', "`"$hcfg`"", '--log', "`"$(Join-Path $out 'host-msi.log')`"", '--notchwin-settings', "`"$(Join-Path $out 'sin.json')`"", '--bar-json', "`"$(Join-Path $out 'sin2.json')`"")
$h = Start-Process $exe -ArgumentList $hostArgs -PassThru; Start-Sleep 4
Check 'HatsuDesktop.exe instalado arranca y se queda en ejecución' (-not $h.HasExited)
Stop-AllHosts; Check 'se cierra de forma ordenada (--quit)' ((HostRunning).Count -eq 0)

# ── convivencia con una copia portable: nadie se secuestra y nunca hay dos hosts
$portable = Join-Path $out 'portable'; Copy-Item $dist $portable -Recurse -Force
$pexe = Join-Path $portable 'HatsuDesktop.exe'; $pargs = $hostArgs.Clone(); $pargs[$pargs.IndexOf('--log') + 1] = "`"$(Join-Path $out 'host-portable.log')`""
$p = Start-Process $pexe -ArgumentList $pargs -PassThru; Start-Sleep 4; Check 'la copia portable está en ejecución' (-not $p.HasExited)
$bg = Start-Process $exe -ArgumentList (@('--background') + $hostArgs) -PassThru -Wait
Check 'el host instalado con --background se cierra sin tocar la copia portable' ($bg.ExitCode -eq 0 -and -not $p.HasExited -and (HostRunning).Count -eq 1)
Check 'quedó anotado en el registro (no la secuestra)' ((Get-Content (Join-Path $out 'host-msi.log') -Raw) -match 'esta se cierra sin tocarla')
$rep = Start-Process $exe -ArgumentList (@('--replace-running') + $hostArgs) -PassThru; Start-Sleep 8
Check '--replace-running (adopción explícita) cierra la portable y continúa con la instalada' ($p.HasExited -and -not $rep.HasExited -and (HostRunning).Count -eq 1)
Stop-AllHosts; Remove-Item $portable -Recurse -Force

# ── actualizar 2.0.0 → 2.0.1
$code = Msi '/i' $msis['2.0.1'] (Join-Path $out 'upgrade-2.0.1.log'); Check 'actualiza 2.0.0 → 2.0.1' ($code -eq 0) "código $code"
$all = @(Arps); Check 'una sola entrada del producto (no duplica la instalación)' ($all.Count -eq 1 -and $all[0].DisplayVersion -eq '2.0.1') ("$($all.Count) entrada(s): " + (($all | ForEach-Object DisplayVersion) -join ', ') + " · claves: " + (ArpNames))
Check 'la versión anterior ya no está registrada' (-not (Arp $pc['2.0.0']))
Check 'los archivos siguen instalados tras actualizar' (Test-Path $exe)
Check 'datos y elecciones del usuario intactos tras actualizar' (((Get-FileHash $cfg).Hash -eq $cfgHash) -and ((Get-FileHash (Join-Path $localData 'nota.txt')).Hash -eq $noteHash))
Check 'el inicio automático ajeno sigue igual tras actualizar' ((Get-ItemProperty $runKey).WinMac -eq $foreign)
$code = Msi '/i' $msis['2.0.0'] (Join-Path $out 'downgrade-2.0.0.log'); Check 'no permite instalar una versión anterior encima' ($code -ne 0) "código $code"
Check 'sigue instalada la 2.0.1' ((@(Arps)[0]).DisplayVersion -eq '2.0.1')

# ── actualizar con el host EN EJECUCIÓN (Restart Manager): debe terminar bien (0, o 3010 si pidió reinicio por archivos en uso)
$h = Start-Process $exe -ArgumentList $hostArgs -PassThru; Start-Sleep 4
$code = Msi '/i' $msis['2.0.2'] (Join-Path $out 'upgrade-running-2.0.2.log')
Check 'actualiza 2.0.1 → 2.0.2 con el host en ejecución' ($code -in 0, 3010) "código $code (3010 = pide reiniciar para sustituir archivos en uso)"
Check 'queda registrada la 2.0.2' ((@(Arps)[0]).DisplayVersion -eq '2.0.2') ((@(Arps) | ForEach-Object DisplayVersion) -join ', ')
Stop-AllHosts

# ── acceso directo del escritorio opcional (propiedad de la línea de comandos equivalente a marcar la característica)
$code = Msi '/x' $msis['2.0.2'] (Join-Path $out 'uninstall-pre.log'); Check 'desinstala para repetir con el acceso directo opcional' ($code -eq 0)
$p2 = Start-Process msiexec.exe -ArgumentList @('/i', "`"$($msis['2.0.2'])`"", 'ADDLOCAL=Main,DesktopShortcutFeature', '/qn', '/norestart', '/l*v', "`"$(Join-Path $out 'install-desktop.log')`"") -Wait -PassThru
Check 'con la característica opcional crea el acceso directo del escritorio' ($p2.ExitCode -eq 0 -and (Test-Path $desktopLnk))

# ── desinstalar: lo propio se quita; lo ajeno y los datos se quedan
New-ItemProperty -Path $runKey -Name 'WinMac' -Value $foreign -Force | Out-Null
$code = Msi '/x' $msis['2.0.2'] (Join-Path $out 'uninstall-foreign.log'); Check 'desinstala (msiexec /x)' ($code -eq 0) "código $code"
Check 'se retiran los archivos instalados' (-not (Test-Path $exe))
Check 'se retira el acceso directo del menú Inicio' (-not (Test-Path $menu))
Check 'se retira el acceso directo del escritorio' (-not (Test-Path $desktopLnk))
Check 'se retira la entrada de Aplicaciones instaladas' (@(Arps).Count -eq 0)
Check 'NO borra la configuración ni los datos del usuario' (((Get-FileHash $cfg).Hash -eq $cfgHash) -and ((Get-FileHash (Join-Path $localData 'nota.txt')).Hash -eq $noteHash))
Check 'NO quita el inicio automático de otra copia' ((Get-ItemProperty $runKey).WinMac -eq $foreign)
Check 'no deja la barra de tareas oculta' (-not (Test-Path (Join-Path $localData 'taskbar-restore.json')))
# propio: se instala de nuevo, el usuario activa el inicio automático de ESTA instalación y al desinstalar se quita
$code = Msi '/i' $msis['2.0.1'] (Join-Path $out 'reinstall.log'); Check 'reinstala (tras desinstalar) sin problemas' ($code -eq 0 -and (Test-Path $exe))
New-ItemProperty -Path $runKey -Name 'WinMac' -Value "`"$exe`" --background" -Force | Out-Null
$code = Msi '/x' $msis['2.0.1'] (Join-Path $out 'uninstall-own.log'); Check 'al desinstalar quita el inicio automático que apuntaba a esta instalación' ($code -eq 0 -and $null -eq (Get-ItemProperty $runKey -Name 'WinMac' -ErrorAction SilentlyContinue).WinMac)
Check 'los datos siguen ahí tras la segunda desinstalación' ((Get-FileHash $cfg).Hash -eq $cfgHash)
Remove-ItemProperty -Path $runKey -Name 'WinMac' -ErrorAction SilentlyContinue

$head = "# Instalación real del MSI en el runner de CI`n`n**Entorno efímero de GitHub Actions (perfil del runner): NO es una prueba en un Windows físico ni en tu equipo.**`n"
($head + ($report -join "`n")) | Set-Content (Join-Path $out 'msi-report.md') -Encoding UTF8
Write-Host "`n$($report.Count - 2) comprobaciones, $fails fallo(s)."; if ($fails -gt 0) { Write-Host "::error title=MSI::$fails comprobación(es) del MSI fallaron"; exit 1 }
