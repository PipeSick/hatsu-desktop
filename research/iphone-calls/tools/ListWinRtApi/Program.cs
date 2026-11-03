using System.Reflection;
var dll = args[0]; var ns = args[1];
var core = typeof(object).Assembly.Location; var res = new PathAssemblyResolver(Directory.GetFiles(Path.GetDirectoryName(core)!, "*.dll").Append(dll));
using var mlc = new MetadataLoadContext(res);
var asm = mlc.LoadFromAssemblyPath(dll);
Type[] types; try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray()!; }
foreach (var t in types.Where(t => t.Namespace == ns).OrderBy(t => t.Name))
{
    Console.WriteLine($"{(t.IsEnum ? "enum" : t.IsInterface ? "interface" : "class")} {t.Name}");
    if (args.Length > 2 && args[2].Split(',').Contains(t.Name))
    {
        if (t.IsEnum) foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static)) Console.WriteLine("    = " + f.Name);
        else
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName)) Console.WriteLine($"    {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) Console.WriteLine($"    prop {p.PropertyType.Name} {p.Name}");
            foreach (var e in t.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) Console.WriteLine($"    event {e.Name}");
        }
    }
}
