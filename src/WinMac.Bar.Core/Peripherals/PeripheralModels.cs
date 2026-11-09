namespace WinMac.Bar.Peripherals;

public enum DeviceKind { Mouse, Keyboard, Headset, Gamepad, Other }
public enum ConnectionKind { Bluetooth, Usb, Dongle, Unknown }
public enum ChargeState { Unknown, Charging, NotCharging }
/// <summary>¿El dispositivo/proveedor ofrece batería? Unknown = no se sabe (no se interpreta como «sin batería»).</summary>
public enum BatteryCapability { Unknown, Supported, Unsupported }
/// <summary>Estado de soporte de un proveedor: Experimental = implementado con la interfaz estándar pero NO verificado con tus dispositivos; NotAvailable = sin interfaz viable.</summary>
public enum SupportLevel { Confirmed, Experimental, NotAvailable }

/// <summary>Lo que UN proveedor dice de UN dispositivo en una lectura. Datos crudos: la normalización (frescura, validez) ocurre en <see cref="PeripheralStore"/>.</summary>
public sealed record RawReading(string DeviceId, string Name, DeviceKind Kind, ConnectionKind Connection, bool Connected, int? Percent, ChargeState Charge = ChargeState.Unknown,
    BatteryCapability Capability = BatteryCapability.Unknown, string? Note = null);

public enum ProviderStatus { Ok, TimedOut, Failed }

public sealed record ProviderResult(string ProviderId, ProviderStatus Status, IReadOnlyList<RawReading> Readings, string? Error = null)
{
    public static ProviderResult Fail(string id, ProviderStatus s, string? e = null) => new(id, s, [], e);
}

/// <summary>Un método de adquisición (Bluetooth estándar, HID…). Solo lee; no escribe nada en el dispositivo y no necesita permisos elevados.</summary>
public interface IPeripheralProvider
{
    string Id { get; }
    string DisplayName { get; }
    SupportLevel Support { get; }
    Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct);
}

/// <summary>Ejecuta un proveedor con plazo: un proveedor que no responde o falla NUNCA bloquea la interfaz ni a los demás; devuelve un resultado con su estado.</summary>
public static class ProviderRunner
{
    public static async Task<ProviderResult> RunAsync(IPeripheralProvider p, TimeSpan timeout, CancellationToken outer = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        Task<IReadOnlyList<RawReading>> work;
        try { work = Task.Run(() => p.ReadAsync(cts.Token), cts.Token); }
        catch (Exception e) { return ProviderResult.Fail(p.Id, ProviderStatus.Failed, e.GetType().Name); }
        var done = await Task.WhenAny(work, Task.Delay(timeout, outer));
        if (outer.IsCancellationRequested) { cts.Cancel(); return ProviderResult.Fail(p.Id, ProviderStatus.Failed, "cancelado"); }
        if (done != work)
        {
            cts.Cancel();
            _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // si termina tarde y falla, no queda sin observar
            return ProviderResult.Fail(p.Id, ProviderStatus.TimedOut);
        }

        try { return new ProviderResult(p.Id, ProviderStatus.Ok, await work); }
        catch (OperationCanceledException) { return ProviderResult.Fail(p.Id, ProviderStatus.TimedOut); }
        catch (Exception e) { return ProviderResult.Fail(p.Id, ProviderStatus.Failed, e.GetType().Name); }
    }
}
