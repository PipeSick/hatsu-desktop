<#
.SYNOPSIS
  Diagnóstico de WM-008 (docs/36): reproduce la búsqueda de letras de NotchWin (NeteaseLyricsClient) para una canción y muestra qué devuelve el proveedor.
  Solo lectura: no cambia el motor. Mismas reglas: título igual o contenido, artista contenido, duración del proveedor a ±20 s de la del reproductor.
#>
param([string]$Title = 'Fighting Myself', [string]$Artist = 'Linkin Park', [int]$Seconds = 195)
$h = @{ 'User-Agent' = 'Mozilla/5.0'; 'Referer' = 'https://music.163.com' }
function Q($s) { [Uri]::EscapeDataString($s) }
foreach ($query in @("$Artist $Title", $Title)) {
  Write-Host "== búsqueda «$query»"
  try { $r = Invoke-RestMethod -Uri "https://music.163.com/api/search/get?s=$(Q $query)&type=1&limit=8" -Headers $h -TimeoutSec 20 } catch { Write-Host "  error: $($_.Exception.Message)"; continue }
  if (-not $r.result.songs) { Write-Host "  sin resultados (code $($r.code))"; continue }
  foreach ($s in $r.result.songs) {
    $artists = ($s.artists | ForEach-Object { $_.name }) -join ', '; $dur = [int]($s.duration / 1000)
    $titleOk = ($s.name -ieq $Title) -or $s.name.ToLower().Contains($Title.ToLower()) -or $Title.ToLower().Contains($s.name.ToLower())
    $artistOk = [bool]($s.artists | Where-Object { $_.name.ToLower().Contains($Artist.ToLower()) -or $Artist.ToLower().Contains($_.name.ToLower()) })
    $durOk = [Math]::Abs($dur - $Seconds) -le 20
    $lyr = ''
    if ($titleOk -and $artistOk) {
      try {
        $l = Invoke-RestMethod -Uri "https://music.163.com/api/song/lyric?id=$($s.id)&lv=1" -Headers $h -TimeoutSec 20
        $lrc = [string]$l.lrc.lyric; $timed = ([regex]::Matches($lrc, '\[\d{1,2}:\d{2}')).Count
        $lyr = "letra: $($lrc.Length) car., $timed líneas con tiempo" + $(if ($l.nolyric) { ' (nolyric)' } elseif ($l.uncollected) { ' (sin recopilar)' } else { '' })
      } catch { $lyr = "letra: error $($_.Exception.Message)" }
    }
    Write-Host ("  {0} «{1}» — {2} — {3}s · título {4} · artista {5} · duración {6} · {7}" -f $s.id, $s.name, $artists, $dur, $titleOk, $artistOk, $durOk, $lyr)
  }
}
# Comparación informativa con LRCLIB (servicio libre, sin clave): ¿existe una letra sincronizada para esa canción en otra fuente?
try {
  $x = Invoke-RestMethod -Uri "https://lrclib.net/api/search?track_name=$(Q $Title)&artist_name=$(Q $Artist)" -TimeoutSec 20
  Write-Host "== LRCLIB: $(@($x).Count) resultado(s)"; @($x) | Select-Object -First 5 | ForEach-Object { Write-Host ("  «{0}» — {1} — {2}s · sincronizada {3}" -f $_.trackName, $_.artistName, [int]$_.duration, [bool]$_.syncedLyrics) }
} catch { Write-Host "== LRCLIB no disponible: $($_.Exception.Message)" }
