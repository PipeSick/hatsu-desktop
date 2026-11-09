using System.Globalization;
using System.Text;

namespace WinMac.Apps;

/// <summary>
/// Una app instalada tal como la lista Windows en «Todas las aplicaciones» (la carpeta virtual <c>shell:AppsFolder</c>): accesos directos del menú Inicio (Win32) y apps
/// empaquetadas (MSIX/UWP). <see cref="Id"/> es su nombre de análisis en esa carpeta (el AUMID en las empaquetadas): estable entre reinicios y suficiente para
/// lanzarla (<c>shell:AppsFolder\Id</c>) y pedir su icono.
/// </summary>
public sealed record InstalledApp(string Id, string Name, AppKind Kind)
{
    public AppIdentity Identity => AppIdentity.FromAumid(Id, Name);
    public string LaunchTarget => @"shell:AppsFolder\" + Id;
}

/// <summary>
/// El índice de apps compartido por Launchpad y Spotlight. Normaliza lo que entrega Windows: sin repetidos, sin desinstaladores ni documentos sueltos
/// (léame, ayuda, enlaces web que algunos instaladores dejan en el menú Inicio) y ordenado por nombre como lo ordenaría el usuario.
/// </summary>
public sealed class AppCatalog
{
    private static readonly string[] JunkExt = [".url", ".txt", ".chm", ".pdf", ".htm", ".html", ".rtf", ".hlp", ".ini", ".log"];
    private static readonly string[] JunkWords = ["uninstall", "desinstalar", "uninstaller", "desinstalación", "desinstalacion"];
    private IReadOnlyList<InstalledApp> _apps = [];

    public IReadOnlyList<InstalledApp> Apps => _apps;
    public DateTime? UpdatedUtc { get; private set; }

    /// <summary>Se dispara tras cada <see cref="Update"/> que cambia la lista.</summary>
    public event Action? Changed;

    public static bool IsJunk(InstalledApp a)
    {
        if (string.IsNullOrWhiteSpace(a.Id) || string.IsNullOrWhiteSpace(a.Name)) return true;
        var id = a.Id.Trim();
        if (a.Kind == AppKind.Win32 && JunkExt.Any(e => id.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return true;
        if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || id.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        var n = AppMatcher.Fold(a.Name);
        return JunkWords.Any(w => n.Contains(AppMatcher.Fold(w), StringComparison.Ordinal));
    }

    public bool Update(IEnumerable<InstalledApp> fromWindows, DateTime nowUtc)
    {
        var clean = fromWindows.Where(a => a is not null && !IsJunk(a))
            .Select(a => a with { Name = a.Name.Trim(), Id = a.Id.Trim() })
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
            .ToList();
        UpdatedUtc = nowUtc;
        bool changed = clean.Count != _apps.Count || clean.Zip(_apps).Any(p => p.First != p.Second);
        _apps = clean;
        if (changed) Changed?.Invoke();
        return changed;
    }

    public InstalledApp? Find(string id) => _apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Búsqueda por nombre compartida por Launchpad y Spotlight. Sin distinguir mayúsculas ni tildes. De más a menos relevante: nombre exacto, empieza por, una palabra
/// empieza por, iniciales («vsc» → Visual Studio Code), contiene, y letras en orden. Los empates los deshace el uso (lo que más abres sube) y luego el nombre más corto.
/// </summary>
public static class AppMatcher
{
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly char[] Separators = [' ', '-', '_', '.', '(', ')', '[', ']', ':', '/', '\\', '&', '+'];

    /// <summary>Puntuación de <paramref name="name"/> para <paramref name="query"/> (ya sin espacios sobrantes); -1 si no coincide.</summary>
    public static int Score(string query, string name)
    {
        var q = Fold(query.Trim()); if (q.Length == 0) return -1;
        var n = Fold(name);
        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 800;
        var words = n.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 650;
        if (q.Contains(' '))
        {
            var parts = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.All(p => words.Any(w => w.StartsWith(p, StringComparison.Ordinal)))) return 600;
        }

        if (q.Length >= 2 && words.Length >= 2 && string.Concat(words.Select(w => w[0])).StartsWith(q, StringComparison.Ordinal)) return 550;
        int at = n.IndexOf(q, StringComparison.Ordinal);
        if (at >= 0) return 400 - Math.Min(at, 100);
        if (q.Length >= 3 && IsSubsequence(q.Replace(" ", ""), n)) return 150;
        return -1;
    }

    private static bool IsSubsequence(string q, string n)
    {
        int i = 0;
        foreach (var c in n) if (i < q.Length && c == q[i]) i++;
        return i == q.Length;
    }

    /// <summary>Las <paramref name="max"/> mejores, de más a menos relevante. <paramref name="usage"/>: veces que se abrió cada app (por Id).</summary>
    public static IReadOnlyList<InstalledApp> Search(IEnumerable<InstalledApp> apps, string query, IReadOnlyDictionary<string, int>? usage = null, int max = 50)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return apps.Select(a => (App: a, Score: Score(query, a.Name)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score + Math.Min(60, 10 * (usage is not null && usage.TryGetValue(x.App.Id, out int u) ? u : 0)))
            .ThenBy(x => x.App.Name.Length).ThenBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, max)).Select(x => x.App).ToList();
    }
}
