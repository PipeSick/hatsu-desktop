<#
.SYNOPSIS
  EXPERIMENTAL (Fase 3.2). Instala el MSIX de prueba en el runner de CI (certificado AUTOFIRMADO efímero) y comprueba que Hatsu Desktop FUNCIONA empaquetado:
  identidad de paquete, módulos reales (Barra y Dock) en marcha, cierre ordenado, y cómo se comportan el registro y AppData frente a la virtualización de MSIX.
.DESCRIPTION
  NO demuestra nada sobre Smart App Control: el runner no lo tiene y un certificado de prueba no es de confianza en ningún PC. Eso solo se prueba con el paquete firmado
  por la Microsoft Store en un Windows con SAC activo (ver docs/52). Las filas «INFO» son observaciones (la virtualización por defecto SE ESPERA que redirija).
#>
param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Cert, [string]$OutDir = 'msix-test', [string]$Label = 'Default', [string]$IdentityName = 'PipeSick.HatsuDesktop', [string]$ExpectedPfn = 'PipeSick.HatsuDesktop_83tr6qzbbarj8')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $out = (Resolve-Path $OutDir).Path; $Package = (Resolve-Path $Package).Path
$rows = [Collections.Generic.List[string]]::new(); $fails = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') { $script:rows.Add("| $(if ($ok) { 'OK' } else { 'FALLO' }) | $name | $detail |"); Write-Host ("{0,-6} {1}  {2}" -f $(if ($ok) { 'OK' } else { 'FALLO' }), $name, $detail); if (-not $ok) { $script:fails++ } }
function Info([string]$name, [string]$detail) { $script:rows.Add("| INFO | $name | $detail |"); Write-Host ("INFO   {0}  {1}" -f $name, $detail) }
function InPkg([string]$cmd, [string]$args_) { Invoke-CommandInDesktopPackage -PackageFamilyName $script:pfn -AppId 'HatsuDesktop' -Command $cmd -Args $args_ -PreventBreakaway }
function WaitFor([scriptblock]$c, [int]$s = 30) { $end = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $end) { if (& $c) { return $true }; Start-Sleep -Milliseconds 300 }; [bool](& $c) }
$rows.Add("### MSIX variante $Label"); $rows.Add(''); $rows.Add('| Resultado | Comprobación | Detalle |'); $rows.Add('|---|---|---|')

