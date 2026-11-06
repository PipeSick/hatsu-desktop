namespace WinMac.Bar.Vpn;

/// <summary>Una conexión VPN de Windows (Configuración › Red › VPN).</summary>
public sealed record VpnProfile(string Name, bool Connected, string? Phonebook = null);

/// <summary>Lectura de las VPN de Windows: perfiles y cuáles están conectadas. <see cref="Reason"/> explica por qué no hay datos.</summary>
public sealed record VpnState(bool Available, IReadOnlyList<VpnProfile> Profiles, string? Reason = null)
{
    public static VpnState None(string reason) => new(false, [], reason);
    public bool AnyConnected => Profiles.Any(p => p.Connected);
    public int ConnectedCount => Profiles.Count(p => p.Connected);
}

/// <summary>Servicio de VPN (implementado con la API pública RAS de Windows). Todas las llamadas pueden tardar: se invocan fuera del hilo de interfaz.</summary>
public interface IVpnService
{
    VpnState Read();
    /// <summary>Conecta el perfil con las credenciales que Windows ya guardó. Devuelve null si salió bien o el motivo del fallo.</summary>
    string? Connect(VpnProfile profile);
    string? Disconnect(VpnProfile profile);
}

/// <summary>Lo que el panel necesita: estado, el perfil en curso (conectando/desconectando) y un mensaje de error.</summary>
public sealed record VpnView(VpnState State, string? BusyName = null, string? BusyText = null, string? Message = null);

public static class VpnText
{
    public const string SettingsUri = "ms-settings:network-vpn";
    public static string Summary(VpnState s) => !s.Available ? "No disponible" : s.Profiles.Count == 0 ? "Sin conexiones VPN" : s.ConnectedCount > 0 ? (s.ConnectedCount == 1 ? "Conectada" : $"{s.ConnectedCount} conectadas") : "Desconectada";
    public static string Tip(VpnState s) => !s.Available || s.Profiles.Count == 0 ? "VPN" : s.ConnectedCount > 0 ? "VPN · conectada: " + string.Join(", ", s.Profiles.Where(p => p.Connected).Select(p => p.Name)) : "VPN · desconectada";
    /// <summary>Traduce los códigos de error de RAS más habituales a algo que el usuario entienda. Sin código conocido, el mensaje genérico con el número.</summary>
    public static string Explain(int code) => code switch
    {
        691 => "Credenciales rechazadas: abre Configuración › VPN, entra una vez con tu usuario y contraseña y vuelve a intentarlo.",
        623 => "Windows no encuentra el perfil (¿se borró?).",
        651 => "El módem o el adaptador VPN informó un error. Comprueba la conexión a internet.",
        678 or 800 or 809 => "El servidor VPN no respondió. Comprueba tu conexión o que el servidor esté disponible.",
        868 => "No se pudo resolver el nombre del servidor VPN.",
        1 or 87 => "Windows no pudo iniciar la conexión.",
        _ => $"Windows devolvió el error {code}. Abre Configuración › VPN para ver el detalle.",
    };
}

/// <summary>Lee las conexiones VPN de un archivo de agenda de RAS (<c>rasphone.pbk</c>, formato INI: una sección por conexión, <c>Type=2</c> = VPN). Puro: se prueba sin Windows.</summary>
public static class PhonebookParser
{
    public static IReadOnlyList<(string Name, string Phonebook)> Vpns(string text, string path)
    {
        var list = new List<(string, string)>(); string? name = null; bool vpn = false;
        void Flush() { if (name is not null && vpn && !list.Any(x => string.Equals(x.Item1, name, StringComparison.OrdinalIgnoreCase))) list.Add((name, path)); }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t', '\uFEFF');
            if (line.Length > 2 && line[0] == '[' && line[^1] == ']') { Flush(); name = line[1..^1]; vpn = false; }
            else if (name is not null && line.StartsWith("Type=", StringComparison.OrdinalIgnoreCase) && line[5..].Trim() == "2") vpn = true;
        }

        Flush(); return list;
    }
}
