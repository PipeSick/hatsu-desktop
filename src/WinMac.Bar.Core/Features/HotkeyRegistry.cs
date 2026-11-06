namespace WinMac.Bar.Features;

public enum ClaimStatus { Ok, Invalid, Reserved, Conflict }
public sealed record ClaimResult(ClaimStatus Status, string? With = null)
{
    public bool Ok => Status == ClaimStatus.Ok;
    public string Explain() => Status switch
    {
        ClaimStatus.Ok => "libre", ClaimStatus.Invalid => "combinación no válida (necesita Ctrl o Alt y una tecla)",
        ClaimStatus.Reserved => "Windows la reserva: " + With, _ => "ya la usa " + With,
    };
}

/// <summary>
/// Registro central de atajos globales de Hatsu con dueño (módulo + id). Pura: decide quién puede tener qué; el registro real en Windows (<c>RegisterHotKey</c>) lo hace un adaptador que
/// consulta aquí primero. Nunca quita un atajo a otro programa: solo evita que dos módulos de Hatsu pidan lo mismo y que se pida lo que Windows reserva.
/// </summary>
public sealed class HotkeyRegistry
{
    public sealed record Claim(string Owner, string Id, string Purpose, HotkeySpec Spec, bool OsAccepted = true);
    private readonly List<Claim> _claims = []; private readonly object _l = new();

    public ClaimResult Check(string? spec, string owner, string id)
    {
        if (!HotkeySpec.TryParse(spec, out var s)) return new ClaimResult(ClaimStatus.Invalid);
        if (s.ReservedBy() is { } r) return new ClaimResult(ClaimStatus.Reserved, r);
        lock (_l) { var o = _claims.FirstOrDefault(c => c.Spec == s && !(c.Owner == owner && c.Id == id)); return o is null ? new ClaimResult(ClaimStatus.Ok) : new ClaimResult(ClaimStatus.Conflict, $"«{o.Purpose}» ({o.Owner})"); }
    }

    public bool IsAvailable(string? spec, string owner, string id) => Check(spec, owner, id).Ok;

    /// <summary>Reclama (o reasigna) un atajo. Si falla conserva el anterior del mismo dueño/id.</summary>
    public ClaimResult TryClaim(string owner, string id, string purpose, string? spec)
    {
        var r = Check(spec, owner, id); if (!r.Ok) return r;
        HotkeySpec.TryParse(spec, out var s);
        lock (_l) { _claims.RemoveAll(c => c.Owner == owner && c.Id == id); _claims.Add(new Claim(owner, id, purpose, s)); }
        return r;
    }

    /// <summary>Anota un atajo que ya existía antes del registro (Spotlight, capturas) para que los módulos nuevos vean el conflicto. No valida.</summary>
    public void Note(string owner, string id, string purpose, string? spec)
    {
        if (!HotkeySpec.TryParse(spec, out var s)) return;
        lock (_l) { _claims.RemoveAll(c => c.Owner == owner && c.Id == id); _claims.Add(new Claim(owner, id, purpose, s)); }
    }

    public void SetOsResult(string owner, string id, bool accepted) { lock (_l) { int i = _claims.FindIndex(c => c.Owner == owner && c.Id == id); if (i >= 0) _claims[i] = _claims[i] with { OsAccepted = accepted }; } }
    public bool Release(string owner, string id) { lock (_l) return _claims.RemoveAll(c => c.Owner == owner && c.Id == id) > 0; }
    public int ReleaseOwner(string owner) { lock (_l) return _claims.RemoveAll(c => c.Owner == owner); }
    public IReadOnlyList<Claim> Claims { get { lock (_l) return [.. _claims]; } }
}
