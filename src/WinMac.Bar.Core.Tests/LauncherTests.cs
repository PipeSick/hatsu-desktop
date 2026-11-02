using WinMac.Apps;
using WinMac.Bar.Launcher;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Tecla Windows → Launchpad: máquina de estados pura. Que Windows respete la tecla máscara y no abra el menú Inicio solo se ve en un escritorio real (docs/22).
public class WinKeyTapTests
{
    private const int E = 0x45, L = 0x4C, R = 0x52, Tab = 0x09, Shift = 0x10, S = 0x53;

    [Fact]
    public void AloneTapOpensLaunchpad()
    {
        var t = new WinKeyTap();
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, true, false, 0));
        Assert.Equal(WinKeyAction.MaskAndOpen, t.OnKey(WinKeyTap.VkLWin, false, false, 120));
        Assert.Equal(WinKeyAction.MaskAndOpen, Tap(t, WinKeyTap.VkRWin, 1000));   // también la derecha
    }

    [Theory]
    [InlineData(E)] [InlineData(R)] [InlineData(L)] [InlineData(0x44 /* D */)] [InlineData(Tab)] [InlineData(S)] [InlineData(0x31 /* 1 */)]
    public void ShortcutsWithWindowsAreNeverTouched(int other)
    {
        var t = new WinKeyTap();
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, true, false, 0));
        Assert.Equal(WinKeyAction.Pass, t.OnKey(other, true, false, 30));
        Assert.Equal(WinKeyAction.Pass, t.OnKey(other, false, false, 60));
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, false, 90));   // Win+E abre el Explorador como siempre; Launchpad no
    }

    [Fact]
    public void ModifierPressedBeforeWindowsDoesNotCountButAfterDoes()
    {
        var t = new WinKeyTap();
        t.OnKey(Shift, true, false, 0); t.OnKey(WinKeyTap.VkLWin, true, false, 10);
        t.OnKey(S, true, false, 20);   // Win+Shift+S (recorte de pantalla)
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, false, 50));
    }

    [Fact]
    public void LongPressRepeatsAndInjectedKeysBehave()
    {
        var t = new WinKeyTap();
        t.OnKey(WinKeyTap.VkLWin, true, false, 0); t.OnKey(WinKeyTap.VkLWin, true, false, 300); t.OnKey(WinKeyTap.VkLWin, true, false, 600);   // repetición automática
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, false, 1500));   // larga: Windows decide, como siempre
        t.OnKey(WinKeyTap.VkLWin, true, false, 2000);
        t.OnKey(WinKeyTap.VkMask, true, true, 2010); t.OnKey(WinKeyTap.VkMask, false, true, 2011);   // nuestra tecla máscara (inyectada) no es una combinación
        Assert.Equal(WinKeyAction.MaskAndOpen, t.OnKey(WinKeyTap.VkLWin, false, false, 2100));
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, true, true, 3000));   // una Windows inyectada por otro programa no abre nada
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, true, 3050));
    }

    [Fact]
    public void DisabledOrMouseComboOrResetNeverOpen()
    {
        var t = new WinKeyTap { Enabled = false };
        Assert.Equal(WinKeyAction.Pass, Tap(t, WinKeyTap.VkLWin, 0));
        t.Enabled = true; t.OnKey(WinKeyTap.VkLWin, true, false, 100); t.OnMouse();
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, false, 150));
        t.OnKey(WinKeyTap.VkLWin, true, false, 200); t.Reset();
        Assert.Equal(WinKeyAction.Pass, t.OnKey(WinKeyTap.VkLWin, false, false, 250));   // soltar sin haber visto pulsar (p. ej. tras bloquear la sesión): nada
    }

    private static WinKeyAction Tap(WinKeyTap t, int vk, long at) { t.OnKey(vk, true, false, at); return t.OnKey(vk, false, false, at + 80); }
}

public class AppCatalogTests
{
    private static InstalledApp W(string name, string id) => new(id, name, AppKind.Win32);
    private static InstalledApp P(string name, string aumid) => new(aumid, name, AppKind.Packaged);

