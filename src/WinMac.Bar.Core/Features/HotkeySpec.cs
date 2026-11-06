namespace WinMac.Bar.Features;

/// <summary>Combinación de teclas global («Ctrl+Alt+Up»). Pura: se prueba sin Windows. Siempre lleva al menos Ctrl o Alt (nunca una tecla suelta ni solo Mayús) y nunca usa Win.</summary>
public readonly record struct HotkeySpec(uint Mods, uint Vk)
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4;

    private static readonly Dictionary<string, uint> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27, ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Home"] = 0x24, ["End"] = 0x23,
        ["Espacio"] = 0x20, ["Intro"] = 0x0D, ["Supr"] = 0x2E, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Backspace"] = 0x08,
    };

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default; if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Ctrl; continue;
                case "alt": mods |= Alt; continue;
                case "shift" or "mayús" or "mayus": mods |= Shift; continue;
            }

            if (vk != 0) return false;                                   // dos teclas principales
            if (Names.TryGetValue(raw, out var n)) vk = n;
            else if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0])) vk = char.ToUpperInvariant(raw[0]);
            else if (raw.Length is 2 or 3 && (raw[0] is 'F' or 'f') && int.TryParse(raw.AsSpan(1), out int fn) && fn is >= 1 and <= 24) vk = (uint)(0x6F + fn);
            else return false;
        }

        if (vk == 0 || (mods & (Ctrl | Alt)) == 0) return false;        // exige Ctrl o Alt: una tecla suelta o solo Mayús secuestraría la escritura
        spec = new HotkeySpec(mods, vk); return true;
    }

    /// <summary>
    /// Combinación a partir de una pulsación real (captura de teclado en Ajustes). Null si es solo un modificador, si no lleva Ctrl o Alt, o si la tecla no es una que Hatsu sepa registrar
    /// (letras, cifras, F1–F24, flechas, Espacio, Intro, Tab, Esc, Inicio/Fin, RePág/AvPág, Insertar/Supr, Retroceso).
    /// </summary>
    public static HotkeySpec? FromKey(uint vk, bool ctrl, bool alt, bool shift)
    {
        bool supported = vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 || Names.ContainsValue(vk);
        if (!supported || (!ctrl && !alt)) return null;
        uint mods = (ctrl ? Ctrl : 0) | (alt ? Alt : 0) | (shift ? Shift : 0);
        return new HotkeySpec(mods, vk);
    }

    /// <summary>Texto canónico (el que se guarda): «Ctrl+Alt+Up».</summary>
    public string Format()
    {
        var parts = new List<string>();
        if ((Mods & Ctrl) != 0) parts.Add("Ctrl"); if ((Mods & Alt) != 0) parts.Add("Alt"); if ((Mods & Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(Vk)); return string.Join('+', parts);
    }

    /// <summary>Texto para mostrar al usuario («Ctrl+Alt+↑»).</summary>
    public string Display()
    {
        var s = Format();
        return s.Replace("Shift", "Mayús").Replace("+Up", "+↑").Replace("+Down", "+↓").Replace("+Left", "+←").Replace("+Right", "+→").Replace("+Space", "+Espacio").Replace("+Enter", "+Intro");
    }

    private static string KeyName(uint vk)
    {
        foreach (var (k, v) in Names) if (v == vk && k is not ("Espacio" or "Intro" or "Supr")) return k;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        return "0x" + vk.ToString("X");
    }

    /// <summary>Combinaciones que Windows se reserva (documentadas): nunca se registran. Devuelve quién las usa, o null.</summary>
    public string? ReservedBy() => Format() switch
    {
        "Alt+Space" => "el menú de ventana de Windows", "Alt+Tab" => "Alt+Tab de Windows", "Ctrl+Alt+Delete" => "la seguridad de Windows", "Alt+F4" => "cerrar ventana",
        "Ctrl+Shift+Esc" => "el Administrador de tareas", "Ctrl+Esc" => "el menú Inicio", "Alt+Esc" => "cambiar de ventana", "Ctrl+Alt+Tab" => "Alt+Tab fijo de Windows",
        "Alt+Enter" => "propiedades", _ => null,
    };
}
