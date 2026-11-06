using System.Runtime.InteropServices;

namespace WinMac.Host.App;

/// <summary>
/// Aspecto de la barra de título de las ventanas propias del host (Ajustes, asistente): el icono de Hatsu (el del ejecutable, no el genérico de Windows) y, en Windows 11, la barra de título y el borde
/// con el color de la ventana para que no parezcan un marco ajeno. Todo es opcional: en Windows anteriores los atributos no existen y se ignoran sin efecto.
/// </summary>
internal static class WindowChrome
{
    private const uint WM_SETICON = 0x80;

    public static void Apply(IntPtr hwnd, bool dark, int sideRgb)
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                var large = new IntPtr[1]; var small = new IntPtr[1];
                if (ExtractIconExW(exe, 0, large, small, 1) >= 1)
                {
                    if (large[0] != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)1, large[0]);
                    if (small[0] != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, IntPtr.Zero, small[0]);
                }
            }

            int caption = Colorref(sideRgb), text = dark ? 0x00FFFFFF : 0x00000000;
            Native.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
            Native.DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref caption, 4);
            Native.DwmSetWindowAttribute(hwnd, 36 /* DWMWA_TEXT_COLOR */, ref text, 4);
        }
        catch (Exception) { /* sin personalización: queda la barra de título normal */ }
    }

    /// <summary>0xRRGGBB → COLORREF (0x00BBGGRR).</summary>
    public static int Colorref(int rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