    [Fact]
    public void CatalogDropsJunkAndDuplicatesAndSortsLikeAPerson()
    {
        var c = new AppCatalog(); int changed = 0; c.Changed += () => changed++;
        Assert.True(c.Update([W("Zoom", @"{X}\Zoom\Zoom.exe"), P("Calculadora", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), W("Uninstall Zoom", @"{X}\Zoom\uninst.exe"),
            W("Léame", @"{X}\app\readme.txt"), W("Sitio web", "https://example.com"), W("Ábaco", @"{X}\abaco.exe"), W("zoom", @"{x}\zoom\zoom.exe"), W("Desinstalar Foo", @"{X}\foo\u.exe")], DateTime.UtcNow));
        Assert.Equal(["Ábaco", "Calculadora", "Zoom"], c.Apps.Select(a => a.Name));
        Assert.False(c.Update(c.Apps, DateTime.UtcNow));   // misma lista: sin aviso
        Assert.Equal(1, changed);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", c.Find("microsoft.windowscalculator_8wekyb3d8bbwe!app")!.LaunchTarget);
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", c.Apps[1].Identity.Aumid);   // el icono se pide por la misma ruta que el Dock
    }

    [Fact]
    public void SearchRanksExactPrefixWordInitialsContainsAndLetters()
    {
        var apps = new[] { W("Visual Studio Code", "1"), W("Visual Studio 2022", "2"), W("Microsoft Edge", "3"), W("Paint", "4"), W("Paint 3D", "5"), W("Explorador de archivos", "6"), W("Configuración", "7"), W("Bloc de notas", "8") };
        Assert.Equal(["Paint", "Paint 3D"], AppMatcher.Search(apps, "paint").Select(a => a.Name));
        Assert.Equal("Visual Studio Code", AppMatcher.Search(apps, "vsc")[0].Name);                  // iniciales
        Assert.Equal("Microsoft Edge", AppMatcher.Search(apps, "edge")[0].Name);                     // una palabra empieza por
        Assert.Equal("Configuración", AppMatcher.Search(apps, "configuracion")[0].Name);            // sin tildes
        Assert.Equal("Explorador de archivos", AppMatcher.Search(apps, "archiv")[0].Name);
        Assert.Equal("Bloc de notas", AppMatcher.Search(apps, "bloc notas")[0].Name);               // varias palabras
        Assert.Empty(AppMatcher.Search(apps, "zzz")); Assert.Empty(AppMatcher.Search(apps, "  "));
        var usage = new Dictionary<string, int> { ["2"] = 5 };
        Assert.Equal("Visual Studio 2022", AppMatcher.Search(apps, "visual", usage)[0].Name);        // lo que más se abre sube en el empate
    }
}

public class LaunchpadControllerTests
{
    private static List<InstalledApp> Apps(int n) => Enumerable.Range(0, n).Select(i => new InstalledApp("id" + i, $"App {i:00}", AppKind.Win32)).ToList();

    private static LaunchpadController Make(int n, double w = 1920, double h = 1080)
    {
        var c = new LaunchpadController(); c.SetSize(w, h); c.SetApps(Apps(n), 0); c.Reset(); return c;
    }

    [Fact]
    public void GeometryIsAMacStyleGridThatFitsTheScreen()
    {
        var g = LaunchpadGeometry.Compute(1920, 1080);
        Assert.Equal(7, g.Cols); Assert.Equal(5, g.Rows); Assert.Equal(35, g.PerPage);
        Assert.All(g.Cells, c => Assert.True(c.X >= 0 && c.Y > g.Search.Y && c.X + c.W <= 1920 && c.Y + c.H <= g.DotsY));
        var small = LaunchpadGeometry.Compute(1024, 640);
        Assert.InRange(small.PerPage, 6, 35); Assert.All(small.Cells, c => Assert.True(c.X + c.W <= 1024 && c.Y + c.H <= small.DotsY));
    }

    [Fact]
    public void PagesKeyboardAndWheel()
    {
        var c = Make(80);
        Assert.Equal(3, c.PageCount); Assert.Equal(35, c.PageItems.Count);
        Assert.Equal(LpCommandKind.Redraw, c.Key(0x27).Kind); Assert.Equal(0, c.Selected);           // → selecciona la primera
        for (int i = 0; i < 6; i++) c.Key(0x27);
        Assert.Equal(6, c.Selected); c.Key(0x27);                                                        // al final de la fila: página siguiente
        Assert.Equal(1, c.Page); Assert.Equal(0, c.Selected);
        c.Key(0x28); Assert.Equal(7, c.Selected);                                                        // ↓ una fila
        Assert.Equal("id42", c.Key(0x0D).App!.Id);                                                      // Intro abre la elegida
        c.Wheel(-120); Assert.Equal(2, c.Page); Assert.Equal(10, c.PageItems.Count);
        c.Wheel(-120); Assert.Equal(2, c.Page);                                                          // no pasa de la última
        c.Key(0x21); Assert.Equal(1, c.Page);
        Assert.Equal(LpCommandKind.Close, c.Key(0x1B).Kind);                                             // Esc cierra
    }

