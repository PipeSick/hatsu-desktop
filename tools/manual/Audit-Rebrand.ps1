<#
.SYNOPSIS
  Auditoría de SOLO LECTURA de las referencias al nombre antiguo («WinMac») en el repositorio (docs/46). No modifica nada.
.DESCRIPTION
  Cuenta las apariciones por zona (src, docs, tools, .github, raíz) y lista los textos con prosa que AÚN dicen «WinMac» dentro de cadenas de C# (candidatos a identidad pública pendiente).
  Las rutas, los nombres de archivo, los espacios de nombres y los identificadores internos no cuentan como pendientes.
#>
param([string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'
$files = Get-ChildItem -Path $Root -Recurse -File -Include *.cs, *.csproj, *.props, *.ps1, *.yml, *.md, *.json, *.txt, *.xaml, *.manifest |
  Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|external|ci-report)\\' }
$zones = [ordered]@{ 'src' = 0; 'docs' = 0; 'tools' = 0; '.github' = 0; 'raíz' = 0 }
foreach ($f in $files) {
  $n = ([regex]::Matches((Get-Content $f.FullName -Raw), 'WinMac')).Count; if ($n -eq 0) { continue }
  $rel = $f.FullName.Substring($Root.Length).TrimStart('\', '/')
  $z = if ($rel -match '^src[\\/]') { 'src' } elseif ($rel -match '^docs[\\/]') { 'docs' } elseif ($rel -match '^tools[\\/]') { 'tools' } elseif ($rel -match '^\.github') { '.github' } else { 'raíz' }
  $zones[$z] += $n
}
Write-Host 'Apariciones de «WinMac» por zona:'; $zones.GetEnumerator() | ForEach-Object { Write-Host ('  {0,-8} {1}' -f $_.Key, $_.Value) }
Write-Host ''; Write-Host 'Textos con prosa que aún dicen «WinMac» en cadenas de C# (excluye pruebas):'
$pending = 0
foreach ($f in ($files | Where-Object { $_.Extension -eq '.cs' -and $_.FullName -notmatch '\.Tests' })) {
  $i = 0
  foreach ($line in (Get-Content $f.FullName)) {
    $i++; if ($line -match '^\s*//') { continue }
    foreach ($m in [regex]::Matches($line, '"[^"\r\n]*\bWinMac\b(?![.\\/A-Za-z0-9_])[^"\r\n]*\s[^"\r\n]*"')) {
      $pending++; Write-Host ('  {0}:{1}  {2}' -f $f.FullName.Substring($Root.Length).TrimStart('\', '/'), $i, $m.Value.Substring(0, [Math]::Min(110, $m.Value.Length)))
    }
  }
}
Write-Host ''; Write-Host "Pendientes de identidad pública: $pending"
