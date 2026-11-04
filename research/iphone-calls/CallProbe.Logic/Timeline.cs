namespace CallProbe.Logic;

/// <summary>Una lectura de solo lectura del transporte en un instante. Los campos null = «no se pudo leer» (distinto de False).</summary>
public sealed record TimelineSample(double Seconds, string Label, bool? IsRegistered, string? AudioRouting, bool? ClassicConnected, int? LineCount, bool CallActive, bool CallIncoming, string? Error = null);

/// <summary>Qué se puede y qué NO se puede concluir de la evolución temporal de AudioRoutingStatus.</summary>
public enum AudioFinding { NoData, Constant, ChangedWithConnection, ChangedAfterRegister, ChangedUnexplained }

public sealed record TimelineSummary(IReadOnlyList<string> Changes, AudioFinding Audio, string AudioReading, IReadOnlyList<string> Limits);

public static class TimelineAnalyzer
{
    /// <param name="registerAtSeconds">instante en que se llamó RegisterApp (null si no se llamó: observación pura)</param>
    public static TimelineSummary Summarize(IReadOnlyList<TimelineSample> s, double? registerAtSeconds)
    {
        var changes = new List<string>();
        for (int i = 1; i < s.Count; i++)
        {
            var a = s[i - 1]; var b = s[i];
            void Diff<T>(string name, T? x, T? y) { if (!EqualityComparer<T?>.Default.Equals(x, y)) changes.Add($"{b.Seconds:0.0} s · {name}: {x?.ToString() ?? "n/d"} → {y?.ToString() ?? "n/d"}  [{b.Label}]"); }
            Diff("IsRegistered", a.IsRegistered, b.IsRegistered); Diff("AudioRoutingStatus", a.AudioRouting, b.AudioRouting); Diff("Classic conectado", a.ClassicConnected, b.ClassicConnected);
            Diff("líneas", a.LineCount, b.LineCount); Diff("llamada activa", a.CallActive, b.CallActive); Diff("llamada entrante", a.CallIncoming, b.CallIncoming);
        }

        var audio = s.Where(x => x.AudioRouting is not null).ToList();
        var limits = new List<string>
        {
            "AudioRoutingStatus solo dice si Windows PERMITIRÍA enrutar el audio de una llamada al PC; no prueba que haya llamada, SCO, ni audio funcional.",
            "Un valor estable durante unos segundos no descarta cambios con una llamada real, ni con otra conexión HFP.",
        };
        if (s.Any(x => x.IsRegistered != true)) limits.Add("Mientras IsRegistered() sea False, cualquier valor de AudioRoutingStatus describe un transporte NO registrado por esta app: no demuestra nada sobre llamadas.");
        if (audio.Count == 0) return new(changes, AudioFinding.NoData, "Sin lecturas válidas de AudioRoutingStatus.", limits);

        var distinct = audio.Select(x => x.AudioRouting!).Distinct().ToList();
        if (distinct.Count == 1) return new(changes, AudioFinding.Constant, $"Constante en «{distinct[0]}» durante {audio[^1].Seconds - audio[0].Seconds:0.0} s ({audio.Count} lecturas).", limits);

        for (int i = 1; i < audio.Count; i++)
        {
            if (audio[i].AudioRouting == audio[i - 1].AudioRouting) continue;
            var before = audio[i - 1]; var after = audio[i];
            bool connChange = before.ClassicConnected != after.ClassicConnected;
            bool afterRegister = registerAtSeconds is double r && before.Seconds >= r - 0.001 && before.IsRegistered != true && after.IsRegistered != true && after.Seconds - r < 30;
            if (connChange) return new(changes, AudioFinding.ChangedWithConnection, $"Cambió {before.AudioRouting} → {after.AudioRouting} a los {after.Seconds:0.0} s a la vez que cambió la conexión Classic: se correlaciona con el enlace, no con el registro.", limits);
            if (afterRegister) return new(changes, AudioFinding.ChangedAfterRegister, $"Cambió {before.AudioRouting} → {after.AudioRouting} tras RegisterApp() aunque IsRegistered() siguió sin ser True: correlación temporal, sin causa demostrada.", limits);
            return new(changes, AudioFinding.ChangedUnexplained, $"Cambió {before.AudioRouting} → {after.AudioRouting} a los {after.Seconds:0.0} s sin cambio concurrente observable en conexión ni registro.", limits);
        }

        return new(changes, AudioFinding.Constant, "Sin cambios.", limits);
    }
}
