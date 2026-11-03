using System.Globalization;
using System.Text.RegularExpressions;
using WinMac.Apps;

namespace WinMac.Bar.Launcher;

/// <summary>A script the user registered in <c>bar.json</c> (<c>SpotlightScripts</c>). Spotlight only runs it after a second, explicit Enter.</summary>
public sealed record SpotScript(string Name, string Path, string? Arguments = null);

/// <summary>
/// Spotlight Pro providers (docs/27). Everything here is pure: it only DESCRIBES results. Nothing typed is ever executed by being typed:
/// network tools and SSH run only on Enter, «&gt; text» never runs (it only opens a terminal or copies the text), user scripts need a second Enter,
/// and nothing is elevated. The host performs the action, with its own validation again.
/// </summary>
public static partial class SpotPro
{
    /// <summary>WinMac's own actions (fixed list; targets are internal ids, never paths).</summary>
    public static readonly (string Title, string Target, string Keywords)[] Actions =
    [
        ("Capturar región", "winmac:capture:region", "captura pantallazo screenshot recorte region imagen"),
        ("Capturar región en 5 segundos", "winmac:capture:region:5", "captura retardo temporizador menu abierto 5 segundos region"),
        ("Capturar pantalla en 5 segundos", "winmac:capture:screen:5", "captura retardo temporizador menu abierto 5 segundos pantalla"),
        ("Capturar ventana", "winmac:capture:window", "captura pantallazo screenshot ventana"),
        ("Capturar pantalla completa", "winmac:capture:screen", "captura pantallazo screenshot pantalla completa monitor"),
        ("Capturar todas las pantallas", "winmac:capture:all", "captura pantallazo screenshot monitores todas"),
        ("Grabar la pantalla", "winmac:record", "grabar grabacion video record pantalla"),
        ("Detener la grabación", "winmac:record:stop", "detener parar grabacion stop"),
        ("Abrir Launchpad", "winmac:launchpad", "launchpad aplicaciones apps"),
        ("Ajustes de WinMac", "winmac:settings", "ajustes winmac configuracion preferencias"),
        ("Guardar espacio de trabajo", "winmac:ws:save", "guardar espacio trabajo ventanas distribucion workspace"),
        ("Ventanas lado a lado", "winmac:ws:side", "ventanas lado dividir mitad side"),
        ("Ventanas en cuadrícula", "winmac:ws:grid", "ventanas cuadricula mosaico grid organizar"),
    ];

    /// <summary>«Restaurar «Espacio 1»» for each saved workspace.</summary>
    public static IEnumerable<(int Score, SpotItem Item)> SearchWorkspaces(string q, IReadOnlyList<string> names)
    {
        var f = AppMatcher.Fold(q.Trim());
        foreach (var n in names)
        {
            string title = "Restaurar «" + n + "»";
            int s = Math.Max(AppMatcher.Score(q, n), AppMatcher.Score(q, title));
            if (s is >= 0 and < SpotlightEngine.Weak) s = -1;
            if (s < 0 && f.Length >= 4 && ("restaurar espacio trabajo workspace").Split(' ').Any(k => k.StartsWith(f, StringComparison.Ordinal))) s = 310;
            if (s >= 0) yield return (s, new SpotItem(SpotKind.Action, title, "Espacio de trabajo guardado", "winmac:ws:restore:" + n));
        }
    }

    public static IEnumerable<(int Score, SpotItem Item)> SearchActions(string q, bool recording)
    {
        var f = AppMatcher.Fold(q.Trim());
        foreach (var (title, target, kw) in Actions)
        {
            if (target == "winmac:record:stop" && !recording) continue;   // only what makes sense now
            if (target == "winmac:record" && recording) continue;
            int s = AppMatcher.Score(q, title);
            if (s is >= 0 and < SpotlightEngine.Weak) s = -1;
            if (s < 0 && f.Length >= 4 && kw.Split(' ').Any(k => k.StartsWith(f, StringComparison.Ordinal))) s = 320;
            if (s >= 0) yield return (s, new SpotItem(SpotKind.Action, title, "Acción de WinMac", target));
        }
    }

