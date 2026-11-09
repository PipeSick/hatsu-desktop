// WinMac — host/supervisor (F1). Un único punto de entrada para el usuario: lanza la Barra superior y el Dock, los vigila y los cierra en orden.
//
//   HatsuDesktop.exe                      inicia el host (y con él los módulos habilitados en %APPDATA%\WinMac\winmac.json)
//   HatsuDesktop.exe --background         igual; es lo que registra el inicio automático
//   HatsuDesktop.exe --diagnose [--out f.txt] [--open] [--quiet]   informe local de SOLO LECTURA (versión, componentes, procesos, firma, asistente, inicio automático, barra de tareas); no envía nada
//   HatsuDesktop.exe --uninstall-cleanup  (desinstalador MSI) restaura la barra de tareas y quita SOLO el inicio automático de esta instalación; no borra datos
//   HatsuDesktop.exe --no-onboarding · --onboarding-auto skip   (pruebas) sin asistente / asistente aplicado sin ventana
//   HatsuDesktop.exe --render-onboarding <Welcome|Components|Appearance|Windows|Review|Result> --out f.png [--theme light|dark] [--scale 2]   dibuja el asistente fuera de pantalla (CI)
//   HatsuDesktop.exe --quit               pide al host en ejecución que se cierre normalmente (cierra sus módulos)
//   HatsuDesktop.exe --diagnose-security  informe de solo lectura: Smart App Control, Integridad de código y antivirus sobre archivos de WinMac (WM-036)
//   HatsuDesktop.exe --restore-taskbar    vuelve a mostrar la barra de tareas de Windows y su «Ocultar automáticamente» como estaba (WM-034), aunque WinMac no pueda arrancar
//   HatsuDesktop.exe --adopt-autostart   el inicio automático pasa a ESTA instalación (solo si ya estaba activado; nunca lo crea)
//   HatsuDesktop.exe --replace-running   cierra la instancia en ejecución (también una de WinMac anterior) y continúa con esta
//   HatsuDesktop.exe --install-autostart  crea UNA clave HKCU\...\Run («WinMac») · --remove-autostart la borra
//   HatsuDesktop.exe --render-settings <general|bar|hub|notch> --out f.png [--theme light|dark] [--scale 2] [--scroll N] [--suggest] [--no-notch]   dibuja Ajustes fuera de pantalla (CI)
//   Solo pruebas: --config <ruta> · --log <ruta> · --no-tray · --fast · --pipe-name <nombre> · --autostart-name <nombre>
using WinMac.Host.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        WinMac.Host.NotchSettings.AgentsOffByDefault = PackageIdentity.IsPackaged;   // Store: agentes de IA desactivados en instalaciones nuevas (activables a mano)
        args = WinMac.Host.LaunchArguments.Effective(args, PackageIdentity.IsStartupTaskActivation());   // MSIX: la StartupTask llega sin argumentos → segundo plano
        try { Native.SetErrorMode(0x0001 | 0x8000); } catch (Exception) { }   // sin cuadros modales del sistema ante un ejecutable no válido o un archivo ausente (bloquearían el arranque de módulos y, con él, la bandeja y el asistente)
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }
        string exe = Environment.ProcessPath ?? "HatsuDesktop.exe", name = HostOptions.Arg(args, "--autostart-name", Autostart.DefaultName);
        try
        {
            if (args.Contains("--package-info")) { Console.WriteLine((PackageIdentity.FullName is { } pf ? "package=" + pf : "package=none") + " agentsDefault=" + (WinMac.Host.NotchSettings.Factory.AgentsEnabled ? "on" : "off")); return 0; }
            if (args.Contains("--install-autostart")) { Autostart.Install(exe, name); Console.WriteLine($"Inicio automático activado ({name} → {Autostart.Current(name)})."); return 0; }
            if (args.Contains("--remove-autostart")) { Console.WriteLine(Autostart.Remove(name) ? "Inicio automático desactivado." : "El inicio automático no estaba activado."); return 0; }
            if (args.Contains("--adopt-autostart"))   // adopción EXPLÍCITA: el inicio automático pasa a esta instalación (solo si ya estaba activado; nunca lo crea)
            {
                var st = Autostart.Assess(exe, name).State;
                if (st is WinMac.Host.AutostartState.NotConfigured) { Console.WriteLine("El inicio automático no está activado: no se cambió nada (actívalo en Ajustes › General o con --install-autostart)."); return 0; }
                var written = Autostart.Adopt(exe, name);
                Console.WriteLine(written is null ? (st == WinMac.Host.AutostartState.Current ? "El inicio automático ya apunta a esta instalación." : "No se cambió nada (el valor no se entiende).") : $"Inicio automático ({name}) → {written}");
                return 0;
            }
            if (args.Contains("--uninstall-cleanup"))   // lo ejecuta el desinstalador MSI antes de quitar archivos: devuelve la barra de tareas y quita SOLO el inicio automático que apunta a esta instalación; nunca borra datos
            {
                int rc = TaskbarHider.RestoreFromCommandLine(quiet: true);
                var stored = Autostart.Current(name);
                if (WinMac.Host.HostCoexistence.AutostartBelongsTo(stored, exe)) { Autostart.Remove(name); Console.WriteLine("Inicio automático de esta instalación quitado."); }
                else Console.WriteLine(stored is null ? "No había inicio automático." : "El inicio automático pertenece a otra copia: no se toca.");
                return rc;
            }
            if (args.Contains("--diagnose"))   // informe local de SOLO LECTURA (Fase 3.1)
            {
                var path = InstallDiagnose.Run(HostOptions.Parse(args), args.Contains("--out") ? HostOptions.Arg(args, "--out", "") : null, open: args.Contains("--open"), print: !args.Contains("--quiet")); return path is null ? 1 : 0;
            }
            if (args.Contains("--quit")) return HostApp.SignalQuit();
            if (args.Contains("--diagnose-security")) return SecurityDiagnostics.Run(quiet: args.Contains("--quiet"));   // WM-036: solo lectura
            if (args.Contains("--restore-taskbar")) return TaskbarHider.RestoreFromCommandLine(quiet: args.Contains("--quiet"));   // WM-034: recuperación manual, sin arrancar WinMac
            if (args.Contains("--taskbar-guard")) return int.TryParse(HostOptions.Arg(args, "--taskbar-guard", ""), out int hostPid) ? TaskbarHider.RunGuard(hostPid) : 2;
            if (args.Contains("--render-settings")) return SettingsOffscreen.Run(args);
            if (args.Contains("--render-onboarding"))   // solo CI: una pantalla del asistente con elecciones de MUESTRA, fuera de pantalla
            {
                string png = HostOptions.Arg(args, "--out", "asistente.png"); var stepName = HostOptions.Arg(args, "--render-onboarding", "Welcome");
                try
                {
                    var step = Enum.Parse<WinMac.Host.OnboardingStep>(stepName, ignoreCase: true);
                    File.WriteAllText(png + ".txt", OnboardingWindow.RenderOffscreen(png, step, HostOptions.Arg(args, "--theme", "dark") != "light", double.TryParse(HostOptions.Arg(args, "--scale", "2"), System.Globalization.CultureInfo.InvariantCulture, out var osc) ? osc : 2, args.Contains("--details") ? "details" : args.Contains("--migration") ? "migration" : null)); return 0;
                }
                catch (Exception e) { try { File.WriteAllText(png + ".txt", "ERROR: " + e); } catch (IOException) { } return 1; }
            }
            if (args.Contains("--render-tray-panel"))   // solo CI: el panel de la bandeja con estados de MUESTRA
            {
                string png = HostOptions.Arg(args, "--out", "bandeja.png");
                var st = new Dictionary<string, WinMac.Host.ModuleState> { ["bar"] = WinMac.Host.ModuleState.Running, ["dock"] = WinMac.Host.ModuleState.Running, ["notch"] = WinMac.Host.ModuleState.Failed };
                File.WriteAllText(png + ".txt", TrayPanel.RenderOffscreen(png, WinMac.Host.TrayPanelModel.Build(st, "2.0"), double.TryParse(HostOptions.Arg(args, "--scale", "2"), System.Globalization.CultureInfo.InvariantCulture, out var tsc) ? tsc : 2, dark: HostOptions.Arg(args, "--theme", "dark") != "light")); return 0;
            }   // solo CI: dibuja Ajustes fuera de pantalla
            return HostApp.Run(HostOptions.Parse(args));
        }
        catch (Exception e) { try { Console.Error.WriteLine("ERROR: " + e); } catch (Exception) { } return 1; }
    }
}
