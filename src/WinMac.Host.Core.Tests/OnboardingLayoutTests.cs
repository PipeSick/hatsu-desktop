using WinMac.Dock;
using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>
/// Geometría y contraste del asistente. Las medidas son las MISMAS que usa el pintor y el controlador (OnboardingLayout), así que una superposición aquí sería una superposición en pantalla.
/// Detectan: texto sobre texto, texto sobre un botón, nada invadiendo el pie de botones, nada fuera de los márgenes, y colores sin contraste suficiente.
/// Lo que NO detectan (hay que mirar las capturas): estética, y que el alto real de un texto en Segoe UI coincida con la estimación.
/// </summary>
public class OnboardingLayoutTests
{
    internal static OnboardingController At(OnboardingStep step, OnboardingChoices? choices = null, OnboardingMode mode = OnboardingMode.Fresh, ApplyResult? result = null, bool busy = false)
    {
        var c = new OnboardingController(mode == OnboardingMode.Resume ? OnboardingMode.Fresh : mode, choices ?? OnboardingChoices.Safe);
        int steps = Array.IndexOf(OnboardingController.Flow, step); for (int i = 0; i < steps; i++) c.Click("next");
        if (step == OnboardingStep.Result) { for (int i = 0; i < 4; i++) c.Click("next"); if (busy) c.BeginApply(); else c.ShowResult(result ?? Ok()); }
        return c;
    }
    private static ApplyResult Ok() => new([new("config", "Guardar la configuración", ApplyOutcome.Ok), new("mod:bar", "Barra superior", ApplyOutcome.Ok, "En ejecución"), new("mod:connect", "Hatsu Connect", ApplyOutcome.Ok, "Desactivado: no se inicia", Inactive: true)], OnboardingStatus.Completed, false);
    private static ApplyResult Warn(int items = 5) => new(Enumerable.Range(0, items - 1).Select(i => new ApplyItem("mod:" + i, "Módulo " + i, ApplyOutcome.Ok, "En ejecución")).Append(new ApplyItem("mod:notch", "Dynamic Island", ApplyOutcome.Warning, "No se pudo iniciar", "estado del supervisor: Failed")).ToList(), OnboardingStatus.InProgress, false);
    private static ApplyResult Failed() => new([new("autostart", "Inicio automático", ApplyOutcome.Failed, "directiva de grupo"), new("rollback", "Restaurar el estado anterior", ApplyOutcome.Ok, "No se dejó nada a medias.")], OnboardingStatus.NotStarted, true);

    public static IEnumerable<object[]> Scenarios()
    {
        var all = OnboardingChoices.Safe with { Connect = true, Peripherals = true, Cleanup = true, Autostart = true, HideTaskbar = true, Theme = ThemeMode.Dark, DockAlignment = DockAlignment.Right, DockSize = 3 };
        foreach (var step in new[] { OnboardingStep.Welcome, OnboardingStep.Components, OnboardingStep.Appearance, OnboardingStep.Windows, OnboardingStep.Review })
        {
            yield return [step, "defecto", OnboardingChoices.Safe, OnboardingMode.Fresh];
            yield return [step, "todo activado", all, OnboardingMode.Fresh];
            yield return [step, "sin Dock", OnboardingChoices.Safe with { Dock = false }, OnboardingMode.Fresh];
            yield return [step, "sin Barra", OnboardingChoices.Safe with { Bar = false }, OnboardingMode.Fresh];
            yield return [step, "reabierto", OnboardingChoices.Safe, OnboardingMode.Rerun];
            yield return [step, "migración", OnboardingChoices.Safe, OnboardingMode.Migration];
        }
    }

