// Módulo de mentira para probar el host sin Dock ni Barra.
//   --id <nombre>   identificador del módulo (bar, dock…)
//   --mode normal | crash | exit-clean | hang | never-ready | ignore-shutdown
//   --out <carpeta> escribe <id>.log con una línea por hecho (arrancó, shutdown, bye…)
// Sin --hosted funciona «solo» (como Dock/Barra sin host). Se autotermina a los 120 s para no dejar procesos huérfanos si una prueba falla.
using WinMac.Ipc;

static string? Arg(string[] a, string n) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

string id = Arg(args, "--id") ?? "fake", mode = Arg(args, "--mode") ?? "normal"; string? outDir = Arg(args, "--out");
void Mark(string what)
{
    if (outDir is null) return;
    try { Directory.CreateDirectory(outDir); File.AppendAllText(Path.Combine(outDir, id + ".log"), $"{DateTime.UtcNow:O} {Environment.ProcessId} {what}\n"); } catch (IOException) { }
}

using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(120));
Mark("started");
var link = ModuleHostLink.FromArgs(args, id, "test");
var shutdown = new TaskCompletionSource();

async Task WaitConnectedAsync() { for (int i = 0; i < 500 && link is not null && !link.IsConnected; i++) await Task.Delay(10); }
async Task SleepForeverAsync() { try { await Task.Delay(Timeout.Infinite, safety.Token); } catch (OperationCanceledException) { } }

if (link is null) { Mark("standalone"); await SleepForeverAsync(); return 0; }
link.ShutdownRequested += () => { Mark("shutdown"); if (mode != "ignore-shutdown") shutdown.TrySetResult(); };

switch (mode)
{
    case "never-ready": await SleepForeverAsync(); return 0;                         // vive pero no se conecta: debe vencer el tiempo de «listo»
    case "crash": link.Start(); await WaitConnectedAsync(); await Task.Delay(150); Mark("crash"); Environment.Exit(7); return 7;
    case "exit-clean": link.Start(); await WaitConnectedAsync(); await Task.Delay(150); Mark("exit-clean"); await link.ByeAsync(TimeSpan.FromSeconds(1)); return 0;
    case "hang": link.Start(); await WaitConnectedAsync(); await Task.Delay(150); await link.DisposeAsync(); Mark("hang"); await SleepForeverAsync(); return 0;   // se calla: no contesta a los latidos
    default:
        link.Start();
        await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, safety.Token).ContinueWith(_ => { }));
        Mark("bye"); await link.ByeAsync(TimeSpan.FromSeconds(2)); return 0;
}