    [Fact]
    public void TypingSearchesEnterOpensTheBestAndEscClearsFirst()
    {
        var c = new LaunchpadController(); c.SetSize(1920, 1080);
        c.SetApps([new("a", "Paint", AppKind.Win32), new("b", "Calculadora", AppKind.Packaged), new("c", "Microsoft Paint 3D", AppKind.Packaged)], 0);
        c.Char('p'); c.Char('a'); c.Char('i');
        Assert.True(c.Searching); Assert.Equal(["Paint", "Microsoft Paint 3D"], c.PageItems.Select(a => a.Name));
        Assert.Equal("a", c.Key(0x0D).App!.Id);
        c.Key(0x08); Assert.Equal("pa", c.Query);
        Assert.Equal(LpCommandKind.Redraw, c.Key(0x1B).Kind); Assert.False(c.Searching);                  // el primer Esc borra la búsqueda
        Assert.Equal(LpCommandKind.Close, c.Key(0x1B).Kind);
        Assert.Equal(LpCommandKind.None, c.Char(' ').Kind);                                              // un espacio solo no empieza a buscar
    }

    [Fact]
    public void ClickOpensBackgroundClosesAndDragReorders()
    {
        var c = Make(10); var g = c.Geometry; var cell3 = g.Cells[3];
        c.PointerDown(cell3.Cx, cell3.Cy);
        var open = c.PointerUp(cell3.Cx, cell3.Cy); Assert.Equal(LpCommandKind.Launch, open.Kind); Assert.Equal("id3", open.App!.Id);
        Assert.Equal(LpCommandKind.Close, c.PointerUp(5, g.Height - 5).Kind);                           // fondo
        c.PointerDown(cell3.Cx, cell3.Cy); c.PointerMove(cell3.Cx + 30, cell3.Cy); Assert.NotNull(c.Drag);
        var drop = c.PointerUp(g.Cells[0].Cx, g.Cells[0].Cy);
        Assert.Equal(LpCommandKind.Move, drop.Kind); Assert.Equal("id3", drop.App!.Id); Assert.Equal(0, drop.Index);
    }

    [Fact]
    public void ContextMenuOffersOrganisation()
    {
        var c = Make(10); var cell = c.Geometry.Cells[2];
        c.RightClick(cell.Cx, cell.Cy);
        Assert.Equal(["open", "front", "hide"], c.Menu!.Items.Select(i => i.Id));
        var r = c.Menu.ItemRect(2); var hide = c.PointerUp(r.Cx, r.Cy);
        Assert.Equal(LpCommandKind.Hide, hide.Kind); Assert.Equal("id2", hide.App!.Id); Assert.Null(c.Menu);
        c.SetApps(Apps(10).Skip(1).ToList(), 1); c.RightClick(3, 3);
        Assert.Equal(["unhide", "reset"], c.Menu!.Items.Select(i => i.Id));
        Assert.Equal(LpCommandKind.Redraw, c.Key(0x1B).Kind); Assert.Null(c.Menu);
    }

    [Fact]
    public void PrefsKeepOrderHiddenAndUsageAndSurviveACorruptFile()
    {
        var apps = Apps(5); var p = new LauncherPrefs();
        Assert.True(p.Move(p.Arrange(apps), "id4", 0));
        Assert.Equal(["id4", "id0", "id1", "id2", "id3"], p.Arrange(apps).Select(a => a.Id));
        p.Hide("id1"); Assert.DoesNotContain(p.Arrange(apps), a => a.Id == "id1"); Assert.Equal(5, p.Arrange(apps, includeHidden: true).Count);
        var more = apps.Append(new InstalledApp("new", "AAA nueva", AppKind.Win32)).ToList();
        Assert.Equal("new", p.Arrange(more).Last().Id);                                                  // una app recién instalada va al final
        p.RecordLaunch("id2"); p.RecordLaunch("id2"); Assert.Equal(2, p.Usage["ID2"]);
        string f = Path.Combine(Path.GetTempPath(), "launcher-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            p.Save(f); var back = LauncherPrefs.Load(f);
            Assert.Equal(p.Arrange(apps).Select(a => a.Id), back.Arrange(apps).Select(a => a.Id)); Assert.Contains("ID1", back.Hidden);
            File.WriteAllText(f, "{ roto"); Assert.Empty(LauncherPrefs.Load(f).Order);
        }
        finally { File.Delete(f); }
        p.UnhideAll(); p.ResetOrder(); Assert.Equal(apps.Select(a => a.Id), p.Arrange(apps).Select(a => a.Id));
    }
}

