namespace CallProbe;

/// <summary>
/// Para la prueba H3 (¿cambia algo si el proceso nace por ACTIVACIÓN normal del paquete en vez de Invoke-CommandInDesktopPackage?):
/// una activación desde el shell no admite argumentos, así que si no hay ninguno se lee UNA línea de Documentos\CallProbe\command.txt y el archivo se borra
/// (un doble clic perdido no repite nada). Solo se aceptan órdenes de etapas 1–3; las etapas 4–5 (llamadas reales) NUNCA se lanzan por este camino.
/// Las órdenes con efecto siguen exigiendo escribir la palabra de confirmación.
/// </summary>
internal static class CommandFile
{
    public static string[] Resolve(string[] args)
    {
        if (args.Length > 0) return args;
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CallProbe", "command.txt");
            if (!File.Exists(path)) return args;
            string line = File.ReadAllText(path).Trim(); File.Delete(path);
            return CallProbe.Logic.CommandPolicy.Parse(line);
        }
        catch { return args; }
    }
}
