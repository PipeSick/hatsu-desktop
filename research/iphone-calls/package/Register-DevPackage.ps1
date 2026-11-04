<#
  Da identidad de paquete a CallProbe SIN certificados: registro «suelto» en modo desarrollador (Add-AppxPackage -Register).
  - Requiere el MODO DESARROLLADOR de Windows (Configuración › Sistema › Para programadores). Es un ajuste del usuario, reversible; este script NO lo cambia: si no está activo, se detiene y lo dice.
  - NO toca certificados, Smart App Control, Defender, directivas ni archivos del sistema.
  - Se deshace con Unregister-DevPackage.ps1.
  Uso:  .\Register-DevPackage.ps1 -BuildDir <carpeta con CallProbe.exe publicado>
#>
param([Parameter(Mandatory)][string]$BuildDir, [string]$Target = "$env:USERPROFILE\CallProbePkg")
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $BuildDir 'CallProbe.exe'))) { throw "No encuentro CallProbe.exe en $BuildDir (publica con: dotnet publish CallProbe -c Release -r win-x64 --self-contained -o <carpeta>)" }
$dev = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($dev -ne 1) { throw "El modo desarrollador no está activo. Actívalo en Configuración › Sistema › Para programadores (es reversible) y vuelve a ejecutar; este script no lo cambia." }
New-Item -ItemType Directory -Force -Path $Target, "$Target\Assets" | Out-Null
Copy-Item "$BuildDir\*" $Target -Recurse -Force
Copy-Item "$PSScriptRoot\AppxManifest.xml" "$Target\AppxManifest.xml" -Force
# Logotipos mínimos (el manifiesto los exige). Se generan aquí; no hay recursos binarios en el repositorio.
Add-Type -AssemblyName System.Drawing
foreach ($spec in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
  $bmp = New-Object System.Drawing.Bitmap $spec[1], $spec[1]; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 40, 40, 48)); $g.Dispose()
  $bmp.Save("$Target\Assets\$($spec[0])", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
Add-AppxPackage -Register "$Target\AppxManifest.xml"
$pkg = Get-AppxPackage WinMac.Research.CallProbe
Write-Host "Registrado: $($pkg.PackageFullName)"
Write-Host "Ejecuta con identidad:  .\Run-Probe.ps1 stage2"
