<#
.SYNOPSIS
  Construye WinMac (host + Barra superior + Dock) en UNA carpeta para probarlo en tu PC: dist\WinMac\WinMac.exe.
.DESCRIPTION
  Requiere el SDK de .NET 10 (el mismo que ya usaste para el Dock). No instala nada, no toca el registro, no necesita administrador.
  Publica framework-dependiente (usa el runtime de .NET 10 instalado). Los tres ejecutables comparten carpeta: WinMac.exe los encuentra por su nombre.
  Uso:   pwsh tools\Build-Local.ps1            (o  powershell -File tools\Build-Local.ps1)
         dist\WinMac\WinMac.exe                inicia el host con la Barra superior y el Dock
         dist\WinMac\WinMac.exe --quit         lo cierra (cierra también sus módulos)
  La Isla (NotchWin) se incluye si hay un clon suyo en -NotchPath (por defecto C:\nw, ruta CORTA: el compilador XAML falla en silencio con rutas largas) con el submódulo
  external\winmac inicializado (git submodule update --init). Sin él, WinMac funciona igual y la Isla aparece como «no instalada».
#>
param([string]$Out = 'dist\WinMac', [string]$Configuration = 'Release', [string]$NotchPath = 'C:\nw')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..'); Set-Location $root
$dest = Join-Path $root $Out
if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($p in @('src/WinMac.Host/WinMac.Host.csproj', 'src/WinMac.Bar.App/WinMac.Bar.App.csproj', 'src/WinMac.Dock.App/WinMac.Dock.App.csproj')) {
  Write-Host "Publicando $p …"
  & dotnet publish $p -c $Configuration -r win-x64 --self-contained false -o $dest
  if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de $p" }
}
$notchProj = Join-Path $NotchPath 'src\NotchWin.App\NotchWin.App.csproj'
if (Test-Path $notchProj) {
  Write-Host "Publicando NotchWin ($NotchPath) …"
  & dotnet publish $notchProj -c $Configuration -p:Platform=x64 -r win-x64 --self-contained true -o (Join-Path $dest 'NotchWin')
  if ($LASTEXITCODE -ne 0) { throw "Falló la publicación de NotchWin" }
} else { Write-Host "NotchWin no incluido (no hay clon en $NotchPath): WinMac funciona sin la Isla." -ForegroundColor Yellow }
$exes = 'WinMac.exe', 'WinMac.Bar.App.exe', 'WinMac.Dock.App.exe'
$missing = $exes | Where-Object { -not (Test-Path (Join-Path $dest $_)) }
if ($missing) { throw "Faltan en $dest : $($missing -join ', ')" }
Write-Host ""
Write-Host "Listo: $dest" -ForegroundColor Green
Write-Host "  Iniciar:  $dest\WinMac.exe"
Write-Host "  Cerrar:   $dest\WinMac.exe --quit"
Write-Host "  Registros: %LOCALAPPDATA%\WinMac\logs\host.log (host), bar.log, dock.log"