    /// <summary>User scripts by name. They are shown, never run on typing; running them asks for a second Enter.</summary>
    public static IEnumerable<SpotItem> SearchScripts(string q, IReadOnlyList<SpotScript> scripts) =>
        scripts.Where(s => !string.IsNullOrWhiteSpace(s.Name) && AppMatcher.Score(q, s.Name) >= 400)
            .Select(s => new SpotItem(SpotKind.Script, s.Name, "Script del usuario · Intro dos veces para ejecutar", s.Path) { Confirm = true, Arguments = s.Arguments });

    // ── network tools
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9\-\.]{0,251}[A-Za-z0-9])?$")] private static partial Regex HostRx();
    [GeneratedRegex(@"^(?:(?<user>[A-Za-z0-9_][A-Za-z0-9_\.\-]{0,63})@)?(?<host>[A-Za-z0-9][A-Za-z0-9\-\.]{0,252})(?::(?<port>\d{1,5}))?$")] private static partial Regex SshRx();

    /// <summary>A host name or IP literal that can be passed as ONE argument (letters, digits, dots, hyphens; or an IPv6 literal). Nothing a shell could interpret.</summary>
    public static bool IsHost(string h) =>
        h.Length is > 0 and <= 253 && (HostRx().IsMatch(h) || (h.Contains(':') && System.Net.IPAddress.TryParse(h, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6));

    /// <summary>Parses «ssh [user@]host[:port]». Null if anything is off (then nothing is offered).</summary>
    public static (string? User, string Host, int? Port)? ParseSsh(string q)
    {
        var t = q.Trim();
        if (!t.StartsWith("ssh ", StringComparison.OrdinalIgnoreCase)) return null;
        var m = SshRx().Match(t[4..].Trim());
        if (!m.Success || !IsHost(m.Groups["host"].Value)) return null;
        int? port = null;
        if (m.Groups["port"].Success) { if (!int.TryParse(m.Groups["port"].Value, out int p) || p is < 1 or > 65535) return null; port = p; }
        return (m.Groups["user"].Success ? m.Groups["user"].Value : null, m.Groups["host"].Value, port);
    }

    /// <summary>The ssh client's argument list (passed as separate arguments, no shell).</summary>
    public static IReadOnlyList<string> SshArguments((string? User, string Host, int? Port) s)
    {
        var a = new List<string>();
        if (s.Port is { } p) { a.Add("-p"); a.Add(p.ToString(CultureInfo.InvariantCulture)); }
        a.Add(s.User is null ? s.Host : s.User + "@" + s.Host);
        return a;
    }

    /// <summary>Tool results for what is typed. Ping and DNS are offered but only run on Enter.</summary>
    public static IEnumerable<SpotItem> Tools(string q)
    {
        var t = q.Trim(); var f = AppMatcher.Fold(t);
        if (f is "ip" or "mi ip" or "ip local" or "direccion ip" or "my ip")
            yield return new SpotItem(SpotKind.Tool, "Dirección IP local", "Herramientas · Intro copia la dirección", "tool:ip");
        if (f is "red" or "network" or "info red" or "informacion de red" or "adaptadores")
            yield return new SpotItem(SpotKind.Tool, "Información de red", "Herramientas · adaptadores activos", "tool:net");
        foreach (var (verb, kind, title) in new[] { ("ping ", "ping", "Hacer ping a "), ("dns ", "dns", "Resolver DNS de "), ("nslookup ", "dns", "Resolver DNS de ") })
            if (t.StartsWith(verb, StringComparison.OrdinalIgnoreCase) && t[verb.Length..].Trim() is { Length: > 0 } host && IsHost(host))
                yield return new SpotItem(SpotKind.Tool, title + host, "Herramientas · Intro para ejecutar (no se hace nada al escribir)", $"tool:{kind}:{host}");
        if (ParseSsh(t) is { } ssh)
            yield return new SpotItem(SpotKind.Command, "Conectar por SSH a " + (ssh.User is null ? "" : ssh.User + "@") + ssh.Host + (ssh.Port is { } p ? ":" + p : ""),
                "Cliente SSH de Windows · Intro abre la conexión en su ventana", "ssh:" + string.Join(' ', SshArguments(ssh)));
        if (t.StartsWith('>') && t.Length > 1)
        {
            var cmd = t[1..].Trim();
            yield return new SpotItem(SpotKind.Command, "Abrir Terminal", "No ejecuta lo escrito: abre una terminal en tu carpeta de usuario", "terminal:");
            if (cmd.Length > 0) yield return new SpotItem(SpotKind.Command, "Copiar «" + (cmd.Length > 40 ? cmd[..40] + "…" : cmd) + "»", "Para pegarlo tú en la terminal", "copy:" + cmd);
        }
    }

    // ── calculator extras
    [GeneratedRegex(@"^\s*(?<p>\d+(?:[.,]\d+)?)\s*%\s*(?:de|of)\s*(?<v>\d+(?:[.,]\d+)?)\s*$", RegexOptions.IgnoreCase)] private static partial Regex PercentOfRx();

    /// <summary>«20% de 150» = 30.</summary>
    public static double? PercentOf(string q)
    {
        var m = PercentOfRx().Match(q);
        if (!m.Success) return null;
        double p = double.Parse(m.Groups["p"].Value.Replace(',', '.'), CultureInfo.InvariantCulture), v = double.Parse(m.Groups["v"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return p * v / 100;
    }

    // ── preview pane
    /// <summary>What the preview pane shows for a result: detail lines and the keys that act on it (only real actions).</summary>
    public static (IReadOnlyList<string> Lines, string Keys) Preview(SpotItem it)
    {
        var lines = new List<string>();
        string keys;
        switch (it.Kind)
        {
            case SpotKind.App: lines.Add("Aplicación instalada"); if (it.App is { } a) lines.Add(a.Id); keys = "Intro abrir"; break;
            case SpotKind.Calc or SpotKind.Convert: lines.Add(it.Subtitle ?? ""); lines.Add(it.Title); keys = "Intro copiar el resultado"; break;
            case SpotKind.Setting: lines.Add("Configuración de Windows"); lines.Add(it.Target); keys = "Intro abrir"; break;
            case SpotKind.Folder or SpotKind.Path: lines.Add(it.Target); keys = "Intro abrir · Ctrl+Intro mostrar en el Explorador"; break;
            case SpotKind.File:
                lines.Add(it.Target);
                keys = SpotlightEngine.IsExecutable(it.Target) ? "Intro mostrar en el Explorador (no se ejecuta)" : "Intro abrir · Ctrl+Intro mostrar en el Explorador"; break;
            case SpotKind.Action: lines.Add("Acción de WinMac"); keys = "Intro ejecutar"; break;
            case SpotKind.Tool: lines.AddRange(it.Details ?? []); keys = it.Target.StartsWith("tool:ping", StringComparison.Ordinal) || it.Target.StartsWith("tool:dns", StringComparison.Ordinal) ? "Intro ejecutar o repetir · Ctrl+C copiar · Esc cancelar" : "Intro copiar"; break;
            case SpotKind.Command: lines.Add(it.Subtitle ?? ""); keys = "Intro"; break;
            case SpotKind.Clipboard:
                lines.Add(it.Subtitle ?? ""); if (it.Details is { Count: > 0 } cd) lines.AddRange(cd);
                keys = it.Arguments == "image" ? "Intro copiar la imagen · Ctrl+Supr quitar del historial" : "Intro copiar · Ctrl+Intro copiar sin formato · Ctrl+Supr quitar del historial";
                return (lines.Where(l => l.Length > 0).ToList(), keys);
            case SpotKind.Script: lines.Add(it.Target); if (it.Arguments is { Length: > 0 } args) lines.Add("Argumentos: " + args); lines.Add("Se ejecuta con tus permisos, nunca elevado"); keys = "Intro dos veces para ejecutar"; break;
            default: keys = "Intro";
            break;
        }

        if (it.Details is { Count: > 0 } d && it.Kind != SpotKind.Tool) lines.AddRange(d);
        return (lines.Where(l => l.Length > 0).ToList(), keys);
    }
}
