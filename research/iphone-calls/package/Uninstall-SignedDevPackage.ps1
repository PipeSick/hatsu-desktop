<# Deshace Install-SignedDevPackage.ps1: quita el paquete, y el certificado de TrustedPeople y de CurrentUser\My (administrador). #>
param([string]$Stage = "$env:TEMP\CallProbeSigned")
$ErrorActionPreference = 'Stop'
Get-AppxPackage WinMac.Research.CallProbe | Remove-AppxPackage
$tp = Join-Path $Stage 'thumbprint.txt'
$thumbs = @(); if (Test-Path $tp) { $thumbs += (Get-Content $tp).Trim() }
$thumbs += (Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=WinMacResearch').Thumbprint
foreach ($t in ($thumbs | Select-Object -Unique)) {
  Remove-Item "Cert:\LocalMachine\TrustedPeople\$t" -ErrorAction SilentlyContinue
  Remove-Item "Cert:\CurrentUser\My\$t" -ErrorAction SilentlyContinue
}
Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Paquete y certificado de investigación quitados.'