public class SpotlightTests
{
    private static readonly InstalledApp[] Apps = [new("calc", "Calculadora", AppKind.Packaged), new("np", "Bloc de notas", AppKind.Win32), new("code", "Visual Studio Code", AppKind.Win32)];
    private static readonly (string, string)[] Folders = [("Descargas", @"C:\Users\a\Downloads"), ("Documentos", @"C:\Users\a\Documents")];

    [Theory]
    [InlineData("2+2", 4)] [InlineData("2 + 3 * 4", 14)] [InlineData("(2+3)*4", 20)] [InlineData("2^10", 1024)] [InlineData("10/4", 2.5)] [InlineData("1,5*2", 3)]
    [InlineData("-3+5", 2)] [InlineData("50%*200", 100)] [InlineData("7×6", 42)] [InlineData("9÷3", 3)]
    public void CalculatorEvaluates(string expr, double expected) => Assert.Equal(expected, SpotCalculator.Evaluate(expr)!.Value, 9);

    [Theory]
    [InlineData("1/0")] [InlineData("2+")] [InlineData("(2")] [InlineData("abc")] [InlineData("2 2")] [InlineData("notepad.exe")] [InlineData("rm -rf /")]
    public void CalculatorRejectsAnythingElse(string expr) => Assert.Null(SpotCalculator.Evaluate(expr));

    [Fact]
    public void ConversionsCoverTheBasics()
    {
        Assert.Equal("6,2137 mi", SpotConverter.Convert("10 km en mi")!.Value.Result);
        Assert.Equal("212 °F", SpotConverter.Convert("100 c to f")!.Value.Result);
        Assert.Equal("2.048 MB", SpotConverter.Convert("2 gb en mb")!.Value.Result);
        Assert.Null(SpotConverter.Convert("10 km en kg"));   // dimensiones distintas
        Assert.Null(SpotConverter.Convert("hola mundo"));
    }

    [Fact]
    public void QueryMixesSourcesAppsComeFromTheSharedIndex()
    {
        var r = SpotlightEngine.Query("calc", Apps, null, Folders, []);
        Assert.Equal(SpotKind.App, r[0].Kind); Assert.Same(Apps[0], r[0].App);
        var m = SpotlightEngine.Query("12*12", Apps, null, Folders, []);
        Assert.Equal("144", m[0].Title); Assert.Equal(SpotKind.Calc, m[0].Kind);
        var s = SpotlightEngine.Query("bluetooth", Apps, null, Folders, []);
        Assert.Contains(s, i => i.Kind == SpotKind.Setting && i.Target == "ms-settings:bluetooth");
        Assert.Contains(SpotlightEngine.Query("descar", Apps, null, Folders, []), i => i.Kind == SpotKind.Folder && i.Target.EndsWith("Downloads"));
        Assert.Contains(SpotlightEngine.Query("resolucion", Apps, null, Folders, []), i => i.Target == "ms-settings:display");   // por palabra clave
        Assert.Empty(SpotlightEngine.Query("   ", Apps, null, Folders, []));
    }

