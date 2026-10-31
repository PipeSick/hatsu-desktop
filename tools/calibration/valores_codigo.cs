#:project ../../src/WinMac.Dock.Core/WinMac.Dock.Core.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
// Imprime en JSON los valores ACTUALES de DockMetrics y GlassStyle (propiedades públicas de tipo double, incluidas las derivadas) leyéndolos del propio código.
// Fuente única de verdad: nada se transcribe a mano. Uso: dotnet run tools/calibration/valores_codigo.cs
using System.Reflection;
using System.Text.Json;
using WinMac.Dock;
static Dictionary<string, double> Dump(object o) => o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.PropertyType == typeof(double)).ToDictionary(p => p.Name, p => (double)p.GetValue(o)!);
var m = new DockMetrics();
Console.WriteLine(JsonSerializer.Serialize(new { metrics = Dump(m), glass_light = Dump(GlassStyle.For(DockTheme.Light)), glass_dark = Dump(GlassStyle.For(DockTheme.Dark)),
    shadow_edge_light = ShadowMath.Cumulative(ShadowMath.LayerAlphas(12, GlassStyle.For(DockTheme.Light).ShadowAlpha), 1),
    shadow_edge_dark = ShadowMath.Cumulative(ShadowMath.LayerAlphas(12, GlassStyle.For(DockTheme.Dark).ShadowAlpha), 1) }));
