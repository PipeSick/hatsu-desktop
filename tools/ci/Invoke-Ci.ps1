<#
.SYNOPSIS
  Restaura, compila, prueba y publica (como artefactos) los componentes de WinMac y genera un informe claro.
  Pensado para GitHub Actions (windows-latest) pero ejecutable también en Linux/macOS con PowerShell 7 (compilación cruzada).
  NO publica versiones oficiales ni sube nada fuera del artefacto del workflow.
.NOTES
  «Compila» NO significa «funciona»: este script no valida comportamiento visual ni de ventanas.
#>
[CmdletBinding()]
param(
  [string]$Configuration = 'Release',
  [string]$ReportDir = 'ci-report',
  [string]$PublishDir = 'ci-publish',
  [switch]$NoPublish
)
$ErrorActionPreference = 'Continue'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $Root
New-Item -ItemType Directory -Force -Path $ReportDir, "$ReportDir/logs", "$ReportDir/trx" | Out-Null
$isWin = [Environment]::OSVersion.Platform -eq 'Win32NT'

# kind: app = se publica; lib = solo compila; test = se ejecuta
$components = @(
  @{ Name = 'Common';           Path = 'spikes/Common/Common.csproj';                     Kind = 'lib';  Extra = @() },
  @{ Name = 'Common.Tests';     Path = 'spikes/Common.Tests/Common.Tests.csproj';         Kind = 'test'; Extra = @() },
  @{ Name = 'S2.DockWpf';       Path = 'spikes/S2.DockWpf/S2.DockWpf.csproj';             Kind = 'app';  Extra = @() },
  @{ Name = 'S3.DockWinUI';     Path = 'spikes/S3.DockWinUI/S3.DockWinUI.csproj';         Kind = 'app';  Extra = @('-p:Platform=x64') },
  @{ Name = 'S4.DockDComp';     Path = 'spikes/S4.DockDComp/S4.DockDComp.csproj';         Kind = 'app';  Extra = @() },
  @{ Name = 'S1.WindowTracker'; Path = 'spikes/S1.WindowTracker/S1.WindowTracker.csproj'; Kind = 'app';  Extra = @() },
  @{ Name = 'S5.TaskbarGuard';  Path = 'spikes/S5.TaskbarGuard/S5.TaskbarGuard.csproj';   Kind = 'app';  Extra = @() },
  @{ Name = 'S6.UiaMenus';      Path = 'spikes/S6.UiaMenus/S6.UiaMenus.csproj';           Kind = 'app';  Extra = @() },
  @{ Name = 'S7.PlatformProbe'; Path = 'spikes/S7.PlatformProbe/S7.PlatformProbe.csproj'; Kind = 'app';  Extra = @() }
)
# Componentes del Dock (se añaden si existen en este commit)
foreach ($d in @(
  @{ Name = 'Apps.Core';       Path = 'src/WinMac.Apps.Core/WinMac.Apps.Core.csproj';             Kind = 'lib';  Extra = @() },
  @{ Name = 'Apps.Core.Tests'; Path = 'src/WinMac.Apps.Core.Tests/WinMac.Apps.Core.Tests.csproj'; Kind = 'test'; Extra = @() },
  @{ Name = 'Apps.Win';        Path = 'src/WinMac.Apps.Win/WinMac.Apps.Win.csproj';               Kind = 'lib';  Extra = @() },
  @{ Name = 'Dock.Core';       Path = 'src/WinMac.Dock.Core/WinMac.Dock.Core.csproj';             Kind = 'lib';  Extra = @() },
  @{ Name = 'Dock.Core.Tests'; Path = 'src/WinMac.Dock.Core.Tests/WinMac.Dock.Core.Tests.csproj'; Kind = 'test'; Extra = @() },
  @{ Name = 'Dock.Win';        Path = 'src/WinMac.Dock.Win/WinMac.Dock.Win.csproj';               Kind = 'lib';  Extra = @() },
  @{ Name = 'Dock.Win.Tests';  Path = 'src/WinMac.Dock.Win.Tests/WinMac.Dock.Win.Tests.csproj';   Kind = 'test'; Extra = @() },
  @{ Name = 'Dock.App';        Path = 'src/WinMac.Dock.App/WinMac.Dock.App.csproj';               Kind = 'app';  Extra = @() },
  @{ Name = 'System.Core';     Path = 'src/WinMac.System.Core/WinMac.System.Core.csproj';         Kind = 'lib';  Extra = @() },
  @{ Name = 'System.Core.Tests'; Path = 'src/WinMac.System.Core.Tests/WinMac.System.Core.Tests.csproj'; Kind = 'test'; Extra = @() },
  @{ Name = 'System.Win';      Path = 'src/WinMac.System.Win/WinMac.System.Win.csproj';           Kind = 'lib';  Extra = @() },
  @{ Name = 'Bar.Core';        Path = 'src/WinMac.Bar.Core/WinMac.Bar.Core.csproj';               Kind = 'lib';  Extra = @() },
  @{ Name = 'Bar.Core.Tests';  Path = 'src/WinMac.Bar.Core.Tests/WinMac.Bar.Core.Tests.csproj';   Kind = 'test'; Extra = @() },
  @{ Name = 'Bar.Win';         Path = 'src/WinMac.Bar.Win/WinMac.Bar.Win.csproj';                 Kind = 'lib';  Extra = @() },
  @{ Name = 'Bar.App';         Path = 'src/WinMac.Bar.App/WinMac.Bar.App.csproj';                 Kind = 'app';  Extra = @() },
  @{ Name = 'Ipc';             Path = 'src/WinMac.Ipc/WinMac.Ipc.csproj';                         Kind = 'lib';  Extra = @() },
  @{ Name = 'Ipc.Tests';       Path = 'src/WinMac.Ipc.Tests/WinMac.Ipc.Tests.csproj';             Kind = 'test'; Extra = @() },
  @{ Name = 'Host.Core';       Path = 'src/WinMac.Host.Core/WinMac.Host.Core.csproj';             Kind = 'lib';  Extra = @() },
  @{ Name = 'Host.Core.Tests'; Path = 'src/WinMac.Host.Core.Tests/WinMac.Host.Core.Tests.csproj'; Kind = 'test'; Extra = @() },
  @{ Name = 'FakeModule';      Path = 'src/WinMac.FakeModule/WinMac.FakeModule.csproj';           Kind = 'app';  Extra = @() },
  @{ Name = 'Host';            Path = 'src/WinMac.Host/WinMac.Host.csproj';                       Kind = 'app';  Extra = @() })) {
  if (Test-Path $d.Path) { $components += $d }
}

