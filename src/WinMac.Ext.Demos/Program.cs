using WinMac.Extensions;

// Two demo extensions in one executable; the manifest chooses which one with its first argument.
//  · pomodoro [--minutes N]: a focus timer shown as an activity with real progress; a notice when it ends.
//  · uptime: how long Windows has been running, refreshed every 10 seconds.
var mode = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "pomodoro";
var ext = WinMacExtension.Connect(args, mode == "uptime" ? "uptime" : "pomodoro", "1.0.0");
if (ext is null) { Console.Error.WriteLine("Esta extensión la inicia WinMac."); return 2; }
using var stop = new CancellationTokenSource();
ext.ShutdownRequested += stop.Cancel;

try
{
    if (mode == "uptime")
    {
        while (!stop.IsCancellationRequested)
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            ext.ShowActivity("uptime", "Windows encendido", $"{(int)up.TotalDays} d {up.Hours} h {up.Minutes} min");
            await Task.Delay(TimeSpan.FromSeconds(10), stop.Token);
        }
    }
    else
    {
        int minutes = 25; int i = Array.IndexOf(args, "--minutes");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) minutes = Math.Clamp(m, 1, 120);
        var total = TimeSpan.FromMinutes(minutes); var start = DateTime.UtcNow;
        while (!stop.IsCancellationRequested)
        {
            var left = total - (DateTime.UtcNow - start);
            if (left <= TimeSpan.Zero) break;
            ext.ShowActivity("focus", "Concentración", $"Quedan {(int)left.TotalMinutes:00}:{left.Seconds:00}", 1 - left.TotalSeconds / total.TotalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
        }

        if (!stop.IsCancellationRequested) { ext.FinishActivity("focus"); ext.Notify("Pomodoro terminado", "Tómate cinco minutos de descanso"); await Task.Delay(1500); }
        while (!stop.IsCancellationRequested) await Task.Delay(1000, stop.Token);   // stays available until WinMac stops it
    }
}
catch (OperationCanceledException) { }
await ext.DisposeAsync();
return 0;
