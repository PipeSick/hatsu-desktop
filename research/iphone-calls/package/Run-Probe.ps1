<#
  Ejecuta CallProbe CON identidad de paquete (Invoke-CommandInDesktopPackage) o SIN ella (-Unpackaged), para comparar.
  Los informes se guardan en Documentos\CallProbe\ (una app empaquetada no siempre muestra consola).
  Ejemplos:
    .\Run-Probe.ps1 stage1                      # con paquete
    .\Run-Probe.ps1 stage2 -Unpackaged          # sin paquete (línea base)
    .\Run-Probe.ps1 stage3 status              # solo lectura
    .\Run-Probe.ps1 stage3 register            # pide escribir REGISTRAR; revierte solo al terminar
    .\Run-Probe.ps1 stage3 unregister          # recuperación si el proceso murió a medias
  Etapas 3 (registrar) y 5 (contestar/colgar) piden escribir una palabra exacta antes de actuar. Nada marca ni inicia llamadas.
#>
param([Parameter(Mandatory, Position = 0)][string]$Stage, [Parameter(Position = 1)][string]$Action = '', [switch]$Unpackaged, [string]$Device = '', [string]$ExtraArgs = '')
$ErrorActionPreference = 'Stop'
$exe = if ($Unpackaged) { Join-Path (Split-Path $PSScriptRoot) 'publish\CallProbe.exe' } else { "$env:USERPROFILE\CallProbePkg\CallProbe.exe" }
if (-not (Test-Path $exe)) { throw "No encuentro $exe. Publica antes: dotnet publish CallProbe -c Release -r win-x64 --self-contained -o publish (y, para la prueba empaquetada, Register-DevPackage.ps1)." }
$tag = if ($Unpackaged) { 'sin-paquete' } else { 'con-paquete' }
$out = Join-Path ([Environment]::GetFolderPath('MyDocuments')) "CallProbe\$Stage-$Action-$tag-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
$probeArgs = @($Stage) + $(if ($Action) { @($Action) } else { @() }) + $(if ($Device) { @('--device', $Device) } else { @() }) + @('--out', $out) + $(if ($ExtraArgs) { $ExtraArgs -split ' ' } else { @() })
# Las etapas interactivas (stage3 register/unregister, stage5) necesitan teclado: se lanzan dentro de cmd /k para que la consola siga abierta.
if ($Unpackaged) { & $exe @probeArgs }
else {
  $pkg = Get-AppxPackage WinMac.Research.CallProbe; if (-not $pkg) { throw 'El paquete no está registrado: ejecuta Register-DevPackage.ps1.' }
  $quoted = ($probeArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
  Invoke-CommandInDesktopPackage -PackageFamilyName $pkg.PackageFamilyName -AppId App -Command 'cmd.exe' -Args "/k `"`"$exe`" $quoted`"" -PreventBreakaway
  Start-Sleep -Seconds 1
}
Write-Host "Informe: $out"
