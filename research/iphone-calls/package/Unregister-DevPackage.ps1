<# Deshace Register-DevPackage.ps1: quita SOLO el paquete de investigación «suelto» (sin firma). (Antes, si lo registraste: .\Run-Probe.ps1 stage3 unregister)
   Si el paquete instalado está FIRMADO (es el de P3), no lo toca: usa Uninstall-SignedDevPackage.ps1. #>
$pkgs = @(Get-AppxPackage WinMac.Research.CallProbe)
if ($pkgs.Count -eq 0) { Write-Host 'No hay paquete de investigación instalado.' }
else {
  foreach ($p in $pkgs) {
    if ("$($p.SignatureKind)" -ne 'None') { throw "El paquete $($p.PackageFullName) está firmado ($($p.SignatureKind)): no es el suelto. No se toca; usa Uninstall-SignedDevPackage.ps1." }
  }
  $pkgs | Remove-AppxPackage
  Remove-Item "$env:USERPROFILE\CallProbePkg" -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host 'Paquete de investigación (suelto) quitado.'
}
