using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.Host.App;

/// <summary>
/// Job Object con <c>KILL_ON_JOB_CLOSE</c>: si el host muere por cualquier causa (incluso <c>taskkill /F</c>), Windows cierra a todos los procesos asignados.
/// Es lo que garantiza «sin procesos huérfanos». Los módulos se asignan justo tras lanzarse.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9; private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)] private struct BASIC { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IOC { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct EXTENDED { public BASIC Basic; public IOC Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObjectW(IntPtr attrs, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private IntPtr _job;

    public JobObject()
    {
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) throw new InvalidOperationException("CreateJobObject falló: " + Marshal.GetLastWin32Error());
        var info = new EXTENDED { Basic = new BASIC { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
        int size = Marshal.SizeOf<EXTENDED>(); IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, p, false);
            if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, p, (uint)size)) throw new InvalidOperationException("SetInformationJobObject falló: " + Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    public void Assign(Process p)
    {
        if (!AssignProcessToJobObject(_job, p.Handle)) throw new InvalidOperationException("AssignProcessToJobObject falló: " + Marshal.GetLastWin32Error());
    }

    /// <summary>Cerrar el manejador cierra a los procesos que sigan dentro del job (en un cierre normal ya han salido todos).</summary>
    public void Dispose() { if (_job != IntPtr.Zero) { CloseHandle(_job); _job = IntPtr.Zero; } }
}
