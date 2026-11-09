<#
.SYNOPSIS
  Superpone los binarios FIRMADOS (devueltos por SignPath) sobre la carpeta de publicación sin firmar, comprobando antes que cada archivo firmado corresponde a uno existente y que su firma es válida.
.DESCRIPTION
  No firma nada ni contacta con ningún servicio. Falla si: falta un archivo esperado, sobra uno que no existía en la publicación, o una firma no es «Valid» con sello de tiempo.
#>
param([Parameter(Mandatory)][string]$DistDir, [Parameter(Mandatory)][string]$SignedDir)
$ErrorActionPreference = 'Stop'; $dist = (Resolve-Path $DistDir).Path; $signed = (Resolve-Path $SignedDir).Path
$files = @(Get-ChildItem $signed -Recurse -File); if ($files.Count -eq 0) { throw "No hay archivos firmados en $signed" }
$bad = @(); foreach ($f in $files) {
  $rel = $f.FullName.Substring($signed.Length + 1); $target = Join-Path $dist $rel
  if (-not (Test-Path $target)) { $bad += "sobra (no existe en la publicación): $rel"; continue }
  $s = Get-AuthenticodeSignature -FilePath $f.FullName
  if ($s.Status -ne 'Valid' -or $null -eq $s.TimeStamperCertificate) { $bad += "firma no válida o sin sello de tiempo ($($s.Status)): $rel"; continue }
}
if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "::error::$_" }; throw "$($bad.Count) problema(s): no se superpone nada" }
foreach ($f in $files) { Copy-Item $f.FullName (Join-Path $dist ($f.FullName.Substring($signed.Length + 1))) -Force }
Write-Host ("{0} binarios firmados superpuestos en {1}" -f $files.Count, $dist)
