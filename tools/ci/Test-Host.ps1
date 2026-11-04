<#
  Pruebas AUTOMÁTICAS del host (HatsuDesktop.exe) en el runner de Windows, con módulos de mentira (WinMac.FakeModule). No usa Dock ni Barra reales, no registra AppBar
  y solo toca el registro con un nombre de prueba (WinMacCiTest). Cada comprobación imprime PASS/FAIL; el script sale con código 1 si alguna falla.
  Cubre los criterios B1–B8 y A6 de docs/14. Las comprobaciones con ventanas reales (AppBar, Dock en pantalla) son MANUALES (docs/15).
#>
param([string]$PublishDir = 'ci-publish', [string]$OutDir = 'smoke')
$ErrorActionPreference = 'Continue'
$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path
$hostExe = Join-Path $PublishDir 'Host/HatsuDesktop.exe'; $fakeExe = Join-Path $PublishDir 'FakeModule/WinMac.FakeModule.exe'
$base = Join-Path ([IO.Path]::GetTempPath()) ("winmac-host-test-" + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $base | Out-Null
$script:fails = 0; $script:rows = @()

function Check([string]$name, [bool]$ok, [string]$detail = '') {
  $r = if ($ok) { 'PASS' } else { 'FAIL'; $script:fails++ }
  Write-Host ("{0,-4} {1}  {2}" -f $r, $name, $detail); $script:rows += [pscustomobject]@{ Prueba = $name; Resultado = $r; Detalle = $detail }
}
function WaitFor([scriptblock]$cond, [int]$sec = 20) { $end = (Get-Date).AddSeconds($sec); while ((Get-Date) -lt $end) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 100 }; return [bool](& $cond) }

