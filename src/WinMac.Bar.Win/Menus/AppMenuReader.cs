using System.Runtime.InteropServices;
using WinMac.Bar.Menus;

namespace WinMac.Bar.Win.Menus;

/// <summary>
/// Lee los menús de la app activa SIN tocarla (opción A: los originales siguen en su ventana) y sin inyectar nada en su proceso:
/// <list type="number">
/// <item><b>Win32</b> (<c>HMENU</c>, apps clásicas): el árbol completo con su estado (deshabilitado, marcado), atajos y submenús. Antes de leer cada submenú se le avisa a la
/// app con <c>WM_INITMENUPOPUP</c> (como hace Windows al abrirlo; con tope de tiempo), para que actualice sus entradas. Las órdenes se ejecutan con <c>WM_COMMAND</c>.
/// Si la app dibuja sus menús (owner-draw) no se puede leer el texto: se usa el menú nativo de Windows de la propia app.</item>
/// <item><b>UI Automation</b> (WinUI, WPF, Electron/VS Code…: sin <c>HMENU</c>): los menús de nivel superior de su barra de menús accesible. Al elegir uno se le pide a la
/// app que lo abra (<c>ExpandCollapsePattern.Expand</c>): se despliega SU menú real, con sus acciones.</item>
/// </list>
/// Las apps sin ninguna de las dos (Office con cinta, la mayoría de navegadores) no muestran menús: solo su nombre. Nada se inventa.
/// </summary>
internal static class AppMenuReader
{
    private const uint MiimState = 0x1, MiimId = 0x2, MiimSubmenu = 0x4, MiimString = 0x40, MiimFtype = 0x100;
    private const uint MftSeparator = 0x800, MftOwnerDraw = 0x100, MftBitmap = 0x4, MfsDisabled = 0x3, MfsChecked = 0x8;

    // ───────────────────────────── Win32
    /// <summary>Árbol del menú de nivel superior <paramref name="index"/>; null si no se puede leer (owner-draw, la app no responde…).</summary>
    public static AppMenuEntry? ReadWin32(IntPtr appWindow, IntPtr subMenu, int index, string text)
    {
        try
        {
            int budget = 400;
            var children = ReadItems(appWindow, subMenu, index, 0, ref budget);
            return children is null ? null : new AppMenuEntry(text, 0, Children: children);
        }
        catch (Exception) { return null; }
    }

    private static List<AppMenuEntry>? ReadItems(IntPtr appWindow, IntPtr menu, int pos, int depth, ref int budget)
    {
        if (depth > 3 || budget <= 0) return [];
        SendMessageTimeoutW(appWindow, 0x117 /* WM_INITMENUPOPUP */, menu, (IntPtr)(pos & 0xFFFF), 0x2 /* SMTO_ABORTIFHUNG */, 250, out _);
        int n = GetMenuItemCount(menu); if (n < 0 || n > 200) return null;
        var list = new List<AppMenuEntry>(n);
        for (int i = 0; i < n && budget > 0; i++, budget--)
        {
            var mii = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = MiimFtype | MiimSubmenu | MiimString | MiimId | MiimState };
            if (!GetMenuItemInfoW(menu, (uint)i, true, ref mii)) return null;
            if ((mii.fType & (MftOwnerDraw | MftBitmap)) != 0) return null;   // la app dibuja sus menús: no se puede representar fielmente
            if ((mii.fType & MftSeparator) != 0) { list.Add(AppMenuEntry.Sep); continue; }
            string raw = Text(menu, (uint)i, mii.cch); if (string.IsNullOrWhiteSpace(raw)) continue;
            var (label, shortcut) = AppMenuEntry.ParseLabel(raw);
            bool enabled = (mii.fState & MfsDisabled) == 0, check = (mii.fState & MfsChecked) != 0;
            if (mii.hSubMenu != IntPtr.Zero)
            {
                var kids = ReadItems(appWindow, mii.hSubMenu, i, depth + 1, ref budget); if (kids is null) return null;
                list.Add(new AppMenuEntry(label, 0, enabled, check, Children: kids));
            }
            else list.Add(new AppMenuEntry(label, (int)mii.wID, enabled, check, Shortcut: shortcut));
        }

