using WinMac.Bar;
using WinMac.Dock;

namespace WinMac.Host;

public enum OnboardingStep { Welcome, Components, Appearance, Windows, Review, Result }
/// <summary>Fresh: instalación nueva · Resume: quedó a medias · Rerun: reabierto a propósito desde Ajustes (no se puede «omitir»; se puede cancelar sin cambiar nada) · Migration: bienvenida a quien ya tenía WinMac/Hatsu configurado (conservar o revisar; nada se aplica sin confirmar).</summary>
public enum OnboardingMode { Fresh, Resume, Rerun, Migration }
public enum OnboardingCommand { None, Skip, Apply, Retry, Accept, Finish, Cancel, Keep }

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

    // ───────────────────────────── diseño (una sola fuente: OnboardingLayout)
    public IReadOnlyList<OnboardingHit> Hits => OnboardingLayout.Compute(this).Where(x => x.Kind == BoxKind.Control).Select(x => new OnboardingHit(x.Id, x.X, x.Y, x.W, x.H, x.Enabled)).ToList();
    /// <summary>Segmentos de un control de opciones (los usa el pintor para el contenedor).</summary>
    public const double SegH = 38;
    public bool ShowDetails { get; private set; }

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
            case "keep": return OnboardingCommand.Keep;
            case "apply": return OnboardingCommand.Apply;
            case "retry": return OnboardingCommand.Retry;
            case "details": ShowDetails = !ShowDetails; return OnboardingCommand.None;
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

    public void BeginApply() { Busy = true; Step = OnboardingStep.Result; Result = null; Note = null; ShowDetails = false; }
    public void ShowResult(ApplyResult r) { Busy = false; Result = r; Step = OnboardingStep.Result; HasUnappliedChanges = r.AllOk ? false : HasUnappliedChanges; }
    public void Reset() { Busy = false; Result = null; Step = OnboardingStep.Review; }

    public string StepTitle => Step switch
    {
        OnboardingStep.Welcome => "Bienvenido a Hatsu Desktop", OnboardingStep.Components => "Elige tus componentes", OnboardingStep.Appearance => "Apariencia",
        OnboardingStep.Windows => "Integración con Windows", OnboardingStep.Review => "Revisa tus elecciones", _ => Busy ? "Aplicando…" : Result is { AllOk: true } ? "Todo listo" : Result is { EssentialFailed: true } ? "No se pudo aplicar" : "Configuración aplicada con incidencias",
    };

    /// <summary>Mensaje de la pantalla final (dentro de la tarjeta, junto a la lista).</summary>
    public string ResultMessage => Result is not { } r ? "" :
        r.RolledBack ? "No se aplicó nada: se restauró el estado anterior. Puedes reintentar o cerrar el asistente sin cambios." :
        r.EssentialFailed ? "Algo esencial falló y no se aplicó. Puedes volver a revisar, reintentar o cerrar sin aplicar." :
        r.HasWarnings ? "Lo que sí funciona ya está aplicado. «Reintentar» vuelve a probar lo que falló. «Continuar de todos modos» cierra el asistente con lo que funciona; lo que no se pudo iniciar queda anotado y puedes reintentarlo o desactivarlo en Ajustes." + (r.Items.Any(i => i.Cause == FailureKind.PolicyBlocked) ? " Windows impidió iniciar un componente con una directiva de seguridad; Hatsu Desktop no la cambia (mira «Detalles técnicos»)." : "") :
        "Todo se aplicó y los módulos están en marcha. Puedes cambiar lo que quieras en Ajustes.";
}
