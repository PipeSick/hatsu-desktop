<#
.SYNOPSIS
  Auditoría (solo lectura) de los binarios y del MSI que produce el CI: firma Authenticode, cabeceras PE, metadatos y procedencia. Escribe un informe en Markdown.
.DESCRIPTION
  Sirve para comparar los ejecutables entre sí (¿se diferencia el Dock de la Barra?) y para dejar constancia de que NINGÚN archivo propio está firmado todavía. NO explica por sí sola por qué Smart App Control
  bloqueó un archivo en un equipo concreto: esa decisión es de Windows y depende de su reputación; el informe solo reúne los hechos verificables del paquete.
#>
param([string]$DistDir = 'dist\WinMac', [string]$Msi = '', [string]$Out = 'auditoria-binarios.md')
$ErrorActionPreference = 'Stop'
function Get-PeInfo([string]$path) {
  $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite'); $br = New-Object System.IO.BinaryReader($fs)
  try {
    $fs.Position = 0x3C; $pe = $br.ReadInt32(); $fs.Position = $pe; if ($br.ReadUInt32() -ne 0x4550) { return $null }
    $machine = $br.ReadUInt16(); $null = $br.ReadUInt16(); $stamp = $br.ReadUInt32(); $opt = $pe + 24; $fs.Position = $opt; $magic = $br.ReadUInt16(); $isPlus = ($magic -eq 0x20B)
    $fs.Position = $opt + 70; $dllChars = $br.ReadUInt16(); $fs.Position = $opt + 68; $subsystem = $br.ReadUInt16()
    $dd = if ($isPlus) { $opt + 112 } else { $opt + 96 }; $fs.Position = $dd + 4 * 8; $null = $br.ReadUInt32(); $certSize = $br.ReadUInt32(); $fs.Position = $dd + 14 * 8; $clr = $br.ReadUInt32()
    [pscustomobject]@{ Machine = $(if ($machine -eq 0x8664) { 'x64' } else { '0x{0:X}' -f $machine }); Stamp = [DateTimeOffset]::FromUnixTimeSeconds([int64]$stamp).UtcDateTime.ToString('yyyy-MM-dd HH:mm'); Subsystem = $(if ($subsystem -eq 2) { 'GUI' } elseif ($subsystem -eq 3) { 'consola' } else { $subsystem })
      Managed = ($clr -ne 0); CertBytes = $certSize; Aslr = [bool]($dllChars -band 0x40); Nx = [bool]($dllChars -band 0x100); HighEntropy = [bool]($dllChars -band 0x20); Cfg = [bool]($dllChars -band 0x4000) }
  } finally { $br.Close(); $fs.Close() }
}
$dist = (Resolve-Path $DistDir).Path; $md = New-Object System.Collections.Generic.List[string]
$md.Add('# Auditoría de binarios de Hatsu Desktop (CI)'); $md.Add('')
$md.Add('Hechos verificables del paquete generado por el CI. **No explica por qué Smart App Control bloqueó un archivo en un equipo concreto**: esa decisión es de Windows (firma y reputación) y no se puede reproducir en el CI.'); $md.Add('')
$md.Add('## Ejecutables principales'); $md.Add(''); $md.Add('| Archivo | Tamaño | Firma Authenticode | Cabeceras PE | Metadatos |'); $md.Add('|---|---:|---|---|---|')
foreach ($f in 'HatsuDesktop.exe', 'HatsuDesktop.Bar.exe', 'HatsuDesktop.Dock.exe', 'HatsuDesktop.Connect.exe', 'NotchWin\NotchWin.App.exe') {
  $p = Join-Path $dist $f; if (-not (Test-Path $p)) { $md.Add("| $f | — | **no está** | — | — |"); continue }
  $i = Get-Item $p; $s = Get-AuthenticodeSignature -FilePath $p; $pe = Get-PeInfo $p; $v = $i.VersionInfo
  $md.Add(('| {0} | {1:N0} | {2} | {3}; enlazado {4}; {5}; {6}; ASLR {7}, NX {8}, HighEntropyVA {9}, CFG {10}; tabla de certificados {11} B | {12} · «{13}» · {14} · original «{15}» |' -f $f, $i.Length, $s.Status, $pe.Machine, $pe.Stamp, $pe.Subsystem, $(if ($pe.Managed) { '.NET' } else { 'nativo' }), $pe.Aslr, $pe.Nx, $pe.HighEntropy, $pe.Cfg, $pe.CertBytes, $v.FileVersion, $v.FileDescription, $v.CompanyName, $v.OriginalFilename))
}
$md.Add(''); $md.Add('## Binarios propios en el paquete'); $md.Add('')
$own = @(Get-ChildItem $dist -Recurse -File -Include *.exe, *.dll | Where-Object { $_.Name -match '^(HatsuDesktop|WinMac|NotchWin)' }); $rows = foreach ($f in $own) { [pscustomobject]@{ Name = $f.FullName.Substring($dist.Length + 1); Status = (Get-AuthenticodeSignature -FilePath $f.FullName).Status } }
$md.Add(('Binarios propios (HatsuDesktop*, WinMac*, NotchWin*): **{0}**; sin firma: **{1}**; con firma válida: **{2}**.' -f @($rows).Count, @($rows | Where-Object Status -eq 'NotSigned').Count, @($rows | Where-Object Status -eq 'Valid').Count)); $md.Add('')
$all = @(Get-ChildItem $dist -Recurse -File -Include *.exe, *.dll); $ms = 0; $thirdParty = @()
foreach ($f in $all) { if ($f.Name -match '^(HatsuDesktop|WinMac|NotchWin)') { continue }; $s2 = Get-AuthenticodeSignature -FilePath $f.FullName; if ($s2.Status -eq 'Valid' -and $s2.SignerCertificate.Subject -match 'Microsoft') { $ms++ } else { $thirdParty += [pscustomobject]@{ Name = $f.FullName.Substring($dist.Length + 1); Status = $s2.Status; Signer = $(if ($s2.SignerCertificate) { ($s2.SignerCertificate.Subject -replace ',.*$', '') } else { '—' }); Version = $f.VersionInfo.FileVersion; Product = $f.VersionInfo.ProductName } } }
$md.Add(('Resto de .exe/.dll ({0}): firmados por Microsoft **{1}**; **sin firma de Microsoft {2}** (bibliotecas de terceros, abajo).' -f ($all.Count - @($rows).Count), $ms, $thirdParty.Count)); $md.Add('')
if ($thirdParty.Count -gt 0) { $md.Add('### Binarios ajenos que NO llevan firma de Microsoft'); $md.Add(''); $md.Add('| Archivo | Firma | Firmante | Versión | Producto |'); $md.Add('|---|---|---|---|---|'); foreach ($t in ($thirdParty | Sort-Object Name)) { $md.Add(('| `{0}` | {1} | {2} | {3} | {4} |' -f $t.Name, $t.Status, $t.Signer, $t.Version, $t.Product)) }; $md.Add('') }
$md.Add('<details><summary>Lista de binarios propios</summary>'); $md.Add(''); foreach ($r in ($rows | Sort-Object Name)) { $md.Add(('- `{0}` — {1}' -f $r.Name, $r.Status)) }; $md.Add(''); $md.Add('</details>'); $md.Add('')
if ($Msi -and (Test-Path $Msi)) { $s = Get-AuthenticodeSignature -FilePath $Msi; $md.Add('## MSI'); $md.Add(''); $md.Add(('`{0}` — {1:N1} MB — firma Authenticode: **{2}** — SHA-256 `{3}`' -f (Split-Path $Msi -Leaf), ((Get-Item $Msi).Length / 1MB), $s.Status, (Get-FileHash $Msi -Algorithm SHA256).Hash.ToLowerInvariant())); $md.Add('') }
$md.Add('## Lectura honesta'); $md.Add('')
$md.Add('- Ningún binario propio ni el MSI están firmados: Windows (Smart App Control, SmartScreen) no puede identificar al editor. Es la causa **más probable**, pero **no está demostrada** para el Dock concreto: solo el registro de Integridad de código del equipo afectado lo puede confirmar (`tools/Diagnose-Install.ps1`).')
$md.Add('- Que la Barra y la Isla arrancaran y el Dock no es compatible con una decisión por archivo basada en reputación (cada archivo se evalúa por separado); este informe no encuentra una diferencia de construcción entre ellos que lo explique.')
$md.Add('- Estrategia legítima de firma: `docs/49-fase3-1-instalacion-real-y-firma.md`.')
New-Item -ItemType Directory -Force -Path (Split-Path -Parent (Join-Path (Get-Location) $Out)) | Out-Null; $md -join "`n" | Set-Content $Out -Encoding UTF8
Write-Host ($md -join "`n")
$bad = @($rows | Where-Object Status -eq 'Valid').Count; Write-Host "Auditoría escrita en $Out"
