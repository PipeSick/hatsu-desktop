<# Deshace Register-DevPackage.ps1: quita el paquete de investigación. (Antes, si lo registraste: .\Run-Probe.ps1 stage3 unregister) #>
Get-AppxPackage WinMac.Research.CallProbe | Remove-AppxPackage
Remove-Item "$env:USERPROFILE\CallProbePkg" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Paquete de investigación quitado.'
