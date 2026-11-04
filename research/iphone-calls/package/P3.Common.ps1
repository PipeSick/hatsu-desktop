<#
  Lógica común de P3 (paquete MSIX FIRMADO de investigación). Se carga con «. .\P3.Common.ps1».
  Toda acción con efecto sobre Windows pasa por el objeto $Ops (inyectable): en producción son las operaciones reales (New-P3RealOps);
  en Test-P3.ps1 son simulaciones en memoria. Así se prueban la detección de conflictos, la recuperación y la limpieza sin tocar ningún sistema.

  REGLAS DE SEGURIDAD (comprobadas por Test-P3.ps1):
   - Un certificado solo se borra por HUELLA EXACTA (40 hex) registrada en el estado de ESA ejecución, nunca por Subject, nombre o patrón.
   - Un paquete solo se quita si su identidad coincide con lo registrado por esa ejecución; si hay una identidad inesperada, se DETIENE todo.
   - Nada se borra fuera de la carpeta de la propia ejecución. Las carpetas/estados de otras ejecuciones no se tocan.
   - La huella se guarda en disco ANTES de cualquier operación posterior que pueda fallar, y el estado (state.json) nunca se borra.
#>
$script:P3 = @{
  PackageName = 'WinMac.Research.CallProbe'
  Publisher   = 'CN=WinMacResearch'
  Capabilities = @('bluetooth', 'phoneCall', 'phoneLineTransportManagement', 'runFullTrust')
  CertValidityDays = 30
  TrustStore = 'LocalMachine\TrustedPeople'
  KeyStore   = 'CurrentUser\My'
}

# ───────────── validaciones puras
function Test-P3Thumbprint([string]$Thumbprint) { return [bool]($Thumbprint -match '^[0-9A-Fa-f]{40}$') }

function ConvertTo-P3NormalizedDn([string]$Dn) { return (($Dn -split ',') | ForEach-Object { $_.Trim() }) -join ',' }

function Test-P3PublisherMatch([string]$ManifestPublisher, [string]$CertSubject) {
  if ([string]::IsNullOrWhiteSpace($ManifestPublisher) -or [string]::IsNullOrWhiteSpace($CertSubject)) { return $false }
  return [string]::Equals((ConvertTo-P3NormalizedDn $ManifestPublisher), (ConvertTo-P3NormalizedDn $CertSubject), [StringComparison]::OrdinalIgnoreCase)
}

function Get-P3ManifestFacts([string]$XmlText) {
  $x = [xml]$XmlText
  $id = $x.Package.Identity
  $caps = @()
  if ($x.Package.Capabilities) { foreach ($c in $x.Package.Capabilities.ChildNodes) { if ($c.NodeType -eq 'Element' -and $c.Name) { $caps += [string]$c.GetAttribute('Name') } } }
  $exe = @(); if ($x.Package.Applications) { foreach ($a in $x.Package.Applications.ChildNodes) { if ($a.NodeType -eq 'Element') { $exe += [string]$a.GetAttribute('Executable') } } }
  return [pscustomobject]@{ Name = [string]$id.Name; Publisher = [string]$id.Publisher; Version = [string]$id.Version; Arch = [string]$id.ProcessorArchitecture
    Capabilities = @($caps | Sort-Object); Executables = $exe }
}

# Devuelve la lista de problemas (vacía = identidad y capacidades exactamente las previstas).
function Test-P3ManifestFacts($Facts, [string]$ExpectedSubject) {
  $p = @()
  if ($Facts.Name -ne $script:P3.PackageName) { $p += "Identity/Name es '$($Facts.Name)', se esperaba '$($script:P3.PackageName)'." }
  if (-not (Test-P3PublisherMatch $Facts.Publisher $ExpectedSubject)) { $p += "Publisher del manifiesto '$($Facts.Publisher)' no coincide con el sujeto del certificado '$ExpectedSubject'." }
  $want = @($script:P3.Capabilities | Sort-Object); $have = @($Facts.Capabilities)
  if (($want -join '|') -ne ($have -join '|')) { $p += "Capacidades distintas de las previstas. Esperadas: $($want -join ', '). Encontradas: $($have -join ', ')." }
  if (@($Facts.Executables) -notcontains 'CallProbe.exe') { $p += 'El manifiesto no declara CallProbe.exe como ejecutable.' }
  return $p
}

