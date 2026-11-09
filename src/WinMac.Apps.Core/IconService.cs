using System.Collections.Concurrent;
using System.Threading.Channels;

namespace WinMac.Apps;

/// <summary>Proveedor de iconos de la plataforma. Debe ser seguro entre hilos. Puede tardar o fallar: <see cref="IconService"/> lo aísla.</summary>
public interface IIconProvider
{
    /// <summary>Sello del origen (p. ej. tamaño+fecha del ejecutable) para invalidar la caché de disco. "" si no aplica.</summary>
    string Stamp(IconRequest request);
    IconBitmap? Load(IconRequest request);
}

public sealed record IconStats(int Loaded, int Failed, int TimedOut, int Rejected, int DiskHits, int Pending);

/// <summary>
/// Servicio de iconos NO bloqueante y reutilizable (Dock hoy; Spotlight y Launchpad después).
///  · <see cref="TryGet"/> devuelve al instante lo que haya en memoria o en disco; si no, encola la carga y devuelve null (el llamador dibuja su reemplazo).
///  · Cada carga corre en segundo plano con TIEMPO LÍMITE; una excepción, un icono corrupto o un bloqueo del Shell marcan el fallo, nunca bloquean la interfaz.
///  · Los fallos se recuerdan un tiempo (caché negativa) para no reintentar en cada frame.
/// </summary>
public sealed class IconService : IDisposable
{
    private sealed class Entry { public IconBitmap? Bmp; public bool Failed; public DateTime RetryAt; public bool Queued; public TaskCompletionSource<IconBitmap?> Done = new(TaskCreationOptions.RunContinuationsAsynchronously); }

    private readonly IIconProvider _provider; private readonly IconCache _cache; private readonly TimeSpan _timeout, _retry; private readonly Func<DateTime> _now;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(); private readonly Channel<(IconRequest Req, Entry E)> _queue = Channel.CreateUnbounded<(IconRequest, Entry)>();
    private readonly CancellationTokenSource _cts = new(); private readonly List<Task> _workers = new();
    private int _loaded, _failed, _timedOut, _rejected, _disk, _pending;

    /// <summary>Se produce en un hilo de fondo cuando un icono queda disponible.</summary>
    public event Action<IconRequest>? Loaded;

    /// <summary>Un icono no se pudo obtener: la petición y el motivo («tiempo agotado», «error: …», «rechazado: …»). Para el registro; el Dock usa el mosaico de reemplazo.</summary>
    public event Action<IconRequest, string>? Failed;

    public IconService(IIconProvider provider, IconCache cache, TimeSpan? loadTimeout = null, TimeSpan? retryAfter = null, int workers = 2, Func<DateTime>? clock = null)
    {
        _provider = provider; _cache = cache; _timeout = loadTimeout ?? TimeSpan.FromSeconds(4); _retry = retryAfter ?? TimeSpan.FromMinutes(5); _now = clock ?? (() => DateTime.UtcNow);
        for (int i = 0; i < Math.Max(1, workers); i++) _workers.Add(Task.Run(() => Worker(_cts.Token)));
    }

    public IconStats Stats => new(_loaded, _failed, _timedOut, _rejected, _disk, _pending);

    public IconBitmap? TryGet(IconRequest req)
    {
        var e = _entries.GetOrAdd(req.Key, _ => new Entry());
        if (e.Bmp is not null) return e.Bmp;
        if (e.Failed && _now() < e.RetryAt) return null;
        Enqueue(req, e); return null;
    }

    public Task<IconBitmap?> GetAsync(IconRequest req)
    {
        var e = _entries.GetOrAdd(req.Key, _ => new Entry());
        if (e.Bmp is not null) return Task.FromResult<IconBitmap?>(e.Bmp);
        if (e.Failed && _now() < e.RetryAt) return Task.FromResult<IconBitmap?>(null);
        Enqueue(req, e); return e.Done.Task;
    }

    /// <summary>Olvida lo cargado y fallado de una app (p. ej. tras cambiar su icono alternativo o actualizarse).</summary>
    public void Invalidate(AppIdentity app)
    {
        foreach (var k in _entries.Keys.Where(k => k.StartsWith(app.Id + "|", StringComparison.Ordinal)).ToList()) { _entries.TryRemove(k, out _); _cache.Remove(k); }
    }

    private void Enqueue(IconRequest req, Entry e)
    {
        lock (e) { if (e.Queued) return; e.Queued = true; if (e.Failed) { e.Failed = false; e.Done = new(TaskCreationOptions.RunContinuationsAsynchronously); } }
        Interlocked.Increment(ref _pending); _queue.Writer.TryWrite((req, e));
    }

    private async Task Worker(CancellationToken ct)
    {
        try { await foreach (var (req, e) in _queue.Reader.ReadAllAsync(ct)) { try { await Process(req, e, ct); } catch (OperationCanceledException) { throw; } catch (Exception x) { Fail(req, e, ref _failed, "error: " + x.GetType().Name); } finally { Interlocked.Decrement(ref _pending); lock (e) e.Queued = false; } } }
        catch (OperationCanceledException) { }
    }

    private async Task Process(IconRequest req, Entry e, CancellationToken ct)
    {
        string stamp; try { stamp = _provider.Stamp(req); } catch (Exception) { stamp = ""; }
        var disk = _cache.GetDisk(req.Key, stamp);
        if (disk is not null) { Interlocked.Increment(ref _disk); Succeed(req, e, disk); return; }

        var work = Task.Run(() => _provider.Load(req), ct);
        IconBitmap? bmp;
        try { bmp = await work.WaitAsync(_timeout, ct); }
        catch (TimeoutException) { Fail(req, e, ref _timedOut, $"tiempo agotado ({_timeout.TotalSeconds:0} s)", soon: true); return; }   // el Shell puede quedarse colgado (red, disco, arranque de sesión): se reintenta pronto
        catch (OperationCanceledException) { throw; }
        catch (Exception x) { Fail(req, e, ref _failed, "error: " + x.GetType().Name + ": " + x.Message); return; }

        if (IconValidator.Reject(bmp) is { } why) { Fail(req, e, ref _rejected, "rechazado: " + why); return; }
        _cache.PutDisk(req.Key, stamp, bmp!); Succeed(req, e, bmp!);
    }

    private void Succeed(IconRequest req, Entry e, IconBitmap bmp)
    {
        _cache.PutMemory(req.Key, bmp); e.Bmp = bmp; Interlocked.Increment(ref _loaded); e.Done.TrySetResult(bmp);
        try { Loaded?.Invoke(req); } catch (Exception) { }
    }

    /// <summary>Reintento tras un tiempo agotado: el Shell suele estar solo lento (p. ej. al iniciar sesión), y 5 min sin icono es demasiado.</summary>
    public static readonly TimeSpan TimeoutRetry = TimeSpan.FromSeconds(30);

    private void Fail(IconRequest req, Entry e, ref int counter, string why, bool soon = false)
    {
        Interlocked.Increment(ref counter); e.Failed = true; e.RetryAt = _now() + (soon && TimeoutRetry < _retry ? TimeoutRetry : _retry); e.Done.TrySetResult(null);
        try { Failed?.Invoke(req, why); } catch (Exception) { }
    }

    public void Dispose() { _cts.Cancel(); _queue.Writer.TryComplete(); }
}