        return list;
    }

    private static string Text(IntPtr menu, uint pos, uint cch)
    {
        if (cch == 0 || cch > 512) return "";
        var buf = Marshal.AllocHGlobal((int)(cch + 1) * 2);
        try
        {
            var m = new MENUITEMINFO { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = MiimString, dwTypeData = buf, cch = cch + 1 };
            return GetMenuItemInfoW(menu, pos, true, ref m) ? Marshal.PtrToStringUni(buf) ?? "" : "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Ejecuta una orden del menú de la app, como si se hubiera elegido en su menú (la app la recibe por su cola de mensajes).</summary>
    public static void Run(IntPtr appWindow, int command)
    {
        SetForegroundWindow(appWindow);
        PostMessageW(appWindow, 0x111 /* WM_COMMAND */, (IntPtr)(command & 0xFFFF), IntPtr.Zero);
    }

    // ───────────────────────────── UI Automation (COM, sin dependencias)
    private const int UIA_ControlTypePropertyId = 30003, UIA_MenuBarControlTypeId = 50010, UIA_MenuItemControlTypeId = 50011, UIA_ExpandCollapsePatternId = 10005, UIA_InvokePatternId = 10000;
    private const int TreeScopeChildren = 2, TreeScopeDescendants = 4;

    /// <summary>Diagnóstico (CI, WINMAC_MENU_DIAG): qué barras de menús accesibles tiene la ventana.</summary>
    public static Action<string>? Diag { get; set; }

    /// <summary>Nombres de los menús de nivel superior de la barra de menús accesible de la ventana. Vacío si no tiene. Se llama fuera del hilo de la barra (puede tardar).</summary>
    public static IReadOnlyList<string> ReadAutomation(IntPtr hwnd) => WithMenuItems(hwnd, items => items.Select(i => i.Name).ToList()) ?? [];

    /// <summary>Abre el menú real de la app (el que está en su ventana) por UI Automation. false si ya no existe o no lo permite.</summary>
    public static bool ExpandAutomation(IntPtr hwnd, int index, string name)
    {
        return WithMenuItems(hwnd, items =>
        {
            var it = index >= 0 && index < items.Count && items[index].Name == name ? items[index] : items.FirstOrDefault(i => i.Name == name);
            if (it.Element is null) return false;
            SetForegroundWindow(hwnd);
            if (it.Element.GetCurrentPattern(UIA_ExpandCollapsePatternId, out var p) == 0 && p is IUIAutomationExpandCollapsePattern ec) { ec.Expand(); return true; }
            if (it.Element.GetCurrentPattern(UIA_InvokePatternId, out var q) == 0 && q is IUIAutomationInvokePattern inv) { inv.Invoke(); return true; }
            return false;
        });
    }

    private static T? WithMenuItems<T>(IntPtr hwnd, Func<List<(string Name, IUIAutomationElement Element)>, T> use)
    {
        IUIAutomation? uia = null;
        try
        {
            uia = (IUIAutomation)new CUIAutomation();
            if (uia.ElementFromHandle(hwnd, out var root) != 0 || root is null) return default;
            if (uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_MenuBarControlTypeId, out var barCond) != 0) return default;
            if (root.FindAll(TreeScopeDescendants, barCond, out var bars) != 0 || bars is null) return default;
            bars.get_Length(out int nb); IUIAutomationElement? bar = null;
            for (int b = 0; b < nb && b < 8 && bar is null; b++)   // la primera barra de menús que sea de la app (no el menú de sistema de la barra de título)
            {
                if (bars.GetElement(b, out var cand) != 0 || cand is null) continue;
                cand.get_CurrentAutomationId(out var aid); cand.get_CurrentName(out var bn); cand.get_CurrentClassName(out var bc);
                Diag?.Invoke($"[menús] barra accesible {b + 1}/{nb}: id «{aid}», nombre «{bn}», clase «{bc}»");
                if (WinMac.Bar.Menus.AppMenuFilter.IsAppMenuBar(aid)) bar = cand;
            }
            if (bar is null) return default;
            if (uia.CreatePropertyCondition(UIA_ControlTypePropertyId, UIA_MenuItemControlTypeId, out var itemCond) != 0) return default;
            if (bar.FindAll(TreeScopeChildren, itemCond, out var arr) != 0 || arr is null) return default;
            arr.get_Length(out int n);
            var list = new List<(string, IUIAutomationElement)>();
            for (int i = 0; i < n && i < 20; i++)
                if (arr.GetElement(i, out var e) == 0 && e is not null && e.get_CurrentName(out var name) == 0 && !string.IsNullOrWhiteSpace(name)) list.Add((name.Replace("&", "").Trim(), e));
            return use(list);
        }
        catch (Exception) { return default; }
        finally { if (uia is not null && Marshal.IsComObject(uia)) Marshal.ReleaseComObject(uia); }
    }

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] private class CUIAutomation { }

    // Tablas virtuales de UIAutomationClient.h (documentadas). Los huecos sin usar se declaran como marcadores para conservar el orden.
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void _CompareElements(); void _CompareRuntimeIds(); void _GetRootElement();
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, out IUIAutomationElement? element);
        void _ElementFromPoint(); void _GetFocusedElement(); void _GetRootElementBuildCache(); void _ElementFromHandleBuildCache(); void _ElementFromPointBuildCache(); void _GetFocusedElementBuildCache();
        void _CreateTreeWalker(); void _ControlViewWalker(); void _ContentViewWalker(); void _RawViewWalker(); void _RawViewCondition(); void _ControlViewCondition(); void _ContentViewCondition();
        void _CreateCacheRequest(); void _CreateTrueCondition(); void _CreateFalseCondition();
        [PreserveSig] int CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, [MarshalAs(UnmanagedType.IUnknown)] out object condition);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void _SetFocus(); void _GetRuntimeId();
        [PreserveSig] int FindFirst(int scope, [MarshalAs(UnmanagedType.IUnknown)] object condition, out IUIAutomationElement? found);
        [PreserveSig] int FindAll(int scope, [MarshalAs(UnmanagedType.IUnknown)] object condition, out IUIAutomationElementArray? found);
        void _FindFirstBuildCache(); void _FindAllBuildCache(); void _BuildUpdatedCache(); void _GetCurrentPropertyValue(); void _GetCurrentPropertyValueEx();
        void _GetCachedPropertyValue(); void _GetCachedPropertyValueEx(); void _GetCurrentPatternAs(); void _GetCachedPatternAs();
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
        void _GetCachedPattern(); void _GetCachedParent(); void _GetCachedChildren(); void _CurrentProcessId(); void _CurrentControlType(); void _CurrentLocalizedControlType();
        [PreserveSig] int get_CurrentName([MarshalAs(UnmanagedType.BStr)] out string? name);
        void _CurrentAcceleratorKey(); void _CurrentAccessKey(); void _CurrentHasKeyboardFocus(); void _CurrentIsKeyboardFocusable(); void _CurrentIsEnabled();
        [PreserveSig] int get_CurrentAutomationId([MarshalAs(UnmanagedType.BStr)] out string? id);
        [PreserveSig] int get_CurrentClassName([MarshalAs(UnmanagedType.BStr)] out string? cls);
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationElement? element);
    }

    [ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationExpandCollapsePattern { [PreserveSig] int Expand(); [PreserveSig] int Collapse(); }

    [ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationInvokePattern { [PreserveSig] int Invoke(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUITEMINFO { public uint cbSize, fMask, fType, fState, wID; public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData; public uint cch; public IntPtr hbmpItem; }

    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMenuItemInfoW(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO mii);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
}