function Count-Match($file, $pattern) { if (Test-Path $file) { (Select-String -Path $file -Pattern $pattern -AllMatches | ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum } else { 0 } }

$results = @()
foreach ($c in $components) {
  $n = $c.Name; $log = "$ReportDir/logs/$n"
  $r = [ordered]@{ Name = $n; Kind = $c.Kind; Restore = 'omitido'; Build = 'omitido'; Test = '-'; Publish = '-'; Warnings = 0; Errors = 0; Seconds = 0; Detail = '' }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  Write-Host "::group::$n"
  & dotnet restore $c.Path @($c.Extra) *> "$log-restore.log"
  $r.Restore = if ($LASTEXITCODE -eq 0) { 'OK' } else { 'FALLO' }
  if ($r.Restore -eq 'OK') {
    & dotnet build $c.Path -c $Configuration --no-restore "-bl:$log.binlog" @($c.Extra) *> "$log-build.log"
    $r.Build = if ($LASTEXITCODE -eq 0) { 'OK' } else { 'FALLO' }
    $r.Warnings = Count-Match "$log-build.log" ': warning '
    $r.Errors = Count-Match "$log-build.log" ': error '
    if ($r.Build -eq 'FALLO') { $r.Detail = (Select-String -Path "$log-build.log" -Pattern ': error ' | Select-Object -First 3 | ForEach-Object { $_.Line.Trim() }) -join ' || ' }
  }
  if ($c.Kind -eq 'test' -and $r.Build -eq 'OK') {
    & dotnet test $c.Path -c $Configuration --no-build --logger "trx;LogFileName=$n.trx" --results-directory "$ReportDir/trx" *> "$log-test.log"
    $r.Test = if ($LASTEXITCODE -eq 0) { 'OK' } else { 'FALLO' }
    $line = Select-String -Path "$log-test.log" -Pattern '(Passed!|Failed!)' | Select-Object -Last 1
    if ($line) { $r.Detail = $line.Line.Trim(); Write-Host "  $($r.Detail)" }
  }
  if ($c.Kind -eq 'app' -and $r.Build -eq 'OK' -and -not $NoPublish -and ($isWin -or $n -ne 'S3.DockWinUI')) {
    $sc = if ($n -eq 'S3.DockWinUI') { 'true' } else { 'false' }
    $rid = if ($isWin) { @('-r', 'win-x64', "--self-contained", $sc) } else { @() }
    & dotnet publish $c.Path -c $Configuration -o "$PublishDir/$n" @($c.Extra) @($rid) *> "$log-publish.log"
    $r.Publish = if ($LASTEXITCODE -eq 0) { 'OK' } else { 'FALLO' }
    if ($r.Publish -eq 'FALLO' -and -not $r.Detail) { $r.Detail = (Select-String -Path "$log-publish.log" -Pattern ': error ' | Select-Object -First 2 | ForEach-Object { $_.Line.Trim() }) -join ' || ' }
  }
  Write-Host "::endgroup::"
  $sw.Stop(); $r.Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
  $results += [pscustomobject]$r
  $ok = ($r.Restore -eq 'OK') -and ($r.Build -eq 'OK') -and ($r.Test -ne 'FALLO') -and ($r.Publish -ne 'FALLO')
  Write-Host ("{0,-18} restore={1} build={2} test={3} publish={4} warn={5} err={6} ({7}s) {8}" -f $n, $r.Restore, $r.Build, $r.Test, $r.Publish, $r.Warnings, $r.Errors, $r.Seconds, $(if ($ok) { '' } else { '<<< FALLO' }))
  if (-not $ok) { Write-Host "::error title=$n falló::$($r.Detail)" }
}

# ---- Informe
$osInfo = if ($isWin) { (cmd /c ver) -join ' ' } else { [Environment]::OSVersion.VersionString }
$sdk = (& dotnet --version)
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('# WinMac — Informe de CI')
[void]$md.AppendLine('')
[void]$md.AppendLine("Commit: ``$($env:GITHUB_SHA)`` · SO: $osInfo · .NET SDK: $sdk · Configuración: $Configuration")
[void]$md.AppendLine('')
[void]$md.AppendLine('| Componente | Tipo | Restore | Build | Tests | Publish | Warnings | Errores | s |')
[void]$md.AppendLine('|---|---|---|---|---|---|---|---|---|')
foreach ($r in $results) { [void]$md.AppendLine("| $($r.Name) | $($r.Kind) | $($r.Restore) | $($r.Build) | $($r.Test) | $($r.Publish) | $($r.Warnings) | $($r.Errors) | $($r.Seconds) |") }
$bad = @($results | Where-Object { $_.Restore -eq 'FALLO' -or $_.Build -eq 'FALLO' -or $_.Test -eq 'FALLO' -or $_.Publish -eq 'FALLO' })
[void]$md.AppendLine('')
if ($bad.Count) {
  [void]$md.AppendLine('## Fallos'); foreach ($b in $bad) { [void]$md.AppendLine("- **$($b.Name)**: $($b.Detail)") }
} else { [void]$md.AppendLine('**Todos los componentes restauran, compilan, pasan sus pruebas y se publican como artefacto.**') }
[void]$md.AppendLine('')
[void]$md.AppendLine('## Qué valida y qué NO valida este informe')
[void]$md.AppendLine('- Valida: restauración de dependencias, compilación específica de Windows (WinUI/PRI/manifiesto en el runner Windows), pruebas unitarias de lógica pura, y que los binarios se pueden publicar.')
[void]$md.AppendLine('- **NO valida**: comportamiento visual, animaciones, rendimiento real, interacción con ventanas de otras aplicaciones ni compatibilidad GPU. «Compila» no es «funciona».')
[void]$md.AppendLine('- No se publica ninguna versión oficial: los binarios son artefactos temporales del workflow.')
$md.ToString() | Set-Content "$ReportDir/summary.md" -Encoding UTF8
$results | ConvertTo-Json -Depth 4 | Set-Content "$ReportDir/summary.json" -Encoding UTF8
if ($env:GITHUB_STEP_SUMMARY) { Get-Content "$ReportDir/summary.md" -Raw | Add-Content $env:GITHUB_STEP_SUMMARY }
if ($bad.Count) { exit 1 } else { exit 0 }
