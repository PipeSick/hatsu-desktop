<#
.SYNOPSIS
  Verifica la firma Authenticode de los binarios PROPIOS y del MSI. Informativo por defecto; con -Require falla si alguno no tiene una firma VÁLIDA (cadena de confianza completa) y con sello de tiempo.
.DESCRIPTION
  Hoy ningún binario propio está firmado, así que el CI lo ejecuta sin -Require y solo informa. Cuando exista un certificado de firma de una autoridad reconocida, se pasa a -Require: una compilación con
  un binario propio sin firma válida deja de ser un candidato a entrega (Smart App Control, política VerifiedAndReputableDesktop, bloquea la carga de archivos sin reputación ni firma válida; ver docs/50).
  «Valid» significa que Windows verificó la cadena hasta una raíz que ya confía; una firma con un certificado autofirmado o de una raíz privada NO sale «Valid» en un equipo normal.
#>
param([string]$DistDir = 'dist\WinMac', [string]$Msi = '', [switch]$Require)
$ErrorActionPreference = 'Stop'; $dist = (Resolve-Path $DistDir).Path
$own = @(Get-ChildItem $dist -Recurse -File -Include *.exe, *.dll | Where-Object { $_.Name -match '^(HatsuDesktop|WinMac|NotchWin)' })
if ($Msi -and (Test-Path $Msi)) { $own += Get-Item $Msi }
$bad = @(); foreach ($f in $own) { $s = Get-AuthenticodeSignature -FilePath $f.FullName; $ok = ($s.Status -eq 'Valid') -and ($null -ne $s.TimeStamperCertificate)
  if (-not $ok) { $bad += [pscustomobject]@{ Archivo = $f.Name; Estado = $s.Status; SelloDeTiempo = ($null -ne $s.TimeStamperCertificate) } } }
Write-Host ("Binarios propios y MSI revisados: {0} · con firma válida y sello de tiempo: {1} · sin ella: {2}" -f $own.Count, ($own.Count - $bad.Count), $bad.Count)
if ($bad.Count -gt 0) { $bad | Select-Object -First 12 | Format-Table -AutoSize | Out-String | Write-Host; if ($bad.Count -gt 12) { Write-Host ("… y {0} más" -f ($bad.Count - 12)) } }
if ($Require -and $bad.Count -gt 0) { Write-Host "::error title=Firma::$($bad.Count) archivo(s) propio(s) sin firma válida: no es un candidato a entrega"; exit 1 }
if (-not $Require -and $bad.Count -gt 0) { Write-Host '::notice title=Firma::Informativo: los binarios propios y el MSI aún no están firmados (docs/50). Pasar a -Require cuando exista certificado.' }
