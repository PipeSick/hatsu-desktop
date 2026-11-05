<#
.SYNOPSIS
  Empaqueta dist\WinMac como MSIX con la identidad OFICIAL de Partner Center (store-identity.json). NO publica nada ni lo envía a certificación. El MSI no se toca.
.DESCRIPTION
  Siempre produce:
    HatsuDesktop-<v>-store-unsigned.msix   ← el artefacto para subir a Partner Center. SIN firmar a propósito: la Store lo firma tras certificar.
  Con -Sign produce además (solo para CI):
    HatsuDesktop-<v>-citest-signed.msix    ← los MISMOS bytes firmados con un certificado AUTOFIRMADO efímero cuyo asunto es el Publisher oficial. Sirve únicamente para
                                              instalarlo en el runner de CI. No es de confianza en ningún PC y NO demuestra nada sobre Smart App Control.
  Antes de terminar comprueba que el AppxManifest.xml DENTRO del .msix tiene exactamente la identidad de store-identity.json.
  -Variant PhoneCall: igual que Default + la capacidad GENERAL `phoneCall` (NO restringida; docs/59) para el vuelo Beta de diagnóstico. Solo si el propietario lo decide; la publicación estable usa Default.
  -Variant Unvirtualized: experimento de virtualización (capacidad restringida unvirtualizedResources; no necesaria, ver docs/52); nunca para la Store.
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'msix-out', [ValidateSet('Default', 'Unvirtualized', 'PhoneCall')][string]$Variant = 'Default', [switch]$Sign)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$id = Get-Content (Join-Path $PSScriptRoot 'store-identity.json') -Raw | ConvertFrom-Json
[xml]$props = Get-Content (Join-Path $root 'src\Directory.Build.props'); $ver = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$msixVersion = (($ver -split '[.-]')[0..2] -join '.') + '.0'   # la Store exige la 4.ª cifra a 0
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Where-Object { Test-Path (Join-Path $_.FullName 'makeappx.exe') } | Select-Object -First 1
if (-not $sdk) { throw 'No se encontró makeappx.exe (Windows SDK).' }
$makeappx = Join-Path $sdk.FullName 'makeappx.exe'; $signtool = Join-Path $sdk.FullName 'signtool.exe'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null; $OutDir = (Resolve-Path $OutDir).Path
$stage = Join-Path $OutDir "stage-$Variant"; if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Copy-Item (Resolve-Path $DistDir).Path $stage -Recurse
# Fuera del paquete lo que solo tiene sentido en una instalación clásica
foreach ($f in 'Uninstall-HatsuDesktop.ps1', 'SHA256SUMS.txt', 'Migrate-FromWinMac.ps1') { Remove-Item (Join-Path $stage $f) -Force -ErrorAction SilentlyContinue }
# Logotipos desde el icono de marca (tamaños que exige el manifiesto)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\brand\hatsu-flux-256.png')); New-Item -ItemType Directory -Force (Join-Path $stage 'Assets') | Out-Null
foreach ($a in @(@('Square44x44Logo', 44), @('Square150x150Logo', 150), @('StoreLogo', 50))) {
  $bmp = New-Object System.Drawing.Bitmap $a[1], $a[1]; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($src, 0, 0, $a[1], $a[1]); $g.Dispose()
  $bmp.Save((Join-Path $stage "Assets\$($a[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$src.Dispose()
$virt = if ($Variant -eq 'Unvirtualized') { "    <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>`n    <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>" } else { '' }
# Esquema: primero las Capability (generales/restringidas), después las DeviceCapability.
$caps = $(if ($Variant -eq 'PhoneCall') { "    <uap:Capability Name=`"phoneCall`" />`n" } else { '' }) + $(if ($Variant -eq 'Unvirtualized') { "    <rescap:Capability Name=`"unvirtualizedResources`" />`n" } else { '' }) + '    <DeviceCapability Name="bluetooth" />' + "`n" + '    <DeviceCapability Name="location" />' + "`n" + '    <DeviceCapability Name="webcam" />'
$m = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml.in') -Raw -Encoding UTF8).Replace('@@IDENTITY_NAME@@', $id.identityName).Replace('@@PUBLISHER@@', $id.publisher).Replace('@@PUBLISHER_DISPLAY@@', $id.publisherDisplayName).Replace('@@VERSION@@', $msixVersion).Replace('@@VIRTUALIZATION@@', $virt).Replace('@@CAPABILITIES@@', $caps)
if ($m -match '@@[A-Z_]+@@') { throw 'Quedan marcadores @@ sin sustituir en el manifiesto.' }
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $m, (New-Object System.Text.UTF8Encoding($false)))
$suffix = if ($Variant -eq 'Default') { 'store-unsigned' } elseif ($Variant -eq 'PhoneCall') { 'beta-phonecall-unsigned' } else { "experimento-$Variant-unsigned" }
$pkg = Join-Path $OutDir "HatsuDesktop-$msixVersion-$suffix.msix"; if (Test-Path $pkg) { Remove-Item $pkg }
$mo = & $makeappx pack /d $stage /p $pkg /o /nv 2>&1; if ($LASTEXITCODE -ne 0) { $mo | Select-Object -Last 12 | Out-Host; throw "makeappx falló ($LASTEXITCODE)" }

# Comprobación: la identidad DENTRO del paquete es exactamente la de Partner Center
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($pkg)
try { $e = $zip.GetEntry('AppxManifest.xml'); $rd = New-Object IO.StreamReader($e.Open()); [xml]$inner = $rd.ReadToEnd(); $rd.Dispose() } finally { $zip.Dispose() }
$ident = $inner.Package.Identity
foreach ($pair in @(@('Name', $id.identityName), @('Publisher', $id.publisher))) { if ($ident.($pair[0]) -ne $pair[1]) { throw "Identidad incorrecta: $($pair[0]) = '$($ident.($pair[0]))', se esperaba '$($pair[1])'" } }
if ($inner.Package.Properties.PublisherDisplayName -ne $id.publisherDisplayName) { throw 'PublisherDisplayName no coincide con Partner Center.' }
Write-Host "Identidad verificada dentro del .msix: $($ident.Name) | $($ident.Publisher) | $($inner.Package.Properties.PublisherDisplayName) | v$($ident.Version)"
$h = (Get-FileHash $pkg -Algorithm SHA256).Hash.ToLowerInvariant(); "$h  $(Split-Path $pkg -Leaf)" | Set-Content "$pkg.sha256" -Encoding ASCII
Write-Host "MSIX ($Variant) SIN FIRMAR: $pkg  [$([math]::Round((Get-Item $pkg).Length / 1MB, 1)) MB]  SHA-256 $h"

if ($Sign) {
  if ($Variant -ne 'Default') { throw '-Sign solo con la variante Default.' }
  $signed = Join-Path $OutDir "HatsuDesktop-$msixVersion-citest-signed.msix"; Copy-Item $pkg $signed -Force
  $cert = New-SelfSignedCertificate -Type Custom -Subject $id.publisher -KeyUsage DigitalSignature -FriendlyName 'Hatsu Desktop CI test (NO confiable)' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  $pwd = ConvertTo-SecureString -String ([Guid]::NewGuid().ToString('N')) -Force -AsPlainText; $pfx = Join-Path $OutDir 'ci-test.pfx'
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null; Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'ci-test.cer') | Out-Null
  & $signtool sign /fd SHA256 /a /f $pfx /p ([System.Net.NetworkCredential]::new('', $pwd).Password) $signed | Out-Host; if ($LASTEXITCODE -ne 0) { throw "signtool falló ($LASTEXITCODE)" }
  Remove-Item $pfx -Force   # la clave privada no sale del script
  Write-Host "Paquete de PRUEBA firmado (solo CI, certificado autofirmado): $signed"
}
