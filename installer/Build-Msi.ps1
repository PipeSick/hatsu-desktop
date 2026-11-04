<#
.SYNOPSIS
  Construye el MSI por usuario de Hatsu Desktop a partir de dist\WinMac (WiX 5.x). NO instala ni publica nada.
.DESCRIPTION
  UpgradeCode fijo (installer\Package.wxs). ProductCode determinista: GUID de nombre (SHA-1) de «UpgradeCode|versión», así dos compilaciones de la misma versión tienen el mismo ProductCode
  y cada versión nueva el suyo (requisito de MSI para las actualizaciones). Fechas de los archivos fijadas por Package-WinMac.ps1.
  Requiere:  dotnet tool install --global wix --version 5.0.2   y   wix extension add --global WixToolset.UI.wixext/5.0.2
.PARAMETER Version     major.minor.build (por defecto, la de src\Directory.Build.props)
.PARAMETER Compression none|low|medium|high (las pruebas usan none para ir rápido; la entrega, medium)
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'paquete', [string]$Version = '', [string]$Compression = 'medium')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..'); $dist = (Resolve-Path $DistDir).Path
if (-not (Test-Path (Join-Path $dist 'HatsuDesktop.exe'))) { throw "No está HatsuDesktop.exe en $dist (ejecuta antes tools\Build-Local.ps1 y tools\Package-WinMac.ps1)" }
if (-not $Version) { [xml]$props = Get-Content (Join-Path $root 'src\Directory.Build.props'); $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versión MSI inválida «$Version»: use major.minor.build" }
$upgradeCode = 'ae38cbf7-6543-463e-9067-50e88f162fa1'
function New-NameGuid([string]$name) {   # GUID de nombre (RFC 4122 v5) con espacio de nombres = UpgradeCode: determinista
  $ns = [Guid]::Parse($upgradeCode).ToByteArray(); [Array]::Reverse($ns, 0, 4); [Array]::Reverse($ns, 4, 2); [Array]::Reverse($ns, 6, 2)
  $h = [Security.Cryptography.SHA1]::Create().ComputeHash($ns + [Text.Encoding]::UTF8.GetBytes($name)); $g = $h[0..15]
  $g[6] = ($g[6] -band 0x0F) -bor 0x50; $g[8] = ($g[8] -band 0x3F) -bor 0x80
  [Array]::Reverse($g, 0, 4); [Array]::Reverse($g, 4, 2); [Array]::Reverse($g, 6, 2); (New-Object Guid (,[byte[]]$g)).ToString().ToUpperInvariant()
}
$productCode = New-NameGuid "HatsuDesktop|$Version"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$msi = Join-Path $OutDir "HatsuDesktop-$Version-win-x64.msi"; if (Test-Path $msi) { Remove-Item $msi }
$args = @('build', (Join-Path $PSScriptRoot 'Package.wxs'), '-arch', 'x64', '-culture', 'es-ES', '-ext', 'WixToolset.UI.wixext', '-o', $msi,
  '-d', "Version=$Version", '-d', "ProductCode={$productCode}", '-d', "DistDir=$dist", '-d', "IconFile=$(Join-Path $root 'assets\brand\HatsuDesktop.ico')", '-d', "LicenseRtf=$(Join-Path $PSScriptRoot 'License.rtf')", '-d', "Compression=$Compression")   # sin validación ICE en la compilación: ICE38/64/91 (archivos en el perfil del usuario) no aplican a una instalación POR USUARIO
& wix @args; if ($LASTEXITCODE -ne 0) { throw "wix build falló ($LASTEXITCODE)" }
"$productCode" | Set-Content "$msi.productcode" -Encoding ASCII
$h = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant(); "$h  $(Split-Path $msi -Leaf)" | Set-Content "$msi.sha256" -Encoding ASCII
Write-Host "MSI: $msi ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)  versión $Version  ProductCode {$productCode}  UpgradeCode {$upgradeCode}"; Write-Host "SHA-256: $h"