    private static bool IsContainer(LayoutBox b) => b.Kind == BoxKind.Card || b.Id.StartsWith("c:") || b.Id.StartsWith("t:") || b.Id.EndsWith(".seg") || b.Id == "p.desk";
    internal static void AssertGeometry(OnboardingController c, string what)
    {
        var boxes = OnboardingLayout.Compute(c);
        foreach (var b in boxes) Assert.True(b.X >= -0.01 && b.Y >= -0.01 && b.Right <= OnboardingLayout.W + 0.01 && b.Bottom <= OnboardingLayout.H + 0.01, $"{what}: {b.Id} se sale de la ventana ({b})");
        foreach (var b in boxes.Where(b => !b.Id.StartsWith("w.") && !b.Id.StartsWith("h.") && b.Id is not ("dots" or "note") ))
            Assert.True(b.X >= OnboardingLayout.MarginX - 0.01 && b.Right <= OnboardingLayout.W - OnboardingLayout.MarginX + 0.01, $"{what}: {b.Id} rompe el margen de 40 ({b.X:0}–{b.Right:0})");
        var leaves = boxes.Where(b => !IsContainer(b)).ToList(); var containers = boxes.Where(IsContainer).ToList();
        for (int i = 0; i < leaves.Count; i++)
            for (int j = i + 1; j < leaves.Count; j++)
                Assert.False(leaves[i].Overlaps(leaves[j]), $"{what}: se superponen {leaves[i].Id} {Fmt(leaves[i])} y {leaves[j].Id} {Fmt(leaves[j])}");
        foreach (var l in leaves) foreach (var k in containers)
                if (l.Overlaps(k)) Assert.True(k.Contains(l), $"{what}: {l.Id} {Fmt(l)} cruza el borde de {k.Id} {Fmt(k)}");
        foreach (var k1 in containers.Where(k => k.Kind == BoxKind.Card || k.Id.StartsWith("c:") || k.Id.StartsWith("t:"))) foreach (var k2 in containers.Where(k => k.Kind == BoxKind.Card || k.Id.StartsWith("c:") || k.Id.StartsWith("t:")))
                if (!ReferenceEquals(k1, k2) && string.CompareOrdinal(k1.Id, k2.Id) < 0) Assert.False(k1.Overlaps(k2), $"{what}: se superponen las superficies {k1.Id} y {k2.Id}");
        // el pie (botones) queda libre: nada de contenido entra en su franja, salvo los propios botones y los puntos de progreso
        foreach (var b in boxes.Where(b => !(b.Kind == BoxKind.Control && b.Y >= OnboardingLayout.FooterY - 0.01) && b.Id is not ("dots" or "w.note") && !b.Id.StartsWith("c:") || b.Id.StartsWith("c:")))
            Assert.True(b.Bottom <= OnboardingLayout.FooterY - 4 + 0.01 || b.Id is "dots", $"{what}: {b.Id} ({b.Bottom:0}) invade el pie de botones (y ≥ {OnboardingLayout.FooterY})");
    }
    private static string Fmt(LayoutBox b) => $"[{b.X:0.#},{b.Y:0.#} {b.W:0.#}×{b.H:0.#}]";

    [Theory, MemberData(nameof(Scenarios))]
    public void No_text_or_control_overlaps_anything_and_nothing_invades_the_footer(OnboardingStep step, string name, OnboardingChoices choices, OnboardingMode mode) =>
        AssertGeometry(At(step, choices, mode), $"{step}/{name}");

    [Fact] public void The_result_screen_has_room_for_every_kind_of_outcome_and_never_overlaps_the_buttons()
    {
        AssertGeometry(At(OnboardingStep.Result, result: Ok()), "Result/ok"); AssertGeometry(At(OnboardingStep.Result, result: Warn()), "Result/incidencias");
        AssertGeometry(At(OnboardingStep.Result, result: Warn(12)), "Result/muchas filas"); AssertGeometry(At(OnboardingStep.Result, result: Failed()), "Result/fallo esencial"); AssertGeometry(At(OnboardingStep.Result, busy: true), "Result/aplicando");
        var d = At(OnboardingStep.Result, result: Warn(8)); d.Click("details"); AssertGeometry(d, "Result/detalles técnicos");
    }

    [Fact] public void The_progress_dots_are_gone_from_the_result_and_welcome_screens_and_never_share_space_with_buttons()
    {
        Assert.DoesNotContain(OnboardingLayout.Compute(At(OnboardingStep.Result, result: Warn())), b => b.Id == "dots");
        Assert.DoesNotContain(OnboardingLayout.Compute(At(OnboardingStep.Welcome)), b => b.Id == "dots");
        foreach (var s in new[] { OnboardingStep.Components, OnboardingStep.Appearance, OnboardingStep.Windows, OnboardingStep.Review })
        { var bx = OnboardingLayout.Compute(At(s)); var dots = bx.Single(b => b.Id == "dots"); Assert.DoesNotContain(bx.Where(b => b.Kind == BoxKind.Control && b.Id is "back" or "next" or "apply"), b => b.Overlaps(dots)); }
    }

