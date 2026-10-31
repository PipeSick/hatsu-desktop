namespace WinMac.Apps.Win;

/// <summary>Ejecuta trabajo de COM/Shell en un hilo STA propio y descartable: si el Shell se cuelga, solo se pierde ese hilo (de fondo), nunca la interfaz.</summary>
internal static class StaRunner
{
    public static T? Run<T>(Func<T?> work)
    {
        T? result = default; Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception e) { error = e; } }) { IsBackground = true, Name = "WinMac.Sta" };
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return result;
    }
}
