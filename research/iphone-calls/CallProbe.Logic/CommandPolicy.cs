namespace CallProbe.Logic;

/// <summary>Qué órdenes pueden llegar por el archivo de órdenes (activación del paquete). Las etapas 4–5 (llamadas reales) jamás.</summary>
public static class CommandPolicy
{
    private static readonly string[] Allowed = ["stage1", "stage2", "stage3"];

    public static string[] Parse(string? line)
    {
        var parts = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && Allowed.Contains(parts[0].ToLowerInvariant()) ? parts : [];
    }
}
