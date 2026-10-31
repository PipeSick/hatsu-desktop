using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;

namespace WinMac.Ipc;

public static class PipeNames
{
    /// <summary>Nombre del canal del usuario actual y de SU sesión de Windows: <c>WinMac.&lt;SID&gt;.&lt;sesión&gt;</c> (así dos sesiones del mismo usuario, p. ej. escritorio remoto, no se mezclan).</summary>
    public static string ForCurrentUser() =>
        "WinMac." + (OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName : Environment.UserName) + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;

    /// <summary>Token de sesión de 256 bits (base64 URL).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

internal static class PeerInfo
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);

    /// <summary>PID del proceso cliente (solo Windows; null si no se puede saber).</summary>
    public static uint? ClientPid(NamedPipeServerStream s)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return GetNamedPipeClientProcessId(s.SafePipeHandle.DangerousGetHandle(), out uint pid) ? pid : null; } catch (Exception) { return null; }
    }
}