    [Fact] public void Appearance_stacks_label_control_note_in_order_with_real_gaps_the_reported_defect()
    {
        var b = OnboardingLayout.Compute(At(OnboardingStep.Appearance)).ToDictionary(x => x.Id);
        Assert.True(b["a.n1"].Y >= b["theme:0"].Bottom + 2, "la nota del tema debe empezar bajo el control del tema");
        Assert.True(b["a.l2"].Y >= b["a.n1"].Bottom + 8, "«Tamaño del Dock» debe empezar bajo la nota con un respiro");
        var order = new[] { "a.l1", "theme:0", "a.n1", "a.l2", "size:0", "a.l3", "align:0", "a.l4", "bg:0" }; for (int i = 1; i < order.Length; i++) Assert.True(b[order[i]].Y >= b[order[i - 1]].Bottom - 0.01, $"{order[i]} debe ir debajo de {order[i - 1]}");
        Assert.Equal(OnboardingLayout.ThemeNote, "Se aplica al Dock. La Barra superior y la Isla siguen siempre el tema de Windows.");
        Assert.True(b["a.n1"].H >= TextFit.Height(OnboardingLayout.ThemeNote, 420, OnboardingLayout.FontSmall, OnboardingLayout.LineSmall) - 0.01, "la nota reserva el alto real de su texto");
    }

    [Fact] public void The_appearance_preview_is_a_horizontal_desktop()
    {
        var d = OnboardingLayout.Compute(At(OnboardingStep.Appearance)).Single(b => b.Id == "p.desk"); Assert.True(d.W > d.H, $"debe ser apaisada: {d.W}×{d.H}"); Assert.InRange(d.W / d.H, 1.5, 1.8);
    }

    [Fact] public void Components_that_need_the_Bar_say_so_on_their_card_and_only_they_do()
    {
        var chips = OnboardingLayout.Compute(At(OnboardingStep.Components)).Where(b => b.Id.StartsWith("cr:")).Select(b => b.Id[3..]).OrderBy(x => x).ToArray();
        Assert.Equal(["cleanup", "peripherals"], chips); Assert.Equal("Requiere Barra superior", OnboardingRules.RequiresBarText);
        Assert.True(TextFit.Lines(OnboardingRules.RequiresBarText, 168 - 8, 11.5) == 1, "el texto de la etiqueta cabe en su caja");
        Assert.All(new[] { "dock", "bar", "notch", "connect" }, id => Assert.False(OnboardingRules.RequiresBar(id)));
    }

    [Fact] public void Single_line_texts_really_fit_on_one_line()
    {
        foreach (var sz in new[] { OnboardingChoices.Safe, OnboardingChoices.Safe with { Autostart = true, HideTaskbar = true, DockSize = 3, DockAlignment = DockAlignment.Right } })
            foreach (var (l, v) in OnboardingRules.Review(sz)) { Assert.Equal(1, TextFit.Lines(v, 420, OnboardingLayout.FontBody)); Assert.Equal(1, TextFit.Lines(l, 250, OnboardingLayout.FontBody)); }
        foreach (var (names, n) in new[] { (OnboardingRules.ThemeNames, 3), (OnboardingRules.AlignmentNames, 3), (OnboardingRules.BackgroundNames, 2), (DockSettings.Presets.Select(p => p.Name).ToArray(), 4) })
            foreach (var name in names) Assert.True(TextFit.Lines(name, 420.0 / n - 12, OnboardingLayout.FontBody) == 1, $"«{name}» no cabe en un segmento de {n}");
        Assert.Equal(1, TextFit.Lines("Configuración aplicada con incidencias", 500, 22));   // título de la pantalla final
    }

    [Fact] public void Every_button_label_fits_inside_its_button_in_every_screen()
    {
        string Label(string id) => id switch { "next" => "Siguiente", "back" => "Atrás", "apply" => "Aplicar y comenzar", "retry" => "Reintentar", "accept" => "Continuar de todos modos", "cancel" => "Cerrar sin aplicar", "finish" => "Empezar", "skip" => "Omitir configuración", _ => "" };
        var cs = new List<OnboardingController> { At(OnboardingStep.Welcome), At(OnboardingStep.Components), At(OnboardingStep.Review), At(OnboardingStep.Result, result: Ok()), At(OnboardingStep.Result, result: Warn()), At(OnboardingStep.Result, result: Failed()) };
        foreach (var c in cs) foreach (var b in OnboardingLayout.Compute(c).Where(b => b.Kind == BoxKind.Control && Label(b.Id) != ""))
                Assert.True(TextFit.Lines(Label(b.Id), b.W - 20, OnboardingLayout.FontBody) == 1, $"«{Label(b.Id)}» no cabe en su botón ({b.W:0} de ancho) en {c.Step}");
    }

