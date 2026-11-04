<#
.SYNOPSIS
  Genera la «configuración de artefacto» de SignPath a partir del CONTENIDO REAL de una carpeta de publicación, para que la lista de archivos a firmar nunca quede desactualizada.
.DESCRIPTION
  -Mode Binaries : un <zip-file> con un <pe-file> por cada binario PROPIO (HatsuDesktop*, WinMac*) y NINGUNO ajeno. NotchWin se firma en SU repositorio y las bibliotecas de terceros no se firman aquí
                   (SignPath Foundation firma solo binarios propios del proyecto).
  -Mode Installer: un <msi-file> raíz para firmar el MSI ya construido con los binarios firmados.
  NO contacta con ningún servicio: solo escribe un XML. Sirve para revisarlo antes de pedir nada y se validará con el editor de SignPath cuando exista el proyecto aprobado.
#>
param([Parameter(Mandatory)][ValidateSet('Binaries', 'Installer')][string]$Mode, [string]$DistDir = 'dist\WinMac', [string]$Out = 'signpath-artifact-configuration.xml', [string]$ProductName = 'Hatsu Desktop')
$ErrorActionPreference = 'Stop'
$ns = 'http://signpath.io/artifact-configuration/v1'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine("<!-- Generado por tools/signing/New-ArtifactConfiguration.ps1 ($Mode). No se ha enviado a ningún servicio. -->")
if ($Mode -eq 'Binaries') {
  $root = (Resolve-Path $DistDir).Path
  $own = @(Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' -and $_.Name -match '^(HatsuDesktop|WinMac)' } | Sort-Object FullName)
  if ($own.Count -eq 0) { throw "No hay binarios propios (HatsuDesktop*, WinMac*) en $root" }
  [void]$sb.AppendLine("<artifact-configuration xmlns=`"$ns`">"); [void]$sb.AppendLine('  <zip-file>')
  foreach ($f in $own) { $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/'); [void]$sb.AppendLine("    <pe-file path=`"$rel`">"); [void]$sb.AppendLine('      <authenticode-sign />'); [void]$sb.AppendLine('    </pe-file>') }
  [void]$sb.AppendLine('  </zip-file>'); [void]$sb.AppendLine('</artifact-configuration>')
  Write-Host ("{0} binarios propios a firmar; excluidos (no se firman aquí): NotchWin y bibliotecas de terceros." -f $own.Count)
} else {
  [void]$sb.AppendLine("<artifact-configuration xmlns=`"$ns`">"); [void]$sb.AppendLine('  <msi-file>'); [void]$sb.AppendLine('    <authenticode-sign />'); [void]$sb.AppendLine('  </msi-file>'); [void]$sb.AppendLine('</artifact-configuration>')
}
$xml = $sb.ToString(); [void][xml]$xml   # comprueba que es XML bien formado
$target = [System.IO.Path]::GetFullPath($Out, (Get-Location).Path)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
[System.IO.File]::WriteAllText($target, $xml, (New-Object System.Text.UTF8Encoding($false))); Write-Host "Escrito: $target"
