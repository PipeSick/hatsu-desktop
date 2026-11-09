using System.ComponentModel;
using Xunit;

namespace WinMac.Host.Tests;

/// <summary>WM-036: el motivo real de un módulo que no arranca, y qué se reintenta.</summary>
public class LaunchDiagnosticsTests
{
    [Fact]
    public void Security_blocks_are_named_by_their_windows_code_and_retried_but_a_missing_file_is_not()
    {
        var sac = new Win32Exception(LaunchDiagnostics.ErrorAppControlBlocked);
        Assert.Contains("Control de aplicaciones", LaunchDiagnostics.Explain(sac)); Assert.Contains("[error 4551]", LaunchDiagnostics.Explain(sac));
        Assert.True(LaunchDiagnostics.Retryable(sac));   // antes el módulo quedaba «Fallado» hasta reiniciar WinMac
        Assert.Contains("antivirus", LaunchDiagnostics.Explain(new Win32Exception(LaunchDiagnostics.ErrorVirusInfected)));
        Assert.Contains("Integridad de código", LaunchDiagnostics.Explain(new Win32Exception(LaunchDiagnostics.ErrorInvalidImageHash)));
        Assert.False(LaunchDiagnostics.Retryable(new FileNotFoundException("x")));
        Assert.False(LaunchDiagnostics.Retryable(new Win32Exception(LaunchDiagnostics.ErrorFileNotFound)));
    }

    [Fact]
    public void Exit_codes_of_blocked_libraries_are_explained()
    {
        Assert.Contains("bloqueó una biblioteca", LaunchDiagnostics.ExitText(unchecked((int)0xC0000428)));
        Assert.Contains("falta una biblioteca", LaunchDiagnostics.ExitText(unchecked((int)0xC0000135)));
        Assert.Equal("código 1", LaunchDiagnostics.ExitText(1));
        Assert.Equal("código 0xFFFFFFFF", LaunchDiagnostics.ExitText(-1));
    }
}
