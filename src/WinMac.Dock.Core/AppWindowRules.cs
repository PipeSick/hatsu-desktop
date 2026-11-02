namespace WinMac.Dock;

/// <summary>Lo que se sabe de una ventana de nivel superior para decidir si es una «ventana de app» (las que el Dock muestra), parecido a Alt+Tab.</summary>
public readonly record struct WindowFacts(bool Visible, bool Cloaked, bool Minimized, long ExStyle, bool HasOwner, string ClassName, bool HasTitle);

/// <summary>
/// Regla pura (se prueba en Linux) de qué ventanas cuentan como apps en el Dock.
/// <para>
/// Una ventana minimizada de verdad SIGUE siendo visible (conserva WS_VISIBLE). La regla anterior se saltaba la comprobación de visibilidad para las minimizadas y dejaba pasar
/// ventanas internas OCULTAS que el sistema marca como minimizadas: el usuario vio en el Dock el «Administrador de ventanas del escritorio» (dwm.exe) con una
/// «DWM Notification Window» «minimizada». Ahora la visibilidad se exige siempre; a las minimizadas solo se les perdona el estado «cloaked».
/// </para>
/// </summary>
public static class AppWindowRules
{
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "WorkerW", "Windows.UI.Core.CoreWindow" };

    public static bool IsAppWindow(WindowFacts w)
    {
        if (!w.Visible || !w.HasTitle) return false;
        if (w.Cloaked && !w.Minimized) return false;   // otra escritorio virtual, app suspendida… (una minimizada puede seguir siendo válida)
        bool appWindow = (w.ExStyle & WS_EX_APPWINDOW) != 0;
        if ((w.ExStyle & WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;   // ventanas de herramientas
        if (w.HasOwner && !appWindow) return false;                             // diálogos y ventanas propiedad de otra
        return !ShellClasses.Contains(w.ClassName);
    }
}
