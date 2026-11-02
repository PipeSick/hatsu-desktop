<#
.SYNOPSIS
  Empaqueta dist\WinMac en un .zip reproducible (docs/32). NO publica nada: el zip queda en la carpeta indicada.
.DESCRIPTION
  Añade VERSION.txt (versión + commit), licencias (LICENSE, NOTICE, THIRD_PARTY.md), el desinstalador y SHA256SUMS.txt; fija las fechas de todos los
  archivos a la del commit para que dos empaquetados del mismo commit den el mismo zip. Sin certificados comerciales: el paquete no va firmado y Windows
  SmartScreen puede avisar la primera vez (no se desactiva ninguna protección).
#>
param([string]$DistDir = 'dist\WinMac', [string]$OutDir = 'paquete')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not (Test-Path (Join-Path $DistDir 'WinMac.exe'))) { throw "No está WinMac.exe en $DistDir (ejecuta antes tools\Build-Local.ps1)" }
[xml]$props = Get-Content (Join-Path $root 'src\Directory.Build.props'); $version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$sha = (git -C $root rev-parse --short HEAD).Trim(); $when = [DateTime]::Parse((git -C $root log -1 --format=%cI).Trim()).ToUniversalTime()
"WinMac $version (commit $sha, $($when.ToString('yyyy-MM-dd')))`nSoftware libre (ver LICENSE). Compilación no firmada: no es una versión oficial publicada." | Set-Content (Join-Path $DistDir 'VERSION.txt') -Encoding UTF8
foreach ($f in 'LICENSE', 'NOTICE', 'THIRD_PARTY.md') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $DistDir -Force } }
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-WinMac.ps1') $DistDir -Force
Get-ChildItem $DistDir -Recurse -File | ForEach-Object { $_.LastWriteTimeUtc = $when }
$sums = Get-ChildItem $DistDir -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
  '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring((Resolve-Path $DistDir).Path.Length + 1).Replace('\', '/')
}
$sums | Set-Content (Join-Path $DistDir 'SHA256SUMS.txt') -Encoding UTF8; (Get-Item (Join-Path $DistDir 'SHA256SUMS.txt')).LastWriteTimeUtc = $when
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zip = Join-Path $OutDir "WinMac-$version-$sha-win-x64.zip"; if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$z = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
  foreach ($f in (Get-ChildItem $DistDir -Recurse -File | Sort-Object FullName)) {
    $rel = 'WinMac/' + $f.FullName.Substring((Resolve-Path $DistDir).Path.Length + 1).Replace('\', '/')
    $e = $z.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal); $e.LastWriteTime = [DateTimeOffset]$when   # la fecha antes de escribir (luego no se puede)
    $out = $e.Open(); $in = [IO.File]::OpenRead($f.FullName); try { $in.CopyTo($out) } finally { $in.Dispose(); $out.Dispose() }
  }
} finally { $z.Dispose() }
$h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$h  $(Split-Path $zip -Leaf)" | Set-Content "$zip.sha256" -Encoding ASCII
Write-Host "Paquete: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"; Write-Host "SHA-256: $h"