    [Fact] public void Rounding_to_device_pixels_at_common_scales_never_creates_an_overlap()
    {
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            foreach (var step in new[] { OnboardingStep.Components, OnboardingStep.Appearance, OnboardingStep.Windows, OnboardingStep.Review })
            {
                var leaves = OnboardingLayout.Compute(At(step)).Where(b => !IsContainer(b)).Select(b => b with { X = Math.Round(b.X * scale), Y = Math.Round(b.Y * scale), W = Math.Round(b.Right * scale) - Math.Round(b.X * scale), H = Math.Round(b.Bottom * scale) - Math.Round(b.Y * scale) }).ToList();
                for (int i = 0; i < leaves.Count; i++) for (int j = i + 1; j < leaves.Count; j++) Assert.False(leaves[i].Overlaps(leaves[j]), $"{step} a {scale}×: {leaves[i].Id} y {leaves[j].Id}");
            }
    }

    [Fact] public void Hits_are_exactly_the_clickable_boxes_and_a_disabled_control_is_not_clickable()
    {
        var c = At(OnboardingStep.Windows, OnboardingChoices.Safe with { Dock = false });
        var hide = c.Hits.Single(h => h.Id == "t:hidetask"); Assert.False(hide.Enabled); Assert.Null(c.HitTest(hide.X + 5, hide.Y + 5));
        var a = OnboardingLayout.Compute(c).Where(b => b.Kind == BoxKind.Control).Select(b => b.Id).OrderBy(x => x); Assert.Equal(a, c.Hits.Select(h => h.Id).OrderBy(x => x));
        foreach (var s in new[] { OnboardingStep.Welcome, OnboardingStep.Components, OnboardingStep.Appearance, OnboardingStep.Review }) { var cc = At(s); foreach (var h in cc.Hits.Where(h => h.Enabled)) Assert.Equal(h.Id, cc.HitTest(h.X + h.W / 2, h.Y + h.H / 2)); }
    }

    // ── contraste (WCAG AA 4,5:1 para texto; 3:1 para el indicador neutro)
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Text_colors_have_AA_contrast_on_every_surface_in_both_themes(bool dark)
    {
        var p = OnboardingPalette.For(dark);
        foreach (var (name, color) in new[] { ("Ink", p.Ink), ("Muted", p.Muted), ("AccentText", p.AccentText), ("Warn", p.Warn), ("Bad", p.Bad), ("Good", p.Good) })
            foreach (var surface in p.Surfaces()) Assert.True(OnboardingPalette.Contrast(color, surface) >= 4.5, $"{(dark ? "oscuro" : "claro")}: {name} {color:X6} sobre {surface:X6} = {OnboardingPalette.Contrast(color, surface):0.00}:1");
        foreach (var surface in p.Surfaces()) Assert.True(OnboardingPalette.Contrast(p.Neutral, surface) >= 3.0, $"indicador neutro sobre {surface:X6} = {OnboardingPalette.Contrast(p.Neutral, surface):0.00}:1");
    }

    [Fact] public void White_text_on_the_primary_button_keeps_AA_contrast_normal_and_hover()
    {
        foreach (var bg in new[] { 0x7C3AED, 0x6D28D9, 0x5B21B6 }) Assert.True(OnboardingPalette.Contrast(0xFFFFFF, bg) >= 4.5, $"blanco sobre {bg:X6} = {OnboardingPalette.Contrast(0xFFFFFF, bg):0.00}");
    }

    [Fact] public void The_light_theme_keeps_the_Flux_hues_but_uses_dark_enough_variants_for_text()
    {
        var l = OnboardingPalette.LightTheme; Assert.Equal(0x6D28D9, l.AccentText);   // violeta Flux oscurecido para texto (el #A78BFA original daba ≈2:1 sobre #EDE9FE)
        Assert.True(OnboardingPalette.Contrast(0xA78BFA, l.BgTop) < 3, "la prueba documenta por qué no se usa #A78BFA como texto en claro");
        Assert.Equal(0xEDE9FE, l.BgTop); Assert.Equal(0x0B0618, OnboardingPalette.DarkTheme.BgTop);
        Assert.True(OnboardingPalette.Contrast(l.Warn, l.BgBottom) >= 4.5);
    }

