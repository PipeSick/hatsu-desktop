namespace WinMac.Spikes.Common;

public enum Pose { None, Rest, Hover, Hidden }

/// <summary>Opciones comunes: --variant X · --pose rest|hover|hidden (estático, para capturas) · --interactive · --out DIR · --quick (fases cortas, solo humo).</summary>
public sealed record CliOptions(string Variant, Pose Pose, bool Interactive, string OutDir, bool Quick)
{
    public bool IsBench => Pose == Pose.None && !Interactive;

    public static CliOptions Parse(string[] args, string defaultVariant)
    {
        string? Get(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        Pose pose = Get("--pose")?.ToLowerInvariant() switch { "rest" => Pose.Rest, "hover" => Pose.Hover, "hidden" => Pose.Hidden, _ => Pose.None };
        return new CliOptions(Get("--variant") ?? defaultVariant, pose, args.Contains("--interactive"), Get("--out") ?? "bench-results", args.Contains("--quick"));
    }
}
