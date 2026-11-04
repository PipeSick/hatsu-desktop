<#
  P3 — INSTALA el paquete de investigación FIRMADO (autofirmado de desarrollo; NO equivale a una firma de Microsoft Store).
  Hipótesis que prueba (H1/C): ¿el registro de PhoneLineTransportDevice tiene efecto con un MSIX firmado y de confianza, en vez del paquete suelto sin firma?
  Requisitos: Windows PowerShell 5.1 (NO pwsh) como ADMINISTRADOR; Windows SDK (makeappx/signtool, x64); modo de carga lateral activo; ningún paquete WinMac.Research.CallProbe instalado.
  Si algo falla a mitad, el propio script deshace SOLO lo que creó (por huella exacta); si no puede, imprime la huella y cómo terminar con Uninstall-SignedDevPackage.ps1.
  Uso:  .\Install-SignedDevPackage.ps1 -BuildDir <carpeta publicada con CallProbe.exe>
#>
param([Parameter(Mandatory)][string]$BuildDir, [string]$Base = [Environment]::GetFolderPath('LocalApplicationData'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Usa Windows PowerShell 5.1 (powershell.exe), no pwsh: el módulo Appx no carga en pwsh.' }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta esta consola como administrador (hace falta para LocalMachine\TrustedPeople).' }
. (Join-Path $PSScriptRoot 'P3.Common.ps1')
$s = Invoke-P3Install -Base $Base -BuildDir (Resolve-Path $BuildDir).Path -ManifestPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Ops (New-P3RealOps)
Write-Host ''
Write-Host "P3 INSTALADO Y VERIFICADO (instalación ≠ resultado de la prueba)."
Write-Host "  Ejecución:   $($s.runId)"; Write-Host "  Huella:      $($s.thumbprint)"; Write-Host "  Paquete:     $($s.packageFullName) (firma: $($s.signatureKind))"
Write-Host '  Siguiente:   .\Run-Probe.ps1 stage3 status   y luego   .\Run-Probe.ps1 stage3 register   (ver INFORME §13)'
Write-Host '  Al terminar: .\Uninstall-SignedDevPackage.ps1'
