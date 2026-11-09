namespace WinMac.Bar.Cleanup;

public enum CleanupKind
{
    /// <summary>Carpeta con archivos que se pueden eliminar tras confirmar (con filtro de antigüedad).</summary>
    Folder,
    /// <summary>Papelera: tamaño por la API del shell y vaciado SOLO con una segunda confirmación explícita.</summary>
    RecycleBin,
    /// <summary>Solo informativa: no se puede eliminar desde WinMac (requiere permisos elevados o revisión humana).</summary>
    ReviewOnly,
}

/// <param name="MinAge">Solo archivos sin modificar desde hace al menos esto (evita lo que sigue en uso).</param>
/// <param name="Patterns">Si no es null, solo archivos con esas extensiones (p. ej. «.log»).</param>
public sealed record CleanupCategory(string Id, string Title, string Reason, CleanupKind Kind, string[] Roots, TimeSpan MinAge, string[]? Extensions = null, string? WhyNotCleanable = null);

/// <summary>Un archivo concreto encontrado en el análisis, con lo que se vio (para revalidar antes de borrar).</summary>
public sealed record CleanItem(string Path, long Length, DateTime LastWriteUtc);

public enum SkipReason { TooRecent, Reparse, Locked, Denied, Changed, Vanished, OutOfScope, Protected }

public sealed record ScanResult(string CategoryId, IReadOnlyList<CleanItem> Items, long TotalBytes, IReadOnlyDictionary<SkipReason, int> Skipped, bool Truncated, bool Failed, string? Error)
{
    public static ScanResult Empty(string id, string? error = null) => new(id, [], 0, new Dictionary<SkipReason, int>(), false, error is not null, error);
}

public sealed record CleanResult(string CategoryId, int Deleted, long FreedBytes, IReadOnlyDictionary<SkipReason, int> Skipped, bool Cancelled)
{
    public int SkippedCount => Skipped.Values.Sum();
}

/// <summary>Entrada del sistema de archivos tal como la necesita el limpiador (sin seguir enlaces).</summary>
public sealed record FsEntry(string Path, bool IsDirectory, bool IsReparse, long Length, DateTime LastWriteUtc);

public enum DeleteOutcome { Deleted, Locked, Denied, Vanished }

/// <summary>Abstracción del sistema de archivos: permite probar enlaces, bloqueos y cambios sin tocar el disco.</summary>
public interface IFileSystem
{
    /// <summary>Hijos directos de un directorio, SIN seguir enlaces simbólicos ni junctions (<see cref="FsEntry.IsReparse"/> los marca). Lanza si no se puede leer.</summary>
    IEnumerable<FsEntry> Children(string dir);
    /// <summary>Estado actual de una ruta, o null si ya no existe.</summary>
    FsEntry? Stat(string path);
    DeleteOutcome DeleteFile(string path);
    /// <summary>Elimina un directorio SOLO si está vacío.</summary>
    bool DeleteEmptyDirectory(string path);
}

public interface IRecycleBin
{
    /// <summary>Tamaño y número de elementos, o null si Windows no lo informa.</summary>
    (long Bytes, long Items)? Query();
    bool Empty();
}
