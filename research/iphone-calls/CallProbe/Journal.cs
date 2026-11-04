using System.Text.Json;
using CallProbe.Logic;

namespace CallProbe;

/// <summary>Diario de la etapa 3 (Documentos\CallProbe\stage3-journal.json): guarda un hash del transporte, nunca su identificador ni nombres.</summary>
internal static class Journal
{
    private static string PathOf => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CallProbe", "stage3-journal.json");

    public static JournalState? Load() { try { return File.Exists(PathOf) ? JsonSerializer.Deserialize<JournalState>(File.ReadAllText(PathOf)) : null; } catch (Exception) { return null; } }

    public static void Save(JournalState s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf)!); var tmp = PathOf + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(s)); File.Move(tmp, PathOf, true);   // escritura atómica: un corte no deja medio archivo
    }
}