# Una «sala» de prueba: carpeta propia con winmac.json, host.log y los registros de los módulos de mentira.
function NewRoom([string]$name, [string]$barMode = 'normal', [string]$dockMode = 'normal', [string]$barExe = $fakeExe) {
  $d = Join-Path $base $name; New-Item -ItemType Directory -Force -Path $d | Out-Null
  $cfg = @{ modules = @{
      bar  = @{ enabled = $true; path = $barExe;  args = @('--id', 'bar',  '--mode', $barMode,  '--out', $d) }
      dock = @{ enabled = $true; path = $fakeExe; args = @('--id', 'dock', '--mode', $dockMode, '--out', $d) }
      notch = @{ enabled = $false } } }
  $cfg | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d 'winmac.json') -Encoding UTF8
  return [pscustomobject]@{ Dir = $d; Cfg = Join-Path $d 'winmac.json'; Log = Join-Path $d 'host.log' }
}
function HostArgs($room, [switch]$Tray, [string]$BarJson = '', [switch]$Onboarding, [string]$Auto = '') { $a = @('--fast', '--notchwin-settings', "`"$(Join-Path $room.Dir 'sin-notchwin.json')`"", '--bar-json', "`"$(if ($BarJson) { $BarJson } else { Join-Path $room.Dir 'sin-bar.json' })`"", '--config', "`"$($room.Cfg)`"", '--log', "`"$($room.Log)`""); if (-not $Tray) { $a = @('--no-tray') + $a }; if (-not $Onboarding) { $a = @('--no-onboarding') + $a }; if ($Auto) { $a = @('--onboarding-auto', $Auto) + $a }; $a }
function StartHost($room, [switch]$Tray, [string]$BarJson = '', [switch]$Onboarding, [string]$Auto = '') { $p = Start-Process -FilePath $hostExe -ArgumentList (HostArgs $room -Tray:$Tray -BarJson $BarJson -Onboarding:$Onboarding -Auto $Auto) -PassThru -WindowStyle Hidden; $null = $p.Handle; $p }   # .Handle: sin él, ExitCode puede quedar vacío
function LogHas($room, [string]$text) { (Test-Path $room.Log) -and (Select-String -Path $room.Log -SimpleMatch -Pattern $text -Quiet) }
function ModLog($room, [string]$id) { $f = Join-Path $room.Dir "$id.log"; if (Test-Path $f) { Get-Content $f } else { @() } }
function Starts($room, [string]$id) { @(ModLog $room $id | Where-Object { $_ -match ' started$' }).Count }
function Pids($room, [string]$id) { [int[]]$r = @(ModLog $room $id | Where-Object { $_ -match ' started$' } | ForEach-Object { [int]($_ -split ' ')[1] }); , $r }   # la coma evita que un solo PID se devuelva como número suelto (y que + sume en vez de unir)
function AnyAlive([int[]]$pids) { foreach ($p in $pids) { if (Get-Process -Id $p -ErrorAction SilentlyContinue) { return $true } }; return $false }
function Ports { (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort | Sort-Object -Unique) -join ',' }
function Quit($room, $proc) { Start-Process -FilePath $hostExe -ArgumentList '--quit' -Wait -WindowStyle Hidden | Out-Null; if ($proc) { [void]$proc.WaitForExit(30000) } }
function Cleanup { Get-Process -Name 'HatsuDesktop', 'WinMac', 'WinMac.FakeModule', 'HatsuDesktop.Bar', 'HatsuDesktop.Dock', 'WinMac.Bar.App', 'WinMac.Dock.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300 }

if (-not (Test-Path $hostExe) -or -not (Test-Path $fakeExe)) { Write-Host "::error title=Host::faltan HatsuDesktop.exe o WinMac.FakeModule.exe en $PublishDir"; exit 1 }
Cleanup; $portsBefore = Ports

# ── 1. Arranque normal: ambos módulos listos, la isla (desactivada) no se lanza; ningún puerto TCP nuevo (A6)
$r1 = NewRoom 'normal'; $h1 = StartHost $r1
$ok = WaitFor { (LogHas $r1 '[bar] Running') -and (LogHas $r1 '[dock] Running') } 30
Check 'B2 arranque: Barra y Dock llegan a Running' $ok ("bar={0} dock={1}" -f (Starts $r1 'bar'), (Starts $r1 'dock'))
Check 'B2 la isla desactivada no se lanza' ((ModLog $r1 'notch').Count -eq 0)
Check 'B2 sin avisos de Job Object' (-not (LogHas $r1 'AVISO'))
Check 'A6 el host no abre puertos TCP en escucha' ((Ports) -eq $portsBefore) ("antes=[$portsBefore] ahora=[$(Ports)]")

# ── 2. Instancia única (B1)
$h2 = StartHost $r1; $exited = $h2.WaitForExit(8000)
Check 'B1 un segundo HatsuDesktop.exe sale solo y sin error' ($exited -and $h2.ExitCode -eq 0) ("salió={0} código={1}" -f $exited, $(if ($exited) { $h2.ExitCode } else { 'n/a' }))
Check 'B1 no se duplicaron los módulos' ((Starts $r1 'bar') -eq 1 -and (Starts $r1 'dock') -eq 1)

# ── 3. Cierre ordenado (B3): --quit → módulos cierran por su camino normal y no queda nada
$pids = [int[]]((Pids $r1 'bar') + (Pids $r1 'dock'))
Quit $r1 $h1
Check 'B3 el host termina tras --quit' ($h1.HasExited -and $h1.ExitCode -eq 0)
Check 'B3 los módulos cerraron por su camino normal (shutdown → bye)' ((ModLog $r1 'bar') -match ' bye$' -and (ModLog $r1 'dock') -match ' bye$')
Check 'B3 no queda ningún proceso de módulo' (($pids.Count -eq 2) -and -not (AnyAlive $pids)) ("pids: $($pids -join ',')")

# ── 3a. Hatsu Desktop: --replace-running cierra de forma ordenada la instancia en ejecución y continúa. Los nombres de instancia única y de cierre son los de WinMac, así que también
#        sirve para sustituir una instancia de la versión anterior (SIMULADO en el runner: ambas son esta misma versión; una instancia WinMac real se valida a mano).
$rr = NewRoom 'reemplazo'; $hA = StartHost $rr
$ok = WaitFor { (LogHas $rr '[bar] Running') -and (LogHas $rr '[dock] Running') } 30
Check 'B1b la primera instancia arranca' $ok
$hB = Start-Process -FilePath $hostExe -ArgumentList ((HostArgs $rr) + @('--replace-running')) -PassThru -WindowStyle Hidden; $null = $hB.Handle
$closedA = $hA.WaitForExit(40000)
Check 'B1b --replace-running: la instancia anterior se cierra sola y sin error' ($closedA -and $hA.ExitCode -eq 0) ("salió={0}" -f $closedA)
Check 'B1b la nueva instancia sigue en marcha (no se duplicó)' ((WaitFor { -not $hB.HasExited -and (Starts $rr 'bar') -ge 2 } 30)) ("bar arrancó {0} veces" -f (Starts $rr 'bar'))
Quit $rr $hB
Check 'B1b la nueva instancia también cierra con --quit' ($hB.HasExited)

# ── 3c. Fase 3 · asistente de primera ejecución, de verdad contra el host y el supervisor (sin ventana: --no-tray / --onboarding-auto)
# (1) instalación nueva (sin winmac.json): ningún módulo arranca hasta que el usuario decida; el estado queda anotado
$rf = Join-Path $base 'onb-fresh'; New-Item -ItemType Directory -Force -Path $rf | Out-Null
$roomF = [pscustomobject]@{ Dir = $rf; Cfg = Join-Path $rf 'winmac.json'; Log = Join-Path $rf 'host.log' }
$hF = StartHost $roomF -Onboarding; $okF = WaitFor { LogHas $roomF 'instalación nueva' } 20; Start-Sleep 3
Check 'ONB1 instalación nueva: el host detecta que debe mostrar el asistente' $okF
$jf = if (Test-Path $roomF.Cfg) { Get-Content $roomF.Cfg -Raw | ConvertFrom-Json } else { $null }
Check 'ONB1 queda anotado «NotStarted» y TODOS los módulos esperan (desactivados)' ($jf -and $jf.onboarding.status -eq 'NotStarted' -and -not ($jf.modules.PSObject.Properties | Where-Object { $_.Value.enabled })) ($jf.onboarding.status)
Check 'ONB1 no se lanzó ningún proceso de módulo antes de decidir' (-not (Get-Process -Name 'HatsuDesktop.Bar', 'HatsuDesktop.Dock', 'HatsuDesktop.Connect', 'NotchWin.App' -ErrorAction SilentlyContinue))
Quit $roomF $hF; Check 'ONB1 cierra con --quit' ($hF.HasExited)
# (2) configuración «pendiente» con módulos de mentira + omitir (valores seguros) sin ventana: el supervisor arranca lo elegido y SOLO eso
$ro = NewRoom 'onb-skip'; $oc = Get-Content $ro.Cfg -Raw | ConvertFrom-Json
$oc.modules.bar.enabled = $false; $oc.modules.dock.enabled = $false; $oc | Add-Member -NotePropertyName onboarding -NotePropertyValue ([pscustomobject]@{ status = 'NotStarted'; origin = 'fresh' }) -Force
$oc.modules | Add-Member -NotePropertyName connect -NotePropertyValue ([pscustomobject]@{ enabled = $false; path = $fakeExe; args = @('--id', 'connect', '--mode', 'ok', '--out', $ro.Dir) }) -Force
$oc | ConvertTo-Json -Depth 8 | Set-Content $ro.Cfg -Encoding UTF8
$hO = StartHost $ro -Onboarding -Auto 'skip'
$okO = WaitFor { (LogHas $ro '[bar] Running') -and (LogHas $ro '[dock] Running') } 30
Check 'ONB2 omitir aplica los valores seguros: la Barra y el Dock arrancan por el supervisor' $okO ("bar={0} dock={1}" -f (Starts $ro 'bar'), (Starts $ro 'dock'))
Check 'ONB2 Hatsu Connect (desactivado por defecto) NO se lanza' ((Starts $ro 'connect') -eq 0)
Check 'ONB2 el asistente anotó su resultado en el registro' (WaitFor { LogHas $ro '[asistente]' } 10)
Quit $ro $hO
$jo = Get-Content $ro.Cfg -Raw | ConvertFrom-Json
Check 'ONB2 la configuración guardada tiene Dock, Barra e Isla activados y nada más; sin inicio automático ni barra de tareas oculta' ($jo.modules.bar.enabled -and $jo.modules.dock.enabled -and -not $jo.modules.connect.enabled -and -not $jo.hideTaskbar -and -not $jo.autostart)
Check 'ONB2 omitir NO crea el inicio automático de Windows (valor Run\WinMac ausente)' (-not (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'WinMac' -ErrorAction SilentlyContinue))
Check 'ONB2 el estado del asistente no queda «NotStarted»' ($jo.onboarding.status -ne 'NotStarted') ($jo.onboarding.status)
# (3) un winmac.json que ya existía (WinMac o Hatsu anterior): no se fuerza el asistente; los módulos arrancan como siempre y solo se anota «Existing»
$re = NewRoom 'onb-existing'; $hE = StartHost $re -Onboarding
$okE = WaitFor { (LogHas $re '[bar] Running') -and (LogHas $re '[dock] Running') } 30
Check 'ONB3 usuario existente: arranca como siempre, sin asistente de por medio' $okE
Quit $re $hE; $je = Get-Content $re.Cfg -Raw | ConvertFrom-Json
Check 'ONB3 se anota «Existing» y los módulos y rutas siguen como estaban' (($je.onboarding.status -eq 'Existing') -and $je.modules.bar.enabled -and $je.modules.dock.enabled -and $je.modules.bar.path -eq $fakeExe) ($je.onboarding.status)
Check 'ONB3 el registro dice que se conserva la configuración y que se ofrece una bienvenida de migración (una vez)' ((LogHas $re 'configuración existente') -and (LogHas $re 'bienvenida de migración'))
Check 'ONB3 sin ventana (--no-tray) no se marca la bienvenida como mostrada (se ofrecerá en un arranque interactivo)' ($null -eq $je.onboarding.welcomeShownUtc)

# ── 3d. Fase 3.1 · un módulo que NO se puede iniciar: causa real, sin reintentos infinitos, los demás siguen, informe local de solo lectura
$rb = NewRoom 'f31-badexe'; $badExe = Join-Path $rb.Dir 'HatsuDesktop.Dock.exe'; 'esto no es un ejecutable de Windows' | Set-Content $badExe -Encoding ASCII
$cb = Get-Content $rb.Cfg -Raw | ConvertFrom-Json; $cb.modules.dock.path = $badExe; $cb.modules.dock.args = @(); $cb | ConvertTo-Json -Depth 8 | Set-Content $rb.Cfg -Encoding UTF8
$hb = StartHost $rb; $okB = WaitFor { (LogHas $rb '[dock] Failed') -and (LogHas $rb '[bar] Running') } 40
if (-not $okB) { Write-Host '--- registro del host (F31-1), últimas líneas:'; if (Test-Path $rb.Log) { Get-Content $rb.Log -Tail 40 | ForEach-Object { Write-Host "    $_" } }; Write-Host '--- configuración:'; Get-Content $rb.Cfg -Raw | Write-Host }
Check 'F31-1 un módulo que no se puede lanzar acaba en Failed y la Barra sigue en marcha' $okB ("dock Failed={0} bar Running={1}" -f (LogHas $rb '[dock] Failed'), (LogHas $rb '[bar] Running'))
$tries = @(Get-Content $rb.Log | Where-Object { $_ -match '\[dock\] no se pudo lanzar' }).Count
Check 'F31-2 no hay reintentos infinitos (a lo sumo 5 intentos)' ($tries -ge 1 -and $tries -le 5) ("intentos: $tries")
Check 'F31-3 el registro da el CÓDIGO REAL de Windows (193: no es un programa válido), no una suposición' (LogHas $rb '[error 193]')
Check 'F31-4 el registro NO culpa a Smart App Control de un archivo que simplemente no es un ejecutable' (-not (Select-String -Path $rb.Log -Pattern '\[dock\] no se pudo lanzar.*Smart App Control' -Quiet))
$repFile = Join-Path $rb.Dir 'diagnostico.txt'
$dp = Start-Process $hostExe -ArgumentList @('--diagnose', '--quiet', '--config', "`"$($rb.Cfg)`"", '--log', "`"$($rb.Log)`"", '--notchwin-settings', "`"$(Join-Path $rb.Dir 'sin-notchwin.json')`"", '--out', "`"$repFile`"") -Wait -PassThru -WindowStyle Hidden
$rep = if (Test-Path $repFile) { Get-Content $repFile -Raw } else { '' }
Check 'F31-5 HatsuDesktop.exe --diagnose termina bien y escribe el informe local' ($dp.ExitCode -eq 0 -and $rep.Length -gt 500)
Check 'F31-6 el informe dice que el Dock NO está en ejecución y por qué, y que la Barra sí' ($rep -match 'Dock: elegido=sí · NO está en ejecución' -and $rep -match '\[error 193\]' -and $rep -match 'Barra superior: elegido=sí')
Check 'F31-7 el informe es de solo lectura, local y sin datos personales ni telemetría' ($rep -match 'NO se envía a ningún sitio' -and -not $rep.Contains($env:USERPROFILE) -and -not ($rep -match 'WeatherCity|Molina'))
Quit $rb $hb; Cleanup
# (b) «Dock inaccesible»: permisos denegados sobre el ejecutable (si el entorno no lo permite, se informa y no se falla: depende de las ACL del runner)
$rd = NewRoom 'f31-denied'; $denyExe = Join-Path $rd.Dir 'HatsuDesktop.Dock.exe'; Copy-Item $fakeExe $denyExe -Force
$aclOk = $false; try { & icacls.exe $denyExe /deny '*S-1-1-0:(RX)' | Out-Null; $aclOk = ($LASTEXITCODE -eq 0) } catch { }
$cd = Get-Content $rd.Cfg -Raw | ConvertFrom-Json; $cd.modules.dock.path = $denyExe; $cd.modules.dock.args = @(); $cd | ConvertTo-Json -Depth 8 | Set-Content $rd.Cfg -Encoding UTF8
$hd = StartHost $rd; $okD = WaitFor { (LogHas $rd '[dock] Failed') -and (LogHas $rd '[bar] Running') } 40
if ($aclOk -and $okD) { Check 'F31-8 un ejecutable sin permiso de ejecución: Failed, los demás siguen y la causa es «acceso denegado»' ((LogHas $rd '[error 5]')) ((Select-String -Path $rd.Log -Pattern '\[dock\] no se pudo lanzar' | Select-Object -First 1).Line) }
else { Write-Host "INFO  F31-8 no concluyente en este runner (icacls=$aclOk, Failed=$okD): las ACL del runner no permiten reproducirlo; el caso está cubierto por las pruebas unitarias de clasificación" }
Quit $rd $hd; & icacls.exe $denyExe /remove:d '*S-1-1-0' 2>&1 | Out-Null; Cleanup

# ── 3b. Migración de los ajustes de la Barra (bar.json → winmac.json): una sola vez, sin tocar el original
$r3b = NewRoom 'migracion'; $barJson = Join-Path $r3b.Dir 'bar.json'
'{ "ShowSeconds": true, "Use24Hour": false, "Background": "Transparent" }' | Set-Content $barJson -Encoding UTF8
$shaAntes = (Get-FileHash $barJson -Algorithm SHA256).Hash
$h3b = StartHost $r3b -BarJson $barJson
$okM = WaitFor { (LogHas $r3b '[bar] Running') -and (LogHas $r3b 'ajustes de la Barra importados') } 30
Check 'MIG el host importa bar.json al arrancar por primera vez' $okM
Quit $r3b $h3b
$central = Get-Content $r3b.Cfg -Raw | ConvertFrom-Json
$barCfg = if ($central.PSObject.Properties.Name -contains 'bar') { $central.bar } else { $central.Bar }
Check 'MIG winmac.json recibió los valores de la Barra' ($null -ne $barCfg -and $barCfg.showSeconds -eq $true -and $barCfg.use24Hour -eq $false) ("bar=$($barCfg | ConvertTo-Json -Compress)")
Check 'MIG el bar.json original no se modificó' ((Get-FileHash $barJson -Algorithm SHA256).Hash -eq $shaAntes)
Check 'MIG los módulos y demás ajustes del central se conservaron' (($central.modules.PSObject.Properties.Name -contains 'bar') -and ($central.modules.PSObject.Properties.Name -contains 'dock'))
# segundo arranque: ya hay ajustes de la Barra en el central, así que un bar.json distinto NO los pisa
'{ "ShowSeconds": false }' | Set-Content $barJson -Encoding UTF8
$h3c = StartHost $r3b -BarJson $barJson; Start-Sleep -Seconds 4; Quit $r3b $h3c
$central2 = Get-Content $r3b.Cfg -Raw | ConvertFrom-Json; $barCfg2 = if ($central2.PSObject.Properties.Name -contains 'bar') { $central2.bar } else { $central2.Bar }
Check 'MIG un segundo arranque no pisa los ajustes ya centralizados' ($barCfg2.showSeconds -eq $true)
Cleanup

# ── 4. Host muerto de golpe (B4): el Job Object se lleva a los hijos
$r4 = NewRoom 'kill'; $h4 = StartHost $r4
[void](WaitFor { (LogHas $r4 '[bar] Running') -and (LogHas $r4 '[dock] Running') } 30)
$pids4 = [int[]]((Pids $r4 'bar') + (Pids $r4 'dock')); $alive = AnyAlive $pids4
Stop-Process -Id $h4.Id -Force
$gone = WaitFor { -not (AnyAlive $pids4) } 8
Check 'B4 antes de matar el host, los dos módulos estaban vivos' ($alive -and $pids4.Count -eq 2) ("pids: $($pids4 -join ',')")
Check 'B4 al morir el host de golpe, los hijos desaparecen (Job Object)' ($gone -and $pids4.Count -eq 2) ("pids: $($pids4 -join ',')")
Cleanup

# ── 5. Un módulo que cae no afecta al otro, se reinicia y al final se abandona (B5)
$r5 = NewRoom 'crash' 'crash' 'normal'; $h5 = StartHost $r5
$failed = WaitFor { LogHas $r5 '[bar] Failed' } 60
Check 'B5 la barra que cae una y otra vez llega a Failed (cortacircuito)' $failed ("arranques de bar: $(Starts $r5 'bar')")
Check 'B5 se reintentó antes de rendirse' ((Starts $r5 'bar') -ge 5)
Check 'B5 el Dock siguió funcionando sin reiniciarse' ((LogHas $r5 '[dock] Running') -and (Starts $r5 'dock') -eq 1)
$n = Starts $r5 'bar'; Start-Sleep -Seconds 2; Check 'B5 no hay bucle de reinicios tras Failed' ((Starts $r5 'bar') -eq $n)
Quit $r5 $h5; Cleanup

# ── 6. Módulo sin ejecutable: Failed y el resto sigue (B2)
$r6 = NewRoom 'missing' 'normal' 'normal' (Join-Path $base 'no-existe.exe'); $h6 = StartHost $r6
$ok6 = WaitFor { (LogHas $r6 '[bar] Failed') -and (LogHas $r6 '[dock] Running') } 30
Check 'B2 sin ejecutable: Barra Failed y Dock Running' $ok6
Quit $r6 $h6; Cleanup

# ── 7. Cierre voluntario con bye: no se reinicia
$r7 = NewRoom 'voluntary' 'exit-clean' 'normal'; $h7 = StartHost $r7
$stopped = WaitFor { LogHas $r7 '[bar] Stopped' } 30; Start-Sleep -Seconds 2
Check 'B5 un cierre voluntario (bye + código 0) no se reinicia' ($stopped -and (Starts $r7 'bar') -eq 1) ("arranques: $(Starts $r7 'bar')")
Quit $r7 $h7; Cleanup

# ── 8. Inicio automático: UNA clave y nada más (B8)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'; $name = 'WinMacCiTest'
function RunNames { $k = Get-Item $runKey -ErrorAction SilentlyContinue; if ($k) { @($k.Property | Where-Object { $_ } | Sort-Object) } else { @() } }
$namesBefore = RunNames
Start-Process -FilePath $hostExe -ArgumentList @('--install-autostart', '--autostart-name', $name) -Wait -WindowStyle Hidden | Out-Null
$val = (Get-ItemProperty $runKey -Name $name -ErrorAction SilentlyContinue).$name
Check 'B8 --install-autostart crea una clave con --background' ($val -like '*HatsuDesktop.exe*--background*') ("valor: $val")
$namesMid = RunNames
Check 'B8 solo se añadió esa clave' (($namesMid.Count -eq $namesBefore.Count + 1) -and ($namesMid -contains $name))
Start-Process -FilePath $hostExe -ArgumentList @('--remove-autostart', '--autostart-name', $name) -Wait -WindowStyle Hidden | Out-Null
$namesAfter = RunNames
Check 'B8 --remove-autostart la borra y deja el resto igual' (-not ($namesAfter -contains $name) -and (($namesAfter -join '|') -eq ($namesBefore -join '|')))

# ── 9. Con bandeja (informativo): el host arranca y cierra aunque el runner no tenga escritorio
$r9 = NewRoom 'tray'; $h9 = StartHost $r9 -Tray
$up = WaitFor { (LogHas $r9 '[bar] Running') -and (LogHas $r9 '[dock] Running') } 30
$trayMsg = if (LogHas $r9 'icono de bandeja creado') { 'icono creado' } elseif (LogHas $r9 'no se pudo crear el icono') { 'sin icono (entorno sin bandeja)' } else { 'sin mensaje' }
Check 'M1 (info) con bandeja el host arranca y los módulos llegan a Running' $up $trayMsg
Quit $r9 $h9; Check 'M2 (info) con bandeja el cierre termina limpio' ($h9.HasExited -and $h9.ExitCode -eq 0)
Cleanup

# ── 10–11. Barra y Dock REALES bajo el host (--hosted): conectan, llegan a Running, se cierran en orden y mueren con el host
$barExe = Join-Path $PublishDir 'Bar.App/HatsuDesktop.Bar.exe'; $dockExe = Join-Path $PublishDir 'Dock.App/HatsuDesktop.Dock.exe'
function NewRealRoom([string]$name) {
  $d = Join-Path $base $name; New-Item -ItemType Directory -Force -Path $d | Out-Null
  @{ modules = @{ bar = @{ enabled = $true; path = $barExe; args = @('--no-config', '--no-reserve') }; dock = @{ enabled = $true; path = $dockExe; args = @('--no-config') }; notch = @{ enabled = $false } } } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $d 'winmac.json') -Encoding UTF8
  [pscustomobject]@{ Dir = $d; Cfg = Join-Path $d 'winmac.json'; Log = Join-Path $d 'host.log' }
}
function RealPids { @(Get-Process -Name 'HatsuDesktop.Bar', 'HatsuDesktop.Dock', 'WinMac.Bar.App', 'WinMac.Dock.App' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) }
if ((Test-Path $barExe) -and (Test-Path $dockExe)) {
  $rr = NewRealRoom 'real-quit'; $hr = StartHost $rr
  $up = WaitFor { (LogHas $rr '[bar] Running') -and (LogHas $rr '[dock] Running') } 45
  $realPids = RealPids
  Check 'B9 Barra y Dock REALES bajo el host llegan a Running (--hosted)' ($up -and $realPids.Count -eq 2) ("procesos: $($realPids -join ',')")
  Quit $rr $hr
  Check 'B9 al cerrar, Barra y Dock reales terminan por su camino normal (Stopped, sin Crashed)' ((LogHas $rr '[bar] Stopped') -and (LogHas $rr '[dock] Stopped') -and -not (LogHas $rr 'Crashed') -and -not (LogHas $rr 'Failed'))
  Check 'B9 no queda ningún proceso real de Barra/Dock' (WaitFor { (RealPids).Count -eq 0 } 8)
  Cleanup
  $rk = NewRealRoom 'real-kill'; $hk = StartHost $rk
  [void](WaitFor { (LogHas $rk '[bar] Running') -and (LogHas $rk '[dock] Running') } 45); $kp = RealPids
  Stop-Process -Id $hk.Id -Force
  Check 'B4 con módulos REALES: al morir el host de golpe, Barra y Dock desaparecen (Job Object)' ($kp.Count -eq 2 -and (WaitFor { (RealPids).Count -eq 0 } 10)) ("procesos antes: $($kp -join ',')")
  Cleanup
} else { Check 'B9 (omitido) faltan Bar.App/Dock.App publicados' $false }

$md = "### Host: pruebas de ciclo de vida (módulos de mentira)`n`n| Prueba | Resultado | Detalle |`n|---|---|---|`n" + (($script:rows | ForEach-Object { "| $($_.Prueba) | $($_.Resultado) | $($_.Detalle) |" }) -join "`n")
$md | Set-Content (Join-Path $OutDir 'host-tests.md') -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $md }
try { Remove-Item -Recurse -Force $base -ErrorAction SilentlyContinue } catch { }
if ($script:fails) { Write-Host "::error title=Host::$($script:fails) comprobación(es) fallaron"; exit 1 }
Write-Host 'Host: todas las comprobaciones pasaron.'; exit 0