    // ── textos y estados
    [Fact] public async Task Failed_module_wording_is_user_friendly_with_the_technical_detail_apart()
    {
        var p = new FakePorts { Config = OnboardingDecision.GateFresh(new WinMac.Host.HostConfig(), new OnboardingRecord { Status = OnboardingStatus.NotStarted }) }; p.StartFails.Add("notch");
        var r = await new OnboardingApplier(p) { ModuleTimeout = TimeSpan.Zero }.ApplyAsync(OnboardingChoices.Safe);
        var it = r.Items.Single(i => i.Id == "mod:notch"); Assert.Equal("No se pudo iniciar", it.Detail); Assert.Equal("estado del supervisor: Failed", it.Technical); Assert.DoesNotContain("Failed", it.Detail);
        Assert.True(r.Items.Single(i => i.Id == "mod:connect").Inactive); Assert.False(r.Items.Single(i => i.Id == "mod:dock").Inactive);   // desactivado = neutro, no verde
    }

    [Fact] public void The_result_title_and_message_say_what_happened()
    {
        var w = At(OnboardingStep.Result, result: Warn()); Assert.Equal("Configuración aplicada con incidencias", w.StepTitle); Assert.DoesNotContain("Casi listo", w.StepTitle);
        Assert.Contains("Continuar de todos modos", w.ResultMessage); Assert.Contains("Reintentar", w.ResultMessage); Assert.Contains("Ajustes", w.ResultMessage);
        Assert.Equal("Todo listo", At(OnboardingStep.Result, result: Ok()).StepTitle); Assert.Equal("No se pudo aplicar", At(OnboardingStep.Result, result: Failed()).StepTitle); Assert.Contains("se restauró", At(OnboardingStep.Result, result: Failed()).ResultMessage);
    }

    [Fact] public void Technical_details_are_a_separate_toggle_hidden_by_default()
    {
        var c = At(OnboardingStep.Result, result: Warn()); var l = OnboardingLayout.Compute(c); Assert.Contains(l, b => b.Id == "details"); Assert.DoesNotContain(l, b => b.Id == "x.tech");
        c.Click("details"); Assert.Contains(OnboardingLayout.Compute(c), b => b.Id == "x.tech"); c.Click("details"); Assert.DoesNotContain(OnboardingLayout.Compute(c), b => b.Id == "x.tech");
        Assert.DoesNotContain(OnboardingLayout.Compute(At(OnboardingStep.Result, result: Ok())), b => b.Id == "details");
    }

    [Fact] public void Review_labels_are_unique_and_the_dock_row_is_named_size_and_position()
    {
        foreach (var ch in new[] { OnboardingChoices.Safe, OnboardingChoices.Safe with { Autostart = true, HideTaskbar = true } })
        { var labels = OnboardingRules.Review(ch).Select(r => r.Label).ToList(); Assert.Equal(labels.Count, labels.Distinct().Count()); Assert.Contains("Tamaño y posición del Dock", labels); Assert.Equal(1, labels.Count(l => l == "Dock")); }
    }

    [Fact] public void Welcome_explains_what_skipping_does_and_does_not_do()
    {
        var n = OnboardingLayout.WelcomeSkipNote; Assert.Contains("componentes predeterminados", n); Assert.Contains("No cambia el inicio automático", n); Assert.Contains("ni oculta la barra de tareas", n);
        Assert.DoesNotContain("nada se cambia en Windows", n);   // era impreciso: sí se activan Dock, Barra e Isla
    }

    [Fact] public void A_new_install_starts_with_autostart_and_hidden_taskbar_off()
    {
        // lo que el host entrega al asistente en una instalación nueva (sin elecciones pendientes) son los valores seguros
        var c = new OnboardingController(OnboardingMode.Fresh, new OnboardingRecord { Status = OnboardingStatus.NotStarted }.Pending ?? OnboardingChoices.Safe);
        Assert.False(c.Choices.Autostart); Assert.False(c.Choices.HideTaskbar); Assert.False(OnboardingChoices.Safe.Autostart || OnboardingChoices.Safe.HideTaskbar);
        var review = OnboardingRules.Review(c.Choices); Assert.Contains(review, r => r.Label == "Iniciar con Windows" && r.Value == "No"); Assert.Contains(review, r => r.Label == "Barra de tareas de Windows" && r.Value == "Se queda como está");
    }
}