    [Fact]
    public void PathsOnlyWhenTheyExistAndExecutablesAreRevealedNotRun()
    {
        Assert.Contains(SpotlightEngine.Query(@"C:\Windows", Apps, null, Folders, [], p => p == @"C:\Windows"), i => i.Kind == SpotKind.Path);
        Assert.DoesNotContain(SpotlightEngine.Query(@"C:\NoExiste", Apps, null, Folders, [], _ => false), i => i.Kind == SpotKind.Path);
        Assert.True(SpotlightEngine.IsExecutable(@"C:\x\setup.EXE")); Assert.True(SpotlightEngine.IsExecutable("a.ps1")); Assert.False(SpotlightEngine.IsExecutable("informe.docx"));
        Assert.Equal((Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\x").Replace('/', '\\'), SpotlightEngine.ExpandPath("~\\x"));
    }

    [Fact]
    public void RecentFilesMatchByNameNewestFirst()
    {
        var now = DateTime.UtcNow;
        var r = SpotlightEngine.MatchRecent("informe", [("Informe anual.docx", "a.lnk", now.AddDays(-3)), ("Informe trimestral.xlsx", "b.lnk", now), ("Fotos", "c.lnk", now)]);
        Assert.Equal(["Informe trimestral.xlsx", "Informe anual.docx"], r.Select(i => i.Title));
        Assert.Empty(SpotlightEngine.MatchRecent("i", [("Informe", "a.lnk", now)]));
    }

    [Fact]
    public void ControllerKeyboard()
    {
        var c = new SpotlightController();
        foreach (var ch in "blo") Assert.True(c.Char(ch));
        c.SetResults(SpotlightEngine.Query(c.Query, Apps, null, Folders, []));
        Assert.Equal(0, c.Selected);
        var (cmd, _) = c.Key(0x0D, false); Assert.Equal(SpotCommandKind.Open, cmd.Kind); Assert.Equal("np", cmd.Item!.Target);
        c.Key(0x28, false); Assert.Equal(c.Results.Count > 1 ? 1 : 0, c.Selected);
        Assert.True(c.Key(0x08, true).TextChanged); Assert.Equal("", c.Query);   // Ctrl+Retroceso borra la palabra
        foreach (var ch in "doc") c.Char(ch);
        c.SetResults(SpotlightEngine.Query(c.Query, Apps, null, Folders, []));
        Assert.Equal(SpotCommandKind.Reveal, c.Key(0x0D, true).Command.Kind);   // Ctrl+Intro: mostrar la carpeta en el Explorador
        Assert.Equal(SpotCommandKind.Redraw, c.Key(0x1B, false).Command.Kind); Assert.Equal(SpotCommandKind.Close, c.Key(0x1B, false).Command.Kind);
    }
}

public class AppMenuTests
{
    private static readonly WinMac.Bar.Menus.AppMenuEntry File = new("Archivo", 0, Children:
    [
        new("Nuevo", 101, Shortcut: "Ctrl+N"), new("Abrir…", 102, Shortcut: "Ctrl+O"), WinMac.Bar.Menus.AppMenuEntry.Sep,
        new("Recientes", 0, Children: [new("informe.txt", 201), new("notas.txt", 202, Enabled: false)]),
        new("Ajuste de línea", 103, Checked: true), WinMac.Bar.Menus.AppMenuEntry.Sep, new("Salir", 104, Enabled: false), WinMac.Bar.Menus.AppMenuEntry.Sep,
    ]);

    [Theory]
    [InlineData("&Abrir…\tCtrl+O", "Abrir…", "Ctrl+O")] [InlineData("Guardar &como...", "Guardar como...", null)] [InlineData("Tom && Jerry\t", "Tom & Jerry", null)]
    public void LabelsAndShortcutsComeFromTheRealMenuText(string raw, string label, string? sc)
    {
        var (l, s) = WinMac.Bar.Menus.AppMenuEntry.ParseLabel(raw); Assert.Equal(label, l); Assert.Equal(sc, s);
    }

    [Fact]
    public void PanelShowsShortcutsChecksDisabledSeparatorsAndSubmenus()
    {
        var p = WinMac.Bar.Menus.AppMenuPanel.Build(File, []);
        Assert.Equal(PanelKind.AppMenu, p.Kind);
        Assert.Equal(["Nuevo", "Abrir…", "", "Recientes", "Ajuste de línea", "", "Salir"], p.Widgets.Select(w => w.Kind == WidgetKind.Separator ? "" : w.Text));   // sin separador al final
        Assert.Equal("Ctrl+N", p.Widgets[0].Sub); Assert.Equal("›", p.Widgets[3].Sub); Assert.True(p.Widgets[4].On); Assert.False(p.Widgets[6].Enabled);
    }

    [Fact]
    public void ClickRunsCommandsOpensSubmenusAndIgnoresDisabled()
    {
        Assert.Equal(102, WinMac.Bar.Menus.AppMenuPanel.Click(File, [], "am:1").Command);
        var (cmd, path) = WinMac.Bar.Menus.AppMenuPanel.Click(File, [], "am:3"); Assert.Equal(0, cmd); Assert.Equal([3], path!);
        var sub = WinMac.Bar.Menus.AppMenuPanel.Build(File, path!);
        Assert.Equal("back", sub.Widgets[0].Id); Assert.Equal("Recientes", sub.Widgets[0].Text);
        Assert.Equal(201, WinMac.Bar.Menus.AppMenuPanel.Click(File, path!, "am:0").Command);
        Assert.Equal(0, WinMac.Bar.Menus.AppMenuPanel.Click(File, path!, "am:1").Command);   // deshabilitada: no se ejecuta
        Assert.Equal(0, WinMac.Bar.Menus.AppMenuPanel.Click(File, [], "am:6").Command);
        Assert.Equal(0, WinMac.Bar.Menus.AppMenuPanel.Click(File, [], "am:99").Command);
        Assert.Null(new WinMac.Bar.Menus.AppMenus(WinMac.Bar.Menus.AppMenuSource.None, []).Labels);   // sin menús: nada inventado
    }
}
