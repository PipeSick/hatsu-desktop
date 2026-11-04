<#
  PRUEBA DE HIPÓTESIS H1 (NO EJECUTADA POR LA INVESTIGACIÓN): ¿el registro tiene efecto si el paquete está FIRMADO (como Klangbruecke/MyPhone) en vez de registrado «suelto» (firma None)?
  QUÉ CAMBIA EN TU PC (todo reversible con Uninstall-SignedDevPackage.ps1):
    1. Crea un certificado autofirmado «CN=WinMacResearch» en CurrentUser\My (solo para firmar este paquete).
    2. Lo AÑADE a LocalMachine\TrustedPeople (requiere consola de administrador). Es un cambio en el almacén de confianza: cualquier paquete firmado con ese certificado pasará a ser instalable en este PC. La clave privada se queda en tu perfil; el .cer exportado no la contiene.
    3. Instala el paquete MSIX firmado (misma identidad que el paquete suelto: quita ese antes con Unregister-DevPackage.ps1).
  NO toca: Smart App Control, Defender, directivas, archivos del sistema, el emparejamiento ni ajustes de Bluetooth.
  Requiere: Windows SDK (makeappx.exe y signtool.exe).
  Uso (administrador):  .\Install-SignedDevPackage.ps1 -BuildDir <carpeta publicada con CallProbe.exe> [-Stage "$env:TEMP\CallProbeSigned"]
#>
param([Parameter(Mandatory)][string]$BuildDir, [string]$Stage = "$env:TEMP\CallProbeSigned")
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta esta consola como administrador (hace falta para el almacén de confianza).' }
if (Get-AppxPackage WinMac.Research.CallProbe) { throw 'Ya hay un paquete WinMac.Research.CallProbe instalado (¿el suelto?). Quítalo antes con Unregister-DevPackage.ps1.' }
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory | Where-Object Name -match '^10\.' | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { (Test-Path "$_\makeappx.exe") -and (Test-Path "$_\signtool.exe") } | Select-Object -First 1
if (-not $sdk) { throw 'No encuentro makeappx.exe/signtool.exe (Windows SDK).' }
$content = Join-Path $Stage 'content'; Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$content\Assets" | Out-Null
Copy-Item "$BuildDir\*" $content -Recurse -Force; Copy-Item "$PSScriptRoot\AppxManifest.xml" "$content\AppxManifest.xml"
Add-Type -AssemblyName System.Drawing
foreach ($spec in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
  $bmp = New-Object System.Drawing.Bitmap $spec[1], $spec[1]; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(255, 40, 40, 48)); $g.Dispose()
  $bmp.Save("$content\Assets\$($spec[0])", [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=WinMacResearch' -KeyUsage DigitalSignature -FriendlyName 'WinMac CallProbe (investigación)' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
$cer = Join-Path $Stage 'CallProbe.cer'; Export-Certificate -Cert $cert -FilePath $cer | Out-Null
$msix = Join-Path $Stage 'CallProbe.msix'
& "$sdk\makeappx.exe" pack /d $content /p $msix /o | Out-Null; if ($LASTEXITCODE) { throw 'makeappx falló' }
& "$sdk\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-Null; if ($LASTEXITCODE) { throw 'signtool falló' }
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
Add-AppxPackage -Path $msix
Set-Content -Path (Join-Path $Stage 'thumbprint.txt') -Value $cert.Thumbprint
Write-Host "Instalado paquete firmado. Huella del certificado: $($cert.Thumbprint)"
Write-Host 'Run-Probe.ps1 usa la ubicación de instalación del paquete automáticamente.'
