using WinMac.Bar;
using WinMac.Dock;

namespace WinMac.Host;

public enum OnboardingStep { Welcome, Components, Appearance, Windows, Review, Result }
/// <summary>Fresh: instalación nueva · Resume: quedó a medias · Rerun: reabierto a propósito desde Ajustes (no se puede «omitir»; se puede cancelar sin cambiar nada).</summary>
public enum OnboardingMode { Fresh, Resume, Rerun }
public enum OnboardingCommand { None, Skip, Apply, Retry, Accept, Finish, Cancel }

/// <summary>Zona clicable de la ventana (DIP). El pintor dibuja con estas mismas medidas: lo que se ve es lo que se pulsa.</summary>
public sealed record OnboardingHit(string Id, double X, double Y, double W, double H, bool Enabled = true)
{
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>
/// Estado y diseño del asistente. Puro: sin ventanas, probado en Linux. Nada se aplica al navegar entre pantallas; solo <see cref="OnboardingCommand.Apply"/> (desde la revisión) o
/// <see cref="OnboardingCommand.Skip"/> hacen que el host aplique algo.
/// </summary>
public sealed class OnboardingController
{
    public const double WinW = 800, WinH = 560;

    public OnboardingMode Mode { get; }
    public OnboardingStep Step { get; private set; }
    public OnboardingChoices Choices { get; private set; }
    public string? Note { get; private set; }
    public ApplyResult? Result { get; private set; }
    public bool Busy { get; private set; }
    public string? Hover { get; set; }
    /// <summary>Hay elecciones sin aplicar (para avisar al cerrar).</summary>
    public bool HasUnappliedChanges { get; private set; }

    public OnboardingController(OnboardingMode mode, OnboardingChoices initial)
    {
        Mode = mode; Choices = OnboardingRules.Validate(initial).Choices;
        Step = mode == OnboardingMode.Resume ? OnboardingStep.Review : OnboardingStep.Welcome;
        if (mode == OnboardingMode.Resume) Note = "La configuración quedó a medias. Revisa tus elecciones y aplica, o cambia lo que quieras.";
    }

    public static readonly OnboardingStep[] Flow = [OnboardingStep.Welcome, OnboardingStep.Components, OnboardingStep.Appearance, OnboardingStep.Windows, OnboardingStep.Review];

    // ───────────────────────────── diseño
    public IReadOnlyList<OnboardingHit> Hits
    {
        get
        {
            var h = new List<OnboardingHit>();
            switch (Step)
            {
                case OnboardingStep.Welcome:
                    h.Add(new("next", 290, 392, 220, 46));
                    h.Add(new(Mode == OnboardingMode.Rerun ? "cancel" : "skip", 270, 450, 260, 32));
                    break;
                case OnboardingStep.Components:
                    for (int i = 0; i < OnboardingRules.Components.Length; i++) h.Add(new("c:" + OnboardingRules.Components[i], 40 + (i % 2) * 370, 96 + (i / 2) * 104, 350, 94));
                    Nav(h, "Siguiente"); break;
                case OnboardingStep.Appearance:
                    Segs(h, "theme", 3, 132, true); Segs(h, "size", DockSettings.Presets.Length, 206, Choices.Dock); Segs(h, "align", 3, 280, Choices.Dock); Segs(h, "bg", 2, 354, Choices.Bar);
                    Nav(h, "Siguiente"); break;
                case OnboardingStep.Windows:
                    h.Add(new("t:autostart", 40, 104, 720, 104)); h.Add(new("t:hidetask", 40, 224, 720, 104, Choices.Dock));
                    Nav(h, "Siguiente"); break;
                case OnboardingStep.Review:
                    h.Add(new("back", 40, 492, 120, 44)); h.Add(new("apply", 540, 492, 220, 44)); break;
                case OnboardingStep.Result:
                    if (Busy) break;
                    if (Result is { EssentialFailed: true }) { h.Add(new("back", 40, 492, 150, 44)); h.Add(new("retry", 600, 492, 160, 44)); h.Add(new("cancel", 420, 492, 160, 44)); }
                    else if (Result is { HasWarnings: true }) { h.Add(new("retry", 420, 492, 150, 44)); h.Add(new("accept", 580, 492, 180, 44)); }
                    else h.Add(new("finish", 570, 492, 190, 44));
                    break;
            }
            return h;
        }
    }

    private static void Nav(List<OnboardingHit> h, string next) { h.Add(new("back", 40, 492, 120, 44)); h.Add(new("next", 640, 492, 120, 44)); }
    /// <summary>Segmentos de un control de opciones: ancho total 420 a partir de x = 40.</summary>
    public const double SegX = 40, SegW = 420, SegH = 38;
    private static void Segs(List<OnboardingHit> h, string id, int n, double y, bool enabled) { for (int i = 0; i < n; i++) h.Add(new($"{id}:{i}", SegX + i * SegW / n, y, SegW / n, SegH, enabled)); }

