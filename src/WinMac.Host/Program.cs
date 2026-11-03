// WinMac — host/supervisor (F1). Un único punto de entrada para el usuario: lanza la Barra superior y el Dock, los vigila y los cierra en orden.
//
//   WinMac.exe                      inicia el host (y con él los módulos habilitados en %APPDATA%\WinMac\winmac.json)
//   WinMac.exe --background         igual; es lo que registra el inicio automático
//   WinMac.exe --quit               pide al host en ejecución que se cierre normalmente (cierra sus módulos)
//   WinMac.exe --install-autostart  crea UNA clave HKCU\...\Run («WinMac») · --remove-autostart la borra
//   WinMac.exe --render-settings <general|bar|hub|notch> --out f.png [--theme light|dark] [--scale 2] [--scroll N] [--suggest] [--no-notch]   dibuja Ajustes fuera de pantalla (CI)
//   Solo pruebas: --config <ruta> · --log <ruta> · --no-tray · --fast · --pipe-name <nombre> · --autostart-name <nombre>
using WinMac.Host.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }
        string exe = Environment.ProcessPath ?? "WinMac.exe", name = HostOptions.Arg(args, "--autostart-name", Autostart.DefaultName);
        try
        {
            if (args.Contains("--install-autostart")) { Autostart.Install(exe, name); Console.WriteLine($"Inicio automático activado ({name} → {Autostart.Current(name)})."); return 0; }
            if (args.Contains("--remove-autostart")) { Console.WriteLine(Autostart.Remove(name) ? "Inicio automático desactivado." : "El inicio automático no estaba activado."); return 0; }
            if (args.Contains("--quit")) return HostApp.SignalQuit();
            if (args.Contains("--render-settings")) return SettingsOffscreen.Run(args);
            if (args.Contains("--render-tray-panel"))   // solo CI: el panel de la bandeja con estados de MUESTRA
            {
                string png = HostOptions.Arg(args, "--out", "bandeja.png");
                var st = new Dictionary<string, WinMac.Host.ModuleState> { ["bar"] = WinMac.Host.ModuleState.Running, ["dock"] = WinMac.Host.ModuleState.Running, ["notch"] = WinMac.Host.ModuleState.Failed };
                File.WriteAllText(png + ".txt", TrayPanel.RenderOffscreen(png, WinMac.Host.TrayPanelModel.Build(st, "2.0"), 2)); return 0;
            }   // solo CI: dibuja Ajustes fuera de pantalla
            return HostApp.Run(HostOptions.Parse(args));
        }
        catch (Exception e) { try { Console.Error.WriteLine("ERROR: " + e); } catch (Exception) { } return 1; }
    }
}