# limpieza previa
Get-AppxPackage -Name $IdentityName -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
Get-Process -Name 'HatsuDesktop*', 'NotchWin.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# instalación (certificado de prueba solo en este runner efímero)
Import-Certificate -FilePath $Cert -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
try { Add-AppxPackage -Path $Package -ErrorAction Stop; $inst = $true; $err = '' } catch { $inst = $false; $err = $_.Exception.Message }
Check 'Add-AppxPackage instala el MSIX' $inst $err
$p = Get-AppxPackage -Name $IdentityName | Select-Object -First 1
if (-not $p) { $rows | Set-Content (Join-Path $out "msix-report-$Label.md") -Encoding UTF8; exit 1 }
$script:pfn = $p.PackageFamilyName; $loc = $p.InstallLocation
Check 'la Package Family Name instalada es EXACTAMENTE la de Partner Center' ($p.PackageFamilyName -eq $ExpectedPfn) "$($p.PackageFamilyName)"
Check 'el manifiesto instalado declara las capacidades que el código necesita (notificaciones, ubicación, Bluetooth, cámara) y NO unvirtualizedResources' (($mfx = (Get-AppxPackageManifest -Package $p).OuterXml) -match 'userNotificationListener' -and $mfx -match 'Name="location"' -and $mfx -match 'Name="bluetooth"' -and $mfx -match 'Name="webcam"' -and $mfx -notmatch 'unvirtualizedResources') ''
Check 'el paquete es de plena confianza (no AppContainer) y está en WindowsApps' ($loc -like '*WindowsApps*') $loc
$mf = Get-AppxPackageManifest -Package $p
Check 'el manifiesto declara runFullTrust y la tarea de inicio HatsuDesktopStartup' (($mf.Package.Capabilities.InnerXml -match 'runFullTrust') -and ($mf.OuterXml -match 'HatsuDesktopStartup')) ''
foreach ($f in 'HatsuDesktop.exe', 'HatsuDesktop.dll', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe', 'NotchWin\NotchWin.App.exe', 'Vortice.Direct2D1.dll', 'SharpGen.Runtime.dll') { Check "contiene $f" (Test-Path (Join-Path $loc $f)) '' }

# identidad dentro del paquete
$idf = Join-Path $out "id-$Label.txt"; Remove-Item $idf -ErrorAction SilentlyContinue
InPkg 'cmd.exe' ('/c ""{0}\HatsuDesktop.exe" --package-info > "{1}""' -f $loc, $idf) | Out-Null
$okId = WaitFor { (Test-Path $idf) -and ((Get-Content $idf -Raw) -match 'package=') } 20
Check 'HatsuDesktop.exe se ejecuta CON identidad de paquete (--package-info)' ($okId -and (Get-Content $idf -Raw) -match "package=$IdentityName") ($(if (Test-Path $idf) { (Get-Content $idf -Raw).Trim() } else { 'sin salida' }))

# virtualización: ¿lo que escribe un proceso del paquete en HKCU y AppData lo ve el sistema?
$probe = Join-Path $out 'probe.ps1'; $pout = Join-Path $out "probe-$Label.txt"; Remove-Item $pout -ErrorAction SilentlyContinue
@'
param($Out)
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
try { New-ItemProperty -Path $k -Name HatsuProbe -Value 1 -PropertyType DWord -Force | Out-Null; $r = 'reg-ok' } catch { $r = 'reg-error:' + $_.Exception.Message }
try { New-Item -ItemType Directory -Force "$env:APPDATA\HatsuProbe" | Out-Null; 'x' | Set-Content "$env:APPDATA\HatsuProbe\p.txt"; $f = 'file-ok' } catch { $f = 'file-error:' + $_.Exception.Message }
try { New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name HatsuProbeRun -Value 'x' -PropertyType String -Force | Out-Null; $ru = 'run-ok' } catch { $ru = 'run-error' }
"$r $f $ru" | Set-Content $Out
'@ | Set-Content $probe -Encoding UTF8
InPkg 'powershell.exe' ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -Out "{1}"' -f $probe, $pout) | Out-Null
$okp = WaitFor { Test-Path $pout } 40
Check 'un proceso del paquete puede escribir en HKCU y AppData (sin errores)' ($okp -and ((Get-Content $pout -Raw) -notmatch 'error')) ($(if ($okp) { (Get-Content $pout -Raw).Trim() } else { 'sin salida' }))
$regSeen = $null -ne (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name HatsuProbe -ErrorAction SilentlyContinue)
$fileSeen = Test-Path (Join-Path $env:APPDATA 'HatsuProbe\p.txt'); $runSeen = $null -ne (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name HatsuProbeRun -ErrorAction SilentlyContinue)
Info "virtualización ($Label): la escritura en HKCU\…\Personalize ($(if ($regSeen) { 'la VE' } else { 'NO la ve' })) el sistema; en %APPDATA% ($(if ($fileSeen) { 'la VE' } else { 'NO la ve' })); en HKCU\…\Run ($(if ($runSeen) { 'la VE' } else { 'NO la ve' }))" 'Si NO la ve: el cambio de tema del sistema desde Hatsu y datos compartidos con herramientas externas dejan de funcionar empaquetados (docs/52).'
foreach ($n in 'HatsuProbe') { Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name $n -ErrorAction SilentlyContinue }
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name HatsuProbeRun -ErrorAction SilentlyContinue; Remove-Item (Join-Path $env:APPDATA 'HatsuProbe') -Recurse -Force -ErrorAction SilentlyContinue

# módulos reales empaquetados: el host arranca Barra y Dock desde la carpeta del paquete y los cierra con --quit
$cfg = Join-Path $out 'host.json'; '{"version":3,"modules":{"bar":{"enabled":true},"dock":{"enabled":true},"notch":{"enabled":false},"connect":{"enabled":false}},"onboarding":{"status":"Completed"}}' | Set-Content $cfg -Encoding UTF8
$log = Join-Path $out "host-$Label.log"; Remove-Item $log -ErrorAction SilentlyContinue
InPkg "$loc\HatsuDesktop.exe" ('--no-tray --fast --no-onboarding --config "{0}" --log "{1}" --notchwin-settings "{2}" --bar-json "{3}"' -f $cfg, $log, (Join-Path $out 'n.json'), (Join-Path $out 'b.json')) | Out-Null
$okm = WaitFor { (Test-Path $log) -and (Select-String -Path $log -SimpleMatch -Pattern '[bar] Running' -Quiet) -and (Select-String -Path $log -SimpleMatch -Pattern '[dock] Running' -Quiet) } 60
Check 'empaquetado: la Barra y el Dock llegan a Running' $okm ($(if (Test-Path $log) { ((Select-String -Path $log -Pattern '\[(bar|dock)\]' | Select-Object -Last 4 | ForEach-Object { $_.Line }) -join ' / ') } else { 'sin registro' }))
$procs = @(Get-Process -Name 'HatsuDesktop', 'HatsuDesktop.Bar', 'HatsuDesktop.Dock' -ErrorAction SilentlyContinue)
Check 'los procesos hijo (Barra, Dock) corren desde la carpeta del paquete' (($procs | Where-Object { $_.Path -like '*WindowsApps*' }).Count -ge 3) (($procs | ForEach-Object { $_.ProcessName }) -join ', ')
$dll = if ($okm) { Select-String -Path $log -Pattern 'Vortice|SharpGen|FileNotFoundException|Could not load' -Quiet } else { $false }
Check 'sin errores de carga de bibliotecas (Vortice/SharpGen) en el registro' (-not $dll) ''
InPkg "$loc\HatsuDesktop.exe" '--quit' | Out-Null
$closed = WaitFor { @(Get-Process -Name 'HatsuDesktop', 'HatsuDesktop.Bar', 'HatsuDesktop.Dock' -ErrorAction SilentlyContinue).Count -eq 0 } 30
Check 'el cierre ordenado (--quit) termina host y módulos' $closed ''
Get-Process -Name 'HatsuDesktop*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# inicio automático empaquetado = StartupTask (activarlo puede requerir al usuario: observación, no fallo)
$sa = Join-Path $out "startup-$Label.txt"; Remove-Item $sa -ErrorAction SilentlyContinue
InPkg 'cmd.exe' ('/c ""{0}\HatsuDesktop.exe" --install-autostart > "{1}" 2>&1 & "{0}\HatsuDesktop.exe" --remove-autostart >> "{1}" 2>&1"' -f $loc, $sa) | Out-Null
$oks = WaitFor { (Test-Path $sa) -and ((Get-Content $sa -Raw) -match 'desactivado|no estaba') } 30
Info 'inicio automático (StartupTask) con --install-autostart / --remove-autostart' ($(if (Test-Path $sa) { (Get-Content $sa -Raw).Trim() -replace "`r?`n", ' | ' } else { 'sin salida' }))

Get-AppxPackage -Name $IdentityName | Remove-AppxPackage -ErrorAction SilentlyContinue
Check 'Remove-AppxPackage desinstala limpiamente' ($null -eq (Get-AppxPackage -Name $IdentityName)) ''
$rows | Set-Content (Join-Path $out "msix-report-$Label.md") -Encoding UTF8
if ($fails -gt 0) { Write-Host "::error title=MSIX $Label::$fails comprobaciones fallaron"; exit 1 }