    public string? HitTest(double x, double y) => Hits.FirstOrDefault(h => h.Contains(x, y)) is { Enabled: true } hit ? hit.Id : null;

    // ───────────────────────────── acciones
    /// <summary>Procesa un clic o una pulsación. Devuelve la orden que el host debe ejecutar (casi siempre ninguna).</summary>
    public OnboardingCommand Click(string id)
    {
        if (Busy) return OnboardingCommand.None;
        Note = null;
        if (id.StartsWith("c:")) { var (c, n) = OnboardingRules.Toggle(Choices, id[2..], !OnboardingRules.Get(Choices, id[2..])); Choices = c; Note = n; HasUnappliedChanges = true; return OnboardingCommand.None; }
        if (id.StartsWith("theme:")) { Choices = Choices with { Theme = (ThemeMode)int.Parse(id[6..]) }; HasUnappliedChanges = true; return OnboardingCommand.None; }
        if (id.StartsWith("size:")) { Choices = Choices with { DockSize = int.Parse(id[5..]) }; HasUnappliedChanges = true; return OnboardingCommand.None; }
        if (id.StartsWith("align:")) { Choices = Choices with { DockAlignment = (DockAlignment)int.Parse(id[6..]) }; HasUnappliedChanges = true; return OnboardingCommand.None; }
        if (id.StartsWith("bg:")) { Choices = Choices with { BarBackground = int.Parse(id[3..]) == 0 ? BarBackground.Glass : BarBackground.Transparent }; HasUnappliedChanges = true; return OnboardingCommand.None; }
        switch (id)
        {
            case "t:autostart": Choices = Choices with { Autostart = !Choices.Autostart }; HasUnappliedChanges = true; return OnboardingCommand.None;
            case "t:hidetask":
                if (!Choices.Dock) { Note = "Sin el Dock no se oculta la barra de tareas de Windows."; return OnboardingCommand.None; }
                Choices = Choices with { HideTaskbar = !Choices.HideTaskbar }; HasUnappliedChanges = true; return OnboardingCommand.None;
            case "next": GoTo(NextStep(+1)); return OnboardingCommand.None;
            case "back":
                if (Step == OnboardingStep.Result) { Result = null; Step = OnboardingStep.Review; return OnboardingCommand.None; }
                GoTo(NextStep(-1)); return OnboardingCommand.None;
            case "skip": return Mode == OnboardingMode.Rerun ? OnboardingCommand.Cancel : OnboardingCommand.Skip;
            case "cancel": return OnboardingCommand.Cancel;
            case "apply": return OnboardingCommand.Apply;
            case "retry": return OnboardingCommand.Retry;
            case "accept": return OnboardingCommand.Accept;
            case "finish": return OnboardingCommand.Finish;
        }
        return OnboardingCommand.None;
    }

    private OnboardingStep NextStep(int d)
    {
        int i = Array.IndexOf(Flow, Step); if (i < 0) return Step;
        return Flow[Math.Clamp(i + d, 0, Flow.Length - 1)];
    }

    private void GoTo(OnboardingStep s) { Step = s; }

    /// <summary>Enter: la acción principal de la pantalla.</summary>
    public OnboardingCommand Primary() => Hits.FirstOrDefault(h => h.Id is "next" or "apply" or "finish" or "retry") is { } h ? Click(h.Id) : OnboardingCommand.None;
    /// <summary>Escape: volver, o cancelar en la primera pantalla.</summary>
    public OnboardingCommand Escape() => Step == OnboardingStep.Welcome ? OnboardingCommand.Cancel : Busy ? OnboardingCommand.None : Click("back");

    public void BeginApply() { Busy = true; Step = OnboardingStep.Result; Result = null; Note = null; }
    public void ShowResult(ApplyResult r) { Busy = false; Result = r; Step = OnboardingStep.Result; HasUnappliedChanges = r.AllOk ? false : HasUnappliedChanges; }
    public void Reset() { Busy = false; Result = null; Step = OnboardingStep.Review; }

    public string StepTitle => Step switch
    {
        OnboardingStep.Welcome => "Bienvenido a Hatsu Desktop", OnboardingStep.Components => "Elige tus componentes", OnboardingStep.Appearance => "Apariencia",
        OnboardingStep.Windows => "Integración con Windows", OnboardingStep.Review => "Revisa tus elecciones", _ => Busy ? "Aplicando…" : Result is { AllOk: true } ? "Todo listo" : Result is { EssentialFailed: true } ? "No se pudo aplicar" : "Casi listo",
    };
}
