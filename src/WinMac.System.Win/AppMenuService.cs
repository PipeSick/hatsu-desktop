using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.SystemControl.Win;

/// <summary>Un menú de nivel superior de otra app (EXPERIMENTAL): su texto y el <c>HMENU</c> de su submenú.</summary>
public sealed record AppMenuTop(string Text, IntPtr SubMenu, int Index);

/// <summary>
/// Menús de la app activa, EXPERIMENTAL y opcional. Solo CONSULTA la barra de menús estándar (<c>HMENU</c>) de apps Win32 clásicas; no la oculta ni la modifica, así que el menú original sigue
/// ahí. Para mostrar un submenú se avisa a la app (<c>WM_INITMENUPOPUP</c>, como haría Windows) y se ejecuta la orden elegida con <c>WM_COMMAND</c>. No funciona con apps de WinUI/UWP, Chromium
/// ni cinta de Office (no tienen <c>HMENU</c>), y se descartan las que dibujan sus menús ellas mismas (owner-draw), para no mostrar basura.
/// </summary>
public static class AppMenuService
{
    private const uint MiimState = 0x1, MiimId = 0x2, MiimSubmenu = 0x4, MiimString = 0x40, MiimFtype = 0x100, MftOwnerDraw = 0x100, MftBitmap = 0x4;

    public static IReadOnlyList<AppMenuTop> Read(IntPtr hwnd)
    {
        var result = new List<AppMenuTop>();
        try
        {
            IntPtr menu = Native.GetMenu(hwnd); if (menu == IntPtr.Zero) return result;
            int n = Native.GetMenuItemCount(menu); if (n <= 0 || n > 20) return result;
            for (int i = 0; i < n; i++)
            {
                var info = Info(menu, (uint)i, out string text); if (info is null) return [];   // algo raro: ningún menú antes que uno roto
                if ((info.Value.fType & (MftOwnerDraw | MftBitmap)) != 0) return [];            // la app dibuja sus menús: no tocar
                if (info.Value.hSubMenu == IntPtr.Zero || string.IsNullOrWhiteSpace(text)) continue;   // sin submenú (comando directo) o separador
                if (HasOwnerDraw(info.Value.hSubMenu)) return [];
                result.Add(new AppMenuTop(text.Replace("&", ""), info.Value.hSubMenu, i));
            }
        }
        catch (Exception) { return []; }
        return result;
    }

    private static Native.MENUITEMINFO? Info(IntPtr menu, uint pos, out string text)
    {
        text = "";
        var mii = new Native.MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<Native.MENUITEMINFO>(), fMask = MiimFtype | MiimSubmenu | MiimString | MiimId | MiimState };
        if (!Native.GetMenuItemInfoW(menu, pos, true, ref mii)) return null;
        if (mii.cch > 0 && mii.cch < 256 && (mii.fType & MftOwnerDraw) == 0)
        {
            var buf = Marshal.AllocHGlobal((int)(mii.cch + 1) * 2);
            try
            {
                var m2 = mii; m2.dwTypeData = buf; m2.cch++;
                if (Native.GetMenuItemInfoW(menu, pos, true, ref m2)) text = Marshal.PtrToStringUni(buf) ?? "";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        return mii;
    }

    private static bool HasOwnerDraw(IntPtr sub)
    {
        int n = Native.GetMenuItemCount(sub); if (n < 0 || n > 200) return true;
        for (int i = 0; i < n; i++)
        {
            var mii = new Native.MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<Native.MENUITEMINFO>(), fMask = MiimFtype };
            if (!Native.GetMenuItemInfoW(sub, (uint)i, true, ref mii)) return true;
            if ((mii.fType & (MftOwnerDraw | MftBitmap)) != 0) return true;
        }

        return false;
    }

    /// <summary>Muestra el submenú de la app con el menú nativo de Windows y ejecuta la orden elegida. <paramref name="owner"/> es una ventana NUESTRA (la barra).</summary>
    public static ServiceResult Show(IntPtr owner, IntPtr appWindow, AppMenuTop top, int x, int y) => ServiceResult.Guard(() =>
    {
        // La app actualiza el estado de sus entradas (marcas, deshabilitadas) al recibir esto, igual que cuando abres su menú.
        Native.SendMessageTimeoutW(appWindow, 0x117 /* WM_INITMENUPOPUP */, (UIntPtr)(ulong)top.SubMenu.ToInt64(), (IntPtr)(top.Index & 0xFFFF), 0x2, 300, out _);
        Native.SetForegroundWindow(owner);
        int cmd = Native.TrackPopupMenuEx(top.SubMenu, 0x100 | 0x80 | 0x2 /* RETURNCMD | NONOTIFY | RIGHTBUTTON */, x, y, owner, IntPtr.Zero);
        if (cmd > 0) { Native.SetForegroundWindow(appWindow); Native.PostMessageW(appWindow, 0x111 /* WM_COMMAND */, (UIntPtr)(uint)cmd, IntPtr.Zero); }
        return ServiceResult.Ok;
    }, "Menú de la app");

    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(128); Native.GetClassNameW(h, sb, 128); return sb.ToString(); }
}