# ───────────── estado en disco (atómico; nunca se borra)
function ConvertTo-P3Hashtable($Obj) {
  $h = @{}; if ($null -eq $Obj) { return $h }
  foreach ($prop in $Obj.PSObject.Properties) { $h[$prop.Name] = $prop.Value }
  return $h
}

function Save-P3State([hashtable]$State) {
  $path = $State.statePath; $tmp = "$path.tmp"
  [System.IO.File]::WriteAllText($tmp, ($State | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
  Move-Item -LiteralPath $tmp -Destination $path -Force
}

function Read-P3State([string]$Path) {
  $s = ConvertTo-P3Hashtable (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
  $s.statePath = $Path; return $s
}

function Get-P3RunsRoot([string]$Base) { return (Join-Path $Base 'CallProbeP3') }

# Ejecuciones no cerradas (status distinto de 'cleaned'). Un estado ilegible cuenta como pendiente (se detiene, no se adivina).
function Get-P3PendingRuns([string]$Base) {
  $root = Get-P3RunsRoot $Base; $out = @()
  if (-not (Test-Path -LiteralPath $root)) { return $out }
  foreach ($d in Get-ChildItem -LiteralPath $root -Directory) {
    $f = Join-Path $d.FullName 'state.json'
    if (-not (Test-Path -LiteralPath $f)) { continue }
    try { $s = Read-P3State $f; if ($s.status -ne 'cleaned') { $out += $s } }
    catch { $out += @{ runId = $d.Name; status = 'unreadable'; statePath = $f } }
  }
  return $out
}

function New-P3RunId { return ('{0}-{1}' -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'), ([guid]::NewGuid().ToString('N').Substring(0, 8))) }

# ───────────── decisiones puras
# 'none' = no hay paquete con esa identidad · 'remove' = es el de esta ejecución · 'foreign' = identidad inesperada: DETENERSE
function Get-P3PackageDecision([hashtable]$State, $Pkg) {
  if ($null -eq $Pkg) { return 'none' }
  if ($Pkg.Name -ne $script:P3.PackageName) { return 'foreign' }
  if ($State.preexistingPackage) { return 'foreign' }                     # ya estaba antes de empezar: no es nuestro
  if (-not (Test-P3PublisherMatch $Pkg.Publisher $State.subject)) { return 'foreign' }
  if ($State.packageVersion -and "$($Pkg.Version)" -ne "$($State.packageVersion)") { return 'foreign' }
  if ($State.packageFullName) {
    # Esta ejecución ya anotó el nombre completo exacto del paquete que instaló: solo ése es suyo (aunque Windows lo informe con firma inesperada).
    if ("$($Pkg.PackageFullName)" -ne "$($State.packageFullName)") { return 'foreign' }
  }
  elseif ("$($Pkg.SignatureKind)" -eq 'None') { return 'foreign' }       # sin nombre anotado: un paquete sin firma es el «suelto» de Register-DevPackage, NO el de P3
  if (-not $State.packageInstallAttempted) { return 'foreign' }           # esta ejecución nunca intentó instalar: no puede ser suyo
  return 'remove'
}

# ───────────── recuperación / limpieza (la usan el instalador ante un fallo y el desinstalador)
function Invoke-P3Rollback([hashtable]$State, $Ops) {
  $msgs = New-Object System.Collections.ArrayList; $complete = $true
  function Say($m) { [void]$msgs.Add($m); & $Ops.Log $m }

  $hasThumb = Test-P3Thumbprint $State.thumbprint
  if ($State.certCreated -and -not $hasThumb) { Say 'ABORTO: el estado dice que se creó un certificado pero la huella no es válida; no se borra nada por Subject. Revisa el estado a mano.'; $State.status = 'cleanup-incomplete'; Save-P3State $State; return @{ Complete = $false; Messages = $msgs } }

  # 1) Paquete: solo si esta ejecución llegó a intentar instalarlo, y solo si es exactamente el suyo
  if ($State.packageInstallAttempted) {
    $pkgs = @(& $Ops.GetPackages $script:P3.PackageName)
    if ($pkgs.Count -gt 1) { Say "ABORTO: hay $($pkgs.Count) paquetes con la identidad $($script:P3.PackageName); no se quita ninguno."; $State.status = 'cleanup-incomplete'; Save-P3State $State; return @{ Complete = $false; Messages = $msgs } }
    if ($pkgs.Count -eq 1) {
      $d = Get-P3PackageDecision $State $pkgs[0]
      if ($d -eq 'foreign') {
        Say "ABORTO: existe un paquete $($script:P3.PackageName) que NO es el de esta ejecución (versión $($pkgs[0].Version), firma $($pkgs[0].SignatureKind)). No se toca nada más. El certificado de esta ejecución (huella $($State.thumbprint)) sigue de confianza hasta que lo resuelvas."
        $State.status = 'cleanup-incomplete'; Save-P3State $State; return @{ Complete = $false; Messages = $msgs }
      }
      Say "Quitando el paquete $($pkgs[0].PackageFullName)"; & $Ops.RemovePackage $pkgs[0].PackageFullName
      if (@(& $Ops.GetPackages $script:P3.PackageName).Count -ne 0) { Say 'NO se pudo quitar el paquete.'; $complete = $false }
    } else { Say 'No hay paquete instalado por esta ejecución.' }
  }

  # 2) Certificados: solo por huella exacta y comprobando el sujeto
  if ($hasThumb) {
    foreach ($spec in @(@{ Store = $script:P3.TrustStore; Attempted = $State.trustImportAttempted }, @{ Store = $script:P3.KeyStore; Attempted = $State.certCreated })) {
      if (-not $spec.Attempted) { continue }
      $c = & $Ops.GetCert $spec.Store $State.thumbprint
      if ($null -eq $c) { Say "Certificado $($State.thumbprint) ya no está en $($spec.Store)."; continue }
      if ($c.Thumbprint -ne $State.thumbprint -or -not (Test-P3PublisherMatch $c.Subject $State.subject)) { Say "ABORTO: el certificado hallado en $($spec.Store) no coincide con la huella/sujeto registrados; no se borra."; $complete = $false; continue }
      Say "Quitando el certificado $($State.thumbprint) de $($spec.Store)"; & $Ops.RemoveCert $spec.Store $State.thumbprint
      if ($null -ne (& $Ops.GetCert $spec.Store $State.thumbprint)) { Say "NO se pudo quitar el certificado de $($spec.Store)."; $complete = $false }
    }
  }

  # 3) Archivos: solo los de la carpeta de ESTA ejecución, salvo state.json (queda como registro)
  if ($complete -and $State.runDir -and (Test-Path -LiteralPath $State.runDir)) {
    $runDir = [System.IO.Path]::GetFullPath($State.runDir)
    $leaf = Split-Path $runDir -Leaf
    if ($leaf -ne $State.runId -or (Split-Path (Split-Path $runDir -Parent) -Leaf) -ne 'CallProbeP3') { Say "AVISO: la carpeta $runDir no tiene la forma esperada; no se borra."; $complete = $false }
    else { foreach ($item in Get-ChildItem -LiteralPath $runDir -Force) { if ($item.Name -notin @('state.json')) { Remove-Item -LiteralPath $item.FullName -Recurse -Force } } }
  }

  $State.status = if ($complete) { 'cleaned' } else { 'cleanup-incomplete' }
  $State.cleanedUtc = [DateTime]::UtcNow.ToString('o'); Save-P3State $State
  return @{ Complete = $complete; Messages = $msgs }
}

# ───────────── instalación
function Invoke-P3Install([string]$Base, [string]$BuildDir, [string]$ManifestPath, $Ops) {
  $log = $Ops.Log
  # --- precondiciones: si fallan NO se ha creado nada
  $pending = @(Get-P3PendingRuns $Base)
  if ($pending.Count -gt 0) { throw "P3-PRECONDICION: hay una ejecución anterior sin cerrar ($(($pending | ForEach-Object { $_.runId }) -join ', ')). Ejecuta primero Uninstall-SignedDevPackage.ps1. No se ha cambiado nada." }
  $existing = @(& $Ops.GetPackages $script:P3.PackageName)
  if ($existing.Count -gt 0) { throw "P3-PRECONDICION: ya existe una identidad $($script:P3.PackageName) (versión $($existing[0].Version), firma $($existing[0].SignatureKind)). No es de P3: no se toca. Si es el paquete de desarrollo sin firma, quítalo tú con Unregister-DevPackage.ps1 y repite. No se ha cambiado nada." }
  $tools = & $Ops.FindTools
  if ($null -eq $tools) { throw 'P3-PRECONDICION: no encuentro makeappx.exe y signtool.exe (Windows SDK, carpeta x64). No se ha cambiado nada.' }
  $facts = Get-P3ManifestFacts (Get-Content -LiteralPath $ManifestPath -Raw)
  $problems = @(Test-P3ManifestFacts $facts $script:P3.Publisher)
  if ($problems.Count -gt 0) { throw "P3-PRECONDICION: el manifiesto de origen no es el previsto: $($problems -join ' ') No se ha cambiado nada." }
  if (-not (& $Ops.Confirm $tools)) { throw 'P3-CANCELADO: no se confirmó. No se ha cambiado nada.' }

  # --- estado: se escribe ANTES de crear nada
  $runId = New-P3RunId; $runDir = Join-Path (Get-P3RunsRoot $Base) $runId
  [void](New-Item -ItemType Directory -Force -Path $runDir)
  $state = @{ runId = $runId; runDir = $runDir; statePath = (Join-Path $runDir 'state.json'); status = 'in-progress'; startedUtc = [DateTime]::UtcNow.ToString('o')
    subject = $script:P3.Publisher; thumbprint = ''; certCreated = $false; trustImportAttempted = $false; packageInstallAttempted = $false
    preexistingPackage = $false; packageVersion = $facts.Version; packageFullName = ''; signatureKind = ''; sdk = $tools.Version }
  Save-P3State $state
  & $log "Ejecución $runId · estado en $($state.statePath)"

  try {
    $content = Join-Path $runDir 'content'; $cer = Join-Path $runDir 'CallProbe-P3.cer'; $msix = Join-Path $runDir 'CallProbe-P3.msix'
    & $Ops.PrepareContent $BuildDir $content $ManifestPath

    $cert = & $Ops.NewCert $script:P3.Publisher $script:P3.CertValidityDays $runId
    $state.thumbprint = [string]$cert.Thumbprint; $state.certCreated = $true; Save-P3State $state          # huella a disco ANTES de lo siguiente
    & $log "Certificado creado. HUELLA: $($state.thumbprint)"
    if (-not (Test-P3Thumbprint $state.thumbprint)) { throw "La huella del certificado no es válida: '$($state.thumbprint)'." }
    if (-not (Test-P3PublisherMatch $facts.Publisher $cert.Subject)) { throw "Publisher del manifiesto '$($facts.Publisher)' ≠ sujeto del certificado '$($cert.Subject)'." }

    & $Ops.ExportCert $cert $cer
    $cerThumb = & $Ops.CerThumbprint $cer
    if ($cerThumb -ne $state.thumbprint) { throw "La huella del .cer exportado ($cerThumb) no es la del certificado creado." }

    & $Ops.Pack $tools $content $msix
    $packed = Get-P3ManifestFacts (& $Ops.ReadMsixManifest $msix)
    $problems = @(Test-P3ManifestFacts $packed $cert.Subject)
    if ($problems.Count -gt 0) { throw "El MSIX empaquetado no tiene la identidad/capacidades previstas: $($problems -join ' ')" }

    & $Ops.Sign $tools $msix $state.thumbprint

    $state.trustImportAttempted = $true; Save-P3State $state                                              # intención a disco ANTES de importar
    & $Ops.ImportTrust $cer
    if ($null -eq (& $Ops.GetCert $script:P3.TrustStore $state.thumbprint)) { throw 'El certificado no aparece en TrustedPeople tras importarlo.' }

    $v = & $Ops.Verify $tools $msix
    if (("$v").IndexOf($state.thumbprint, [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'signtool verify no muestra la huella esperada: el MSIX no está firmado con el certificado de esta ejecución.' }

    $state.packageInstallAttempted = $true; Save-P3State $state                                           # intención a disco ANTES de instalar
    & $Ops.AddPackage $msix
    $pkgs = @(& $Ops.GetPackages $script:P3.PackageName)
    if ($pkgs.Count -ne 1) { throw "Tras instalar hay $($pkgs.Count) paquetes con la identidad prevista (se esperaba 1)." }
    $pkg = $pkgs[0]
    $state.packageFullName = [string]$pkg.PackageFullName; $state.signatureKind = [string]$pkg.SignatureKind; Save-P3State $state
    if (-not (Test-P3PublisherMatch $pkg.Publisher $cert.Subject)) { throw "El Publisher instalado '$($pkg.Publisher)' no coincide con el sujeto del certificado." }
    if ("$($pkg.SignatureKind)" -eq 'None') { throw 'Windows registra el paquete con firma None: la prueba P3 no tendría sentido.' }
    $inst = Get-P3ManifestFacts (& $Ops.ReadInstalledManifest $pkg.InstallLocation)
    $problems = @(Test-P3ManifestFacts $inst $cert.Subject)
    if ($problems.Count -gt 0) { throw "El manifiesto INSTALADO no es el previsto: $($problems -join ' ')" }

    $state.status = 'verified-installed'; Save-P3State $state
    return $state
  }
  catch {
    $err = $_.Exception.Message
    & $log "FALLO: $err"
    & $log 'Recuperación automática: se deshace SOLO lo creado por esta ejecución.'
    $rb = Invoke-P3Rollback $state $Ops
    $state.failure = $err; Save-P3State $state
    if ($rb.Complete) { throw "P3-FALLO (revertido por completo): $err" }
    throw "P3-FALLO-INCOMPLETO: $err · La limpieza NO terminó. Huella del certificado: $($state.thumbprint). Ejecuta Uninstall-SignedDevPackage.ps1 -RunId $($state.runId) o lee $($state.statePath)."
  }
}

# ───────────── desinstalación
function Invoke-P3Uninstall([string]$Base, [string]$RunId, $Ops) {
  $pending = @(Get-P3PendingRuns $Base)
  if ($pending.Count -eq 0) { & $Ops.Log 'No hay ninguna ejecución de P3 pendiente de limpiar. No se toca nada.'; return $null }
  if ($RunId) { $pending = @($pending | Where-Object { $_.runId -eq $RunId }); if ($pending.Count -eq 0) { throw "P3-PRECONDICION: no hay ejecución pendiente con RunId '$RunId'." } }
  if ($pending.Count -gt 1) { throw "P3-PRECONDICION: hay $($pending.Count) ejecuciones pendientes ($(($pending | ForEach-Object { $_.runId }) -join ', ')). Indica una con -RunId; no se elige al azar." }
  $s = $pending[0]
  if ($s.status -eq 'unreadable') { throw "P3-PRECONDICION: el estado de $($s.runId) es ilegible ($($s.statePath)). No se borra nada a ciegas." }
  & $Ops.Log "Limpiando la ejecución $($s.runId) (huella $($s.thumbprint))"
  return (Invoke-P3Rollback $s $Ops)
}

# ───────────── operaciones REALES (solo Windows PowerShell 5.1 elevado)
function New-P3RealOps {
  $ops = @{}
  $ops.Log = { param($m) Write-Host ("[P3] " + $m) }
  $ops.Confirm = {
    param($tools)
    Write-Host ''; Write-Host 'P3 va a hacer estos cambios PERSISTENTES en este Windows (todos se deshacen con Uninstall-SignedDevPackage.ps1):'
    Write-Host '  1. Carpeta %LOCALAPPDATA%\CallProbeP3\<id>\ con copia de la app, .cer, .msix y state.json.'
    Write-Host '  2. Certificado autofirmado CN=WinMacResearch (30 días, clave NO exportable) en CurrentUser\My.'
    Write-Host '  3. Su parte pública en LocalMachine\TrustedPeople (confianza para paquetes MSIX firmados con ESE certificado).'
    Write-Host '  4. Paquete MSIX WinMac.Research.CallProbe instalado para tu usuario.'
    Write-Host "  SDK: $($tools.Version). No se tocan Defender, Smart App Control, directivas, Bluetooth, audio ni emparejamientos."
    return ((Read-Host 'Escribe INSTALAR-P3 para continuar') -ceq 'INSTALAR-P3')
  }
  $ops.GetPackages = { param($name) @(Get-AppxPackage -AllUsers -Name $name -ErrorAction SilentlyContinue) }
  $ops.RemovePackage = { param($full) Remove-AppxPackage -Package $full }
  $ops.GetCert = { param($store, $thumb) Get-Item -LiteralPath "Cert:\$store\$thumb" -ErrorAction SilentlyContinue }
  $ops.RemoveCert = { param($store, $thumb) if ($store -like '*\My') { Remove-Item -LiteralPath "Cert:\$store\$thumb" -DeleteKey -Force } else { Remove-Item -LiteralPath "Cert:\$store\$thumb" -Force } }
  $ops.NewCert = {
    param($subject, $days, $runId)
    New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature -FriendlyName "WinMac CallProbe P3 (investigación) $runId" `
      -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays($days) `
      -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  }
  $ops.ExportCert = { param($cert, $path) [void](Export-Certificate -Cert $cert -FilePath $path) }
  $ops.CerThumbprint = { param($path) (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $path).Thumbprint }
  $ops.ImportTrust = { param($path) [void](Import-Certificate -FilePath $path -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople') }
  $ops.FindTools = {
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $root)) { return $null }
    $dirs = Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending
    foreach ($d in $dirs) { $x = Join-Path $d.FullName 'x64'; if ((Test-Path "$x\makeappx.exe") -and (Test-Path "$x\signtool.exe")) { return [pscustomobject]@{ MakeAppx = "$x\makeappx.exe"; SignTool = "$x\signtool.exe"; Version = $d.Name } } }
    return $null
  }
  $ops.PrepareContent = {
    param($buildDir, $content, $manifest)
    if (-not (Test-Path -LiteralPath (Join-Path $buildDir 'CallProbe.exe'))) { throw "No encuentro CallProbe.exe en $buildDir (dotnet publish CallProbe -c Release -r win-x64 --self-contained -o <carpeta>)." }
    [void](New-Item -ItemType Directory -Force -Path "$content\Assets")
    Copy-Item -Path (Join-Path $buildDir '*') -Destination $content -Recurse -Force
    Copy-Item -LiteralPath $manifest -Destination "$content\AppxManifest.xml" -Force
    Add-Type -AssemblyName System.Drawing
    foreach ($spec in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
      $bmp = New-Object System.Drawing.Bitmap $spec[1], $spec[1]; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 40, 40, 48)); $g.Dispose()
      $bmp.Save("$content\Assets\$($spec[0])", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    }
  }
  $ops.Pack = { param($tools, $content, $msix) & $tools.MakeAppx pack /d $content /p $msix /o | Out-Host; if ($LASTEXITCODE -ne 0) { throw "makeappx falló (código $LASTEXITCODE)." } }
  $ops.Sign = { param($tools, $msix, $thumb) & $tools.SignTool sign /fd SHA256 /sha1 $thumb /s My $msix | Out-Host; if ($LASTEXITCODE -ne 0) { throw "signtool sign falló (código $LASTEXITCODE)." } }
  $ops.Verify = { param($tools, $msix) $o = (& $tools.SignTool verify /pa /v $msix 2>&1 | Out-String); if ($LASTEXITCODE -ne 0) { throw "signtool verify falló (código $LASTEXITCODE): $o" }; $o }
  $ops.ReadMsixManifest = {
    param($msix)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [System.IO.Compression.ZipFile]::OpenRead($msix)
    try { $e = $z.GetEntry('AppxManifest.xml'); $r = New-Object System.IO.StreamReader($e.Open()); try { $r.ReadToEnd() } finally { $r.Dispose() } } finally { $z.Dispose() }
  }
  $ops.ReadInstalledManifest = { param($loc) Get-Content -LiteralPath (Join-Path $loc 'AppxManifest.xml') -Raw }
  $ops.AddPackage = { param($msix) Add-AppxPackage -Path $msix }
  return $ops
}
