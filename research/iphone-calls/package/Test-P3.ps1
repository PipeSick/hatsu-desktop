<#
  Pruebas automáticas de P3 (instalación, conflictos, recuperación y limpieza). NO tocan el sistema: usan un «mundo» simulado en memoria y una carpeta temporal.
  Funcionan en Windows PowerShell 5.1 y en pwsh 7.   Uso:  pwsh -NoProfile -File .\Test-P3.ps1   (código de salida 0 = todo bien)
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'P3.Common.ps1')
$script:pass = 0; $script:fail = 0
function Check([bool]$cond, [string]$name) { if ($cond) { $script:pass++ } else { $script:fail++; Write-Host "  FALLA: $name" -ForegroundColor Red } }
function ThrowsLike([scriptblock]$sb, [string]$pattern) { try { & $sb | Out-Null; return $false } catch { return ($_.Exception.Message -like $pattern) } }

$TP = 'LocalMachine\TrustedPeople'; $MY = 'CurrentUser\My'
$manifestPath = Join-Path $PSScriptRoot 'AppxManifest.xml'
$goodManifest = Get-Content -LiteralPath $manifestPath -Raw

function New-World {
  $w = @{ Packages = New-Object System.Collections.ArrayList; Stores = @{}; Fail = ''; RemovedCerts = New-Object System.Collections.ArrayList; Log = New-Object System.Collections.ArrayList
          Manifest = $goodManifest; Confirm = $true; CerThumbOverride = ''; PkgSignatureKind = 'Developer'; LastThumb = '' }
  $w.Stores[$TP] = New-Object System.Collections.ArrayList; $w.Stores[$MY] = New-Object System.Collections.ArrayList
  return $w
}
function New-Cert([string]$subject, [string]$thumb) { return [pscustomobject]@{ Subject = $subject; Thumbprint = $thumb } }
function New-Pkg([string]$ver, [string]$kind, [string]$full) { return [pscustomobject]@{ Name = 'WinMac.Research.CallProbe'; Publisher = 'CN=WinMacResearch'; Version = $ver; SignatureKind = $kind; PackageFullName = $full; InstallLocation = 'X:\fake' } }
function Hex40 { return (([guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')).Substring(0, 40).ToUpper()) }

function New-FakeOps($w) {
  $o = @{}
  $o.Log = { param($m) [void]$w.Log.Add($m) }.GetNewClosure()
  $o.Confirm = { param($t) $w.Confirm }.GetNewClosure()
  $o.GetPackages = { param($n) @($w.Packages | Where-Object { $_.Name -eq $n }) }.GetNewClosure()
  $o.RemovePackage = { param($f) for ($i = $w.Packages.Count - 1; $i -ge 0; $i--) { if ($w.Packages[$i].PackageFullName -eq $f) { $w.Packages.RemoveAt($i) } } }.GetNewClosure()
  $o.GetCert = { param($s, $t) $w.Stores[$s] | Where-Object { $_.Thumbprint -eq $t } | Select-Object -First 1 }.GetNewClosure()
  $o.RemoveCert = { param($s, $t) [void]$w.RemovedCerts.Add("$s|$t"); for ($i = $w.Stores[$s].Count - 1; $i -ge 0; $i--) { if ($w.Stores[$s][$i].Thumbprint -eq $t) { $w.Stores[$s].RemoveAt($i) } } }.GetNewClosure()
  $o.FindTools = { [pscustomobject]@{ MakeAppx = 'm'; SignTool = 's'; Version = '10.0.28000.0' } }.GetNewClosure()
  $o.PrepareContent = { param($b, $c, $m) [void](New-Item -ItemType Directory -Force -Path $c); Set-Content -LiteralPath (Join-Path $c 'CallProbe.exe') 'x' }.GetNewClosure()
  $o.NewCert = { param($subj, $days, $id) $t = Hex40; $w.LastThumb = $t; $c = New-Cert $subj $t; [void]$w.Stores[$MY].Add($c); $c }.GetNewClosure()
  $o.ExportCert = { param($c, $p) if ($w.Fail -eq 'export') { throw 'export roto' }; Set-Content -LiteralPath $p 'cer' }.GetNewClosure()
  $o.CerThumbprint = { param($p) if ($w.CerThumbOverride) { $w.CerThumbOverride } else { $w.LastThumb } }.GetNewClosure()
  $o.Pack = { param($t, $c, $m) if ($w.Fail -eq 'pack') { throw 'makeappx roto' }; Set-Content -LiteralPath $m 'msix' }.GetNewClosure()
  $o.ReadMsixManifest = { param($m) $w.Manifest }.GetNewClosure()
  $o.Sign = { param($t, $m, $th) if ($w.Fail -eq 'sign') { throw 'signtool roto' } }.GetNewClosure()
  $o.ImportTrust = { param($p) if ($w.Fail -eq 'import-partial') { [void]$w.Stores[$TP].Add((New-Cert 'CN=WinMacResearch' $w.LastThumb)); throw 'import roto tras añadir' }; if ($w.Fail -eq 'import') { throw 'import roto' }; [void]$w.Stores[$TP].Add((New-Cert 'CN=WinMacResearch' $w.LastThumb)) }.GetNewClosure()
  $o.Verify = { param($t, $m) if ($w.Fail -eq 'verify') { 'SHA1 hash: ' + (Hex40) } else { 'SHA1 hash: ' + $w.LastThumb.ToLower() } }.GetNewClosure()
  $o.AddPackage = { param($m)
      if ($w.Fail -eq 'add') { throw 'Add-AppxPackage roto (0x800B0109)' }
      [void]$w.Packages.Add((New-Pkg '0.1.0.0' $w.PkgSignatureKind 'WinMac.Research.CallProbe_0.1.0.0_x64__abc'))
      if ($w.Fail -eq 'add-partial') { throw 'Add-AppxPackage falló tras copiar' }
      if ($w.Fail -eq 'add-foreign-appears') { $w.Packages.Clear(); [void]$w.Packages.Add((New-Pkg '9.9.9.9' 'Store' 'WinMac.Research.CallProbe_9.9.9.9_x64__zzz')); throw 'carrera' }
    }.GetNewClosure()
  $o.ReadInstalledManifest = { param($l) $w.Manifest }.GetNewClosure()
  return $o
}

function Run-Install($w, $base, $ops) { Invoke-P3Install -Base $base -BuildDir 'B:\x' -ManifestPath $manifestPath -Ops $ops }
function New-Base { $b = Join-Path ([IO.Path]::GetTempPath()) ('p3test-' + [guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $b); return $b }
function Add-Decoys($w) {
  [void]$w.Stores[$TP].Add((New-Cert 'CN=WinMacResearch' (Hex40))); [void]$w.Stores[$MY].Add((New-Cert 'CN=WinMacResearch' (Hex40)))
  [void]$w.Stores[$TP].Add((New-Cert 'CN=Otro' (Hex40)))
  return @($w.Stores[$TP] + $w.Stores[$MY] | ForEach-Object { $_.Thumbprint })
}
function Decoys-Intact($w, $decoys) { $now = @($w.Stores[$TP] + $w.Stores[$MY] | ForEach-Object { $_.Thumbprint }); foreach ($d in $decoys) { if ($now -notcontains $d) { return $false } }; return $true }
function Run-Dirs($base) { @(Get-ChildItem -LiteralPath (Join-Path $base 'CallProbeP3') -Directory -ErrorAction SilentlyContinue) }

Write-Host 'P3: pruebas'

# 1. Sintaxis de todos los scripts PowerShell del paquete
foreach ($f in Get-ChildItem -LiteralPath $PSScriptRoot -Filter *.ps1) {
  $errs = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errs)
  Check ($errs.Count -eq 0) "sintaxis $($f.Name): $(($errs | ForEach-Object { $_.Message }) -join '; ')"
}
# rutas: los scripts cargan P3.Common.ps1 con $PSScriptRoot y esa dependencia existe
Check (Test-Path (Join-Path $PSScriptRoot 'P3.Common.ps1')) 'existe P3.Common.ps1'
foreach ($n in 'Install-SignedDevPackage.ps1', 'Uninstall-SignedDevPackage.ps1') { Check ((Get-Content (Join-Path $PSScriptRoot $n) -Raw) -match "Join-Path \`$PSScriptRoot 'P3\.Common\.ps1'") "$n carga P3.Common.ps1" }
Check (Test-Path $manifestPath) 'existe AppxManifest.xml'

# 2. Huella
Check (Test-P3Thumbprint (Hex40)) 'huella válida'
$h = Hex40; $bads = @('', $null, 'ABC', '*', ($h + 'A'), ($h.Substring(0, 39) + 'Z'), (('A' * 20) + ' ' + ('B' * 19)), 'CN=WinMacResearch')
foreach ($bad in $bads) { Check (-not (Test-P3Thumbprint $bad)) "huella inválida rechazada: '$bad'" }

# 3. Publisher vs sujeto
Check (Test-P3PublisherMatch 'CN=WinMacResearch' 'CN=WinMacResearch') 'publisher = sujeto'
Check (Test-P3PublisherMatch 'CN=A, O=B' 'CN=A,O=B') 'publisher con espacios tras coma'
Check (-not (Test-P3PublisherMatch 'CN=WinMacResearch' 'CN=WinMacResearch2')) 'publisher distinto'
Check (-not (Test-P3PublisherMatch '' 'CN=X')) 'publisher vacío'

# 4. Manifiesto: identidad y capacidades exactas
$f = Get-P3ManifestFacts $goodManifest
Check (@(Test-P3ManifestFacts $f 'CN=WinMacResearch').Count -eq 0) 'manifiesto real sin problemas'
Check (($f.Capabilities -join ',') -eq 'bluetooth,phoneCall,phoneLineTransportManagement,runFullTrust') 'capacidades leídas'
Check (@(Test-P3ManifestFacts (Get-P3ManifestFacts ($goodManifest -replace '<rescap:Capability Name="runFullTrust" />', '')) 'CN=WinMacResearch').Count -ge 1) 'falta una capacidad → problema'
Check (@(Test-P3ManifestFacts (Get-P3ManifestFacts ($goodManifest -replace '<DeviceCapability Name="bluetooth" />', '<DeviceCapability Name="bluetooth" /><rescap:Capability Name="packageManagement" />')) 'CN=WinMacResearch').Count -ge 1) 'capacidad extra → problema'
Check (@(Test-P3ManifestFacts (Get-P3ManifestFacts ($goodManifest -replace 'Name="WinMac.Research.CallProbe"', 'Name="Otra.App"')) 'CN=WinMacResearch').Count -ge 1) 'nombre distinto → problema'
Check (@(Test-P3ManifestFacts $f 'CN=Distinto').Count -ge 1) 'sujeto distinto → problema'

# 5. Instalación correcta + 6. Desinstalación exacta
$w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w
$otherRun = Join-Path $base 'CallProbeP3\20200101T000000Z-aaaaaaaa'; [void](New-Item -ItemType Directory -Path $otherRun); Set-Content (Join-Path $otherRun 'keep.txt') 'otro'
Save-P3State @{ runId = '20200101T000000Z-aaaaaaaa'; status = 'cleaned'; statePath = (Join-Path $otherRun 'state.json') }
$s = Run-Install $w $base $ops
Check ($s.status -eq 'verified-installed') 'instalación → verified-installed'
Check ($w.Packages.Count -eq 1) 'paquete instalado'
Check ($null -ne (& $ops.GetCert $TP $s.thumbprint) -and $null -ne (& $ops.GetCert $MY $s.thumbprint)) 'cert en TrustedPeople y My'
$disk = Read-P3State $s.statePath
Check ($disk.thumbprint -eq $s.thumbprint -and (Test-P3Thumbprint $disk.thumbprint)) 'huella en disco'
Check ($disk.packageFullName -like 'WinMac.Research.CallProbe_*') 'nombre completo de paquete en disco'
$r = Invoke-P3Uninstall $base '' $ops
Check ($r.Complete) 'desinstalación completa'
Check ($w.Packages.Count -eq 0) 'paquete quitado'
Check ($null -eq (& $ops.GetCert $TP $s.thumbprint) -and $null -eq (& $ops.GetCert $MY $s.thumbprint)) 'certs de la ejecución quitados'
Check (Decoys-Intact $w $dec) 'certificados con el mismo Subject (y otros) intactos'
Check ((@($w.RemovedCerts | Where-Object { $_ -notlike "*|$($s.thumbprint)" })).Count -eq 0) 'solo se borró por la huella exacta'
Check (@(Get-P3PendingRuns $base).Count -eq 0) 'sin ejecuciones pendientes'
Check ((Read-P3State $s.statePath).status -eq 'cleaned') 'state.json conservado y cerrado'
Check (@(Get-ChildItem -LiteralPath $s.runDir -Force).Count -eq 1) 'solo queda state.json en la carpeta de la ejecución'
Check (Test-Path (Join-Path $otherRun 'keep.txt')) 'archivos de otra ejecución intactos'
Check ($null -eq (Invoke-P3Uninstall $base '' $ops)) 'segunda desinstalación: nada que hacer'

# 7. Fallo en cada paso → recuperación completa, sin tocar lo ajeno
foreach ($step in 'export', 'pack', 'sign', 'import', 'import-partial', 'verify', 'add', 'add-partial') {
  $w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w; $w.Fail = $step; $pre = $w.Packages.Count
  Check (ThrowsLike { Run-Install $w $base $ops } 'P3-FALLO (revertido por completo)*') "fallo en '$step' → revertido por completo"
  Check ($w.Packages.Count -eq 0) "[$step] sin paquete"
  Check (Decoys-Intact $w $dec) "[$step] señuelos intactos"
  Check (@($w.Stores[$TP] + $w.Stores[$MY]).Count -eq $dec.Count) "[$step] ningún certificado de la ejecución quedó"
  $st = @(Run-Dirs $base); Check ($st.Count -eq 1) "[$step] una carpeta de ejecución"
  $d = Read-P3State (Join-Path $st[0].FullName 'state.json')
  Check ($d.status -eq 'cleaned') "[$step] estado cleaned"
  if ($step -ne 'export' -or $true) { Check (Test-P3Thumbprint $d.thumbprint) "[$step] huella registrada en disco" }
  Check ((@($w.RemovedCerts | Where-Object { $_ -notlike "*|$($d.thumbprint)" })).Count -eq 0) "[$step] borrado solo por huella exacta"
  Check (@(Get-ChildItem -LiteralPath $st[0].FullName -Force).Count -eq 1) "[$step] carpeta limpia salvo state.json"
}
# manifiesto empaquetado distinto / huella del .cer distinta / firma None
foreach ($case in 'manifest-caps', 'manifest-pub', 'cer-thumb', 'sigkind-none') {
  $w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w
  switch ($case) {
    'manifest-caps' { $w.Manifest = $goodManifest -replace '<rescap:Capability Name="runFullTrust" />', '' }
    'manifest-pub'  { $w.Manifest = $goodManifest -replace 'CN=WinMacResearch', 'CN=Otro' }
    'cer-thumb'     { $w.CerThumbOverride = Hex40 }
    'sigkind-none'  { $w.PkgSignatureKind = 'None' }
  }
  Check (ThrowsLike { Run-Install $w $base $ops } 'P3-FALLO*') "[$case] falla"
  Check ($w.Packages.Count -eq 0 -and (Decoys-Intact $w $dec) -and @($w.Stores[$TP] + $w.Stores[$MY]).Count -eq $dec.Count) "[$case] revertido sin tocar señuelos"
}

# 8–10. Precondiciones: no se crea nada
foreach ($kind in 'Store', 'None', 'Developer') {
  $w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w
  [void]$w.Packages.Add((New-Pkg '0.0.1.0' $kind "WinMac.Research.CallProbe_0.0.1.0_x64__pre-$kind"))
  Check (ThrowsLike { Run-Install $w $base $ops } 'P3-PRECONDICION*') "paquete previo ($kind) → se detiene"
  Check ($w.Packages.Count -eq 1 -and $w.Packages[0].PackageFullName -like "*pre-$kind") "paquete previo ($kind) intacto"
  Check (-not (Test-Path (Join-Path $base 'CallProbeP3')) -and $w.RemovedCerts.Count -eq 0 -and (Decoys-Intact $w $dec) -and @($w.Stores[$MY]).Count -eq 1) "paquete previo ($kind): no se creó ni borró nada"
}
$w = New-World; $base = New-Base; $ops = New-FakeOps $w; $w.Confirm = $false
Check (ThrowsLike { Run-Install $w $base $ops } 'P3-CANCELADO*') 'sin confirmación → cancelado'
Check (-not (Test-Path (Join-Path $base 'CallProbeP3')) -and $w.Stores[$MY].Count -eq 0) 'cancelado: nada creado'
$w = New-World; $base = New-Base; $ops = New-FakeOps $w; $w.Fail = 'sign'
Check (ThrowsLike { Run-Install $w $base $ops } 'P3-FALLO*') 'primera ejecución falla'
$st = (Run-Dirs $base)[0]; $d = Read-P3State (Join-Path $st.FullName 'state.json'); $d.status = 'cleanup-incomplete'; Save-P3State $d
$w.Fail = ''; Check (ThrowsLike { Run-Install $w $base $ops } 'P3-PRECONDICION*sin cerrar*') 'una ejecución pendiente bloquea otra'
$w2 = New-World; $ops2 = New-FakeOps $w2; $w2.Confirm = $true; $manifestBad = Join-Path ([IO.Path]::GetTempPath()) 'bad-manifest.xml'; Set-Content $manifestBad ($goodManifest -replace 'Name="WinMac.Research.CallProbe"', 'Name="X"')
Check (ThrowsLike { Invoke-P3Install -Base (New-Base) -BuildDir 'B:\x' -ManifestPath $manifestBad -Ops $ops2 } 'P3-PRECONDICION*manifiesto*') 'manifiesto de origen incorrecto → se detiene'
$w2.Stores[$MY].Count | ForEach-Object { Check ($_ -eq 0) 'manifiesto incorrecto: nada creado' }
$ops3 = New-FakeOps $w2; $ops3.FindTools = { $null }
Check (ThrowsLike { Invoke-P3Install -Base (New-Base) -BuildDir 'B:\x' -ManifestPath $manifestPath -Ops $ops3 } 'P3-PRECONDICION*makeappx*') 'sin SDK → se detiene'

# 11. Identidad inesperada durante la recuperación → DETENERSE y decirlo
$w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w; $w.Fail = 'add-foreign-appears'
$msg = ''; try { Run-Install $w $base $ops | Out-Null } catch { $msg = $_.Exception.Message }
Check ($msg -like 'P3-FALLO-INCOMPLETO*') 'paquete ajeno durante recuperación → incompleto'
Check ($w.Packages.Count -eq 1 -and $w.Packages[0].Version -eq '9.9.9.9') 'paquete ajeno NO se quitó'
$st = (Run-Dirs $base)[0]; $d = Read-P3State (Join-Path $st.FullName 'state.json')
Check ($d.status -eq 'cleanup-incomplete' -and $msg -like "*$($d.thumbprint)*") 'estado incompleto y mensaje con la huella'
Check ($w.RemovedCerts.Count -eq 0) 'sin identidad clara no se borra ningún certificado'
Check ($null -ne (& $ops.GetCert $TP $d.thumbprint)) 'el certificado de la ejecución sigue (documentado) hasta que el usuario decida'

# 12. Desinstalación con varias ejecuciones
$w = New-World; $base = New-Base; $ops = New-FakeOps $w
foreach ($i in 1, 2) { $w.Fail = 'sign'; try { Run-Install $w $base $ops | Out-Null } catch { }; foreach ($dd in Run-Dirs $base) { $x = Read-P3State (Join-Path $dd.FullName 'state.json'); if ($x.status -eq 'cleaned') { $x.status = 'cleanup-incomplete'; Save-P3State $x } } }
Check (@(Get-P3PendingRuns $base).Count -eq 1) 'bloqueo de pendientes impide una segunda ejecución real'
$ids = @(Run-Dirs $base | ForEach-Object Name)
$x2 = @{ runId = 'zzz'; status = 'cleanup-incomplete'; statePath = (Join-Path $base 'CallProbeP3\zzz\state.json'); thumbprint = Hex40; subject = 'CN=WinMacResearch' }; [void](New-Item -ItemType Directory -Path (Join-Path $base 'CallProbeP3\zzz')); Save-P3State $x2
Check (ThrowsLike { Invoke-P3Uninstall $base '' $ops } 'P3-PRECONDICION*RunId*') 'varias pendientes sin -RunId → se detiene'
Check (ThrowsLike { Invoke-P3Uninstall $base 'noexiste' $ops } 'P3-PRECONDICION*') 'RunId inexistente → se detiene'
$r = Invoke-P3Uninstall $base 'zzz' $ops; Check ($r.Complete) 'limpia solo la ejecución indicada'
Check (@(Get-P3PendingRuns $base).Count -eq 1) 'la otra ejecución sigue pendiente (no se tocó)'

# 13. Estado ilegible
$w = New-World; $base = New-Base; $ops = New-FakeOps $w; $dd = Join-Path $base 'CallProbeP3\broken'; [void](New-Item -ItemType Directory -Path $dd); Set-Content (Join-Path $dd 'state.json') '{ no es json'; Set-Content (Join-Path $dd 'x.txt') 'dato'
Check (ThrowsLike { Invoke-P3Uninstall $base '' $ops } 'P3-PRECONDICION*ilegible*') 'estado ilegible → no se adivina'
Check (Test-Path (Join-Path $dd 'x.txt')) 'estado ilegible: archivos intactos'
Check (ThrowsLike { Run-Install $w $base $ops } 'P3-PRECONDICION*sin cerrar*') 'estado ilegible bloquea instalar'

# 14. Huella inválida con certCreated → ABORTO sin borrar por Subject
$w = New-World; $dec = Add-Decoys $w; $base = New-Base; $ops = New-FakeOps $w; $rd = Join-Path $base 'CallProbeP3\r1'; [void](New-Item -ItemType Directory -Path $rd)
$st = @{ runId = 'r1'; runDir = $rd; statePath = (Join-Path $rd 'state.json'); status = 'in-progress'; subject = 'CN=WinMacResearch'; thumbprint = '*'; certCreated = $true; trustImportAttempted = $true; packageInstallAttempted = $false }; Save-P3State $st
$r = Invoke-P3Rollback $st $ops; Check (-not $r.Complete -and $w.RemovedCerts.Count -eq 0 -and (Decoys-Intact $w $dec)) 'huella inválida: no se borra nada (ni por Subject)'
# 15. Mismo hash pero otro sujeto → no se borra
$w = New-World; $base = New-Base; $ops = New-FakeOps $w; $t = Hex40; [void]$w.Stores[$TP].Add((New-Cert 'CN=Otro' $t)); $rd = Join-Path $base 'CallProbeP3\r2'; [void](New-Item -ItemType Directory -Path $rd)
$st = @{ runId = 'r2'; runDir = $rd; statePath = (Join-Path $rd 'state.json'); status = 'in-progress'; subject = 'CN=WinMacResearch'; thumbprint = $t; certCreated = $false; trustImportAttempted = $true; packageInstallAttempted = $false }; Save-P3State $st
$r = Invoke-P3Rollback $st $ops; Check (-not $r.Complete -and $w.RemovedCerts.Count -eq 0) 'sujeto distinto con la huella registrada → no se borra'
# 16. Carpeta con forma inesperada → no se borra
$w = New-World; $base = New-Base; $ops = New-FakeOps $w; $rd = Join-Path $base 'otra-cosa\r3'; [void](New-Item -ItemType Directory -Path $rd); Set-Content (Join-Path $rd 'valioso.txt') 'x'
$st = @{ runId = 'r3'; runDir = $rd; statePath = (Join-Path $rd 'state.json'); status = 'in-progress'; subject = 'CN=WinMacResearch'; thumbprint = ''; certCreated = $false; trustImportAttempted = $false; packageInstallAttempted = $false }; Save-P3State $st
$r = Invoke-P3Rollback $st $ops; Check ((Test-Path (Join-Path $rd 'valioso.txt')) -and -not $r.Complete) 'carpeta fuera de CallProbeP3\<id> no se borra'
# 17. Decisión sobre paquetes (tabla)
$base0 = @{ subject = 'CN=WinMacResearch'; packageInstallAttempted = $true; packageVersion = '0.1.0.0'; packageFullName = 'F'; preexistingPackage = $false }
Check ((Get-P3PackageDecision $base0 (New-Pkg '0.1.0.0' 'Developer' 'F')) -eq 'remove') 'decisión: idéntico → remove'
Check ((Get-P3PackageDecision $base0 (New-Pkg '0.1.0.0' 'None' 'F')) -eq 'remove') 'decisión: nombre completo anotado y exacto → remove aunque Windows informe None'
Check ((Get-P3PackageDecision $base0 (New-Pkg '0.1.0.0' 'Developer' 'G')) -eq 'foreign') 'decisión: otro nombre completo → foreign'
Check ((Get-P3PackageDecision $base0 (New-Pkg '0.2.0.0' 'Developer' 'F')) -eq 'foreign') 'decisión: otra versión → foreign'
$noTry = $base0.Clone(); $noTry.packageInstallAttempted = $false
Check ((Get-P3PackageDecision $noTry (New-Pkg '0.1.0.0' 'Developer' 'F')) -eq 'foreign') 'decisión: no intentó instalar → foreign'
$noName = $base0.Clone(); $noName.packageFullName = ''
Check ((Get-P3PackageDecision $noName (New-Pkg '0.1.0.0' 'None' 'F')) -eq 'foreign') 'decisión: sin nombre anotado y firma None (paquete suelto) → foreign'
Check ((Get-P3PackageDecision $noName (New-Pkg '0.1.0.0' 'Developer' 'F')) -eq 'remove') 'decisión: sin nombre anotado, firmado, misma identidad → remove'
Check ((Get-P3PackageDecision $base0 $null) -eq 'none') 'decisión: sin paquete → none'

Write-Host ("P3: {0} comprobaciones correctas, {1} fallidas" -f $script:pass, $script:fail)
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
