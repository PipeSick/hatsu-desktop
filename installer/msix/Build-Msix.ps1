<#
.SYNOPSIS
  EXPERIMENTAL (Fase 3.2): empaqueta dist\WinMac como MSIX. NO publica nada ni se envía a la Store. El MSI actual no se toca.
.DESCRIPTION
  -Variant Default      virtualización estándar de MSIX (HKCU/AppData de escritura redirigidos al paquete).
  -Variant Unvirtualized  desactiva la virtualización de registro y de archivos (capacidad restringida unvirtualizedResources: en la Store exige justificación/aprobación).
  -Sign   Firma con un certificado AUTOFIRMADO efímero SOLO para la prueba en CI (no es de confianza ni sirve para distribuir; no demuestra nada sobre Smart App Control).
  Sin -Sign el paquete queda sin firmar: es lo que Partner Center acepta (la Store lo firma ella).
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'msix-out', [ValidateSet('Default', 'Unvirtualized')][string]$Variant = 'Default', [switch]$Sign,
      [string]$IdentityName = 'HatsuDesktop.CiTest', [string]$Publisher = 'CN=HatsuDesktopCiTest', [string]$PublisherDisplay = 'Hatsu Desktop (prueba CI)')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
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
$caps = '    <DeviceCapability Name="bluetooth" />' + $(if ($Variant -eq 'Unvirtualized') { "`n    <rescap:Capability Name=`"unvirtualizedResources`" />" } else { '' })
$m = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml.in') -Raw -Encoding UTF8).Replace('@@IDENTITY_NAME@@', $IdentityName).Replace('@@PUBLISHER@@', $Publisher).Replace('@@PUBLISHER_DISPLAY@@', $PublisherDisplay).Replace('@@VERSION@@', $msixVersion).Replace('@@VIRTUALIZATION@@', $virt).Replace('@@CAPABILITIES@@', $caps)
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $m, (New-Object System.Text.UTF8Encoding($false)))
$pkg = Join-Path $OutDir "HatsuDesktop-$msixVersion-$Variant.msix"; if (Test-Path $pkg) { Remove-Item $pkg }
& $makeappx pack /d $stage /p $pkg /o /nv | Out-Host; if ($LASTEXITCODE -ne 0) { throw "makeappx falló ($LASTEXITCODE)" }
if ($Sign) {
  $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName 'Hatsu Desktop CI test (NO confiable)' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  $pwd = ConvertTo-SecureString -String ([Guid]::NewGuid().ToString('N')) -Force -AsPlainText; $pfx = Join-Path $OutDir 'ci-test.pfx'
  Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null; Export-Certificate -Cert $cert -FilePath (Join-Path $OutDir 'ci-test.cer') | Out-Null
  & $signtool sign /fd SHA256 /a /f $pfx /p ([System.Net.NetworkCredential]::new('', $pwd).Password) $pkg | Out-Host; if ($LASTEXITCODE -ne 0) { throw "signtool falló ($LASTEXITCODE)" }
  Remove-Item $pfx -Force   # la clave privada no sale del script
}
Write-Host "MSIX ($Variant, firmado de prueba: $($Sign.IsPresent)): $pkg  [$([math]::Round((Get-Item $pkg).Length / 1MB, 1)) MB]"
