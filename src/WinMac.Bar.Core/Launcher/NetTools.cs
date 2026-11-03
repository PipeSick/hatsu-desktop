namespace WinMac.Bar.Launcher;

/// <summary>One echo of a real ping: success with its round trip, or the reason (Windows' IPStatus name or an error).</summary>
public readonly record struct PingEcho(bool Ok, long Ms, string Status, string? Address = null);

/// <summary>
/// Spotlight's ping panel (docs/27, WM-021): destination, state, latency, sent/received/loss and understandable errors, from REAL replies only. Pure and tested.
/// </summary>
public static class PingReport
{
    public const int Count = 4;

    public static IReadOnlyList<string> Lines(string host, IReadOnlyList<PingEcho> replies, bool running, bool cancelled = false, string? error = null)
    {
        var l = new List<string>();
        int ok = replies.Count(r => r.Ok), sent = replies.Count;
        string? addr = replies.FirstOrDefault(r => r.Address is not null).Address;
        l.Add("Destino: " + host + (addr is not null && addr != host ? $" ({addr})" : ""));
        string state = error is not null ? "Error" : cancelled ? "Cancelado" : running ? $"Enviando… {sent}/{Count}" : ok == sent && sent > 0 ? "Responde" : ok > 0 ? "Responde con pérdidas" : "Sin respuesta";
        l.Add("Estado: " + state);
        if (error is not null) { l.Add(Explain(error)); return l; }
        if (sent > 0)
        {
            int loss = (int)Math.Round(100.0 * (sent - ok) / sent);
            l.Add($"Paquetes: {sent} enviados · {ok} recibidos · {loss} % perdidos");
            var ms = replies.Where(r => r.Ok).Select(r => r.Ms).ToList();
            if (ms.Count > 0) l.Add($"Latencia: mín. {ms.Min()} ms · media {Math.Round(ms.Average())} ms · máx. {ms.Max()} ms");
            var fails = replies.Where(r => !r.Ok).Select(r => Explain(r.Status)).Distinct().ToList();
            foreach (var f in fails.Take(2)) l.Add(f);
        }

        return l;
    }

    /// <summary>Windows' statuses and exceptions in plain words.</summary>
    public static string Explain(string status) => status switch
    {
        "TimedOut" => "Sin respuesta: el equipo no contestó a tiempo (puede bloquear el ping)",
        "DestinationHostUnreachable" or "DestinationNetworkUnreachable" => "No se puede llegar a ese destino desde esta red",
        "HostNotFound" => "No existe ese nombre: el DNS no lo encontró",
        "NoNetwork" => "Sin conexión de red",
        "TtlExpired" or "TimeExceeded" => "El paquete se perdió por el camino (TTL agotado)",
        _ when status.Contains("No such host", StringComparison.OrdinalIgnoreCase) || status.Contains("No se conoce", StringComparison.OrdinalIgnoreCase) || status.Contains("host is known", StringComparison.OrdinalIgnoreCase) => "No existe ese nombre: el DNS no lo encontró",
        _ => "Error: " + status,
    };
}
