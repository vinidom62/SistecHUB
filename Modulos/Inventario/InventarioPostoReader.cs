using System.Management;

namespace SistecHub.Modulos.Inventario;

internal static class InventarioPostoReader
{
    static readonly string[] ModeloPlaceholderSubstrings =
    {
        "system product name",
        "default string",
        "to be filled",
        "not specified",
        "o.e.m.",
        "unknown",
    };

    public static PostoTrabalhoInventario ReadPostoTrabalho()
    {
        ushort? pcType = null;
        string? mfr = null;
        string? model = null;
        string? wmiUser = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Manufacturer, Model, PCSystemType, UserName FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    mfr = o["Manufacturer"]?.ToString()?.Trim();
                    model = o["Model"]?.ToString()?.Trim();
                    wmiUser = o["UserName"]?.ToString()?.Trim();
                    var raw = o["PCSystemType"];
                    if (raw is ushort u16)
                        pcType = u16;
                    else if (raw is int i32 && i32 is >= 0 and <= ushort.MaxValue)
                        pcType = (ushort)i32;
                }

                break;
            }
        }
        catch
        {
            // ignorar
        }

        var tipo = MapPcSystemType(pcType);
        if (tipo == "Desconhecido")
        {
            var porChassis = MapTipoFromChassis();
            if (!string.IsNullOrEmpty(porChassis) && porChassis != "Desconhecido")
                tipo = porChassis;
        }

        var modelo = BuildModeloComputador(mfr, model);
        var (user, domain, line) = ResolveUtilizadorDominio(wmiUser);

        return new PostoTrabalhoInventario(tipo, modelo, user, domain, line);
    }

    static string MapPcSystemType(ushort? type) =>
        type switch
        {
            1 => "Desktop",
            2 => "Portátil",
            3 => "Estação de trabalho",
            4 => "Servidor empresarial",
            5 => "Servidor SOHO",
            6 => "Appliance",
            7 => "Servidor de elevado desempenho",
            8 => "Outro",
            _ => "Desconhecido",
        };

    static string? MapTipoFromChassis()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT ChassisTypes FROM Win32_SystemEnclosure");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    var raw = o["ChassisTypes"];
                    ushort? first = null;
                    if (raw is ushort[] ua && ua.Length > 0)
                        first = ua[0];
                    else if (raw is int[] ia && ia.Length > 0 && ia[0] is >= 0 and <= ushort.MaxValue)
                        first = (ushort)ia[0];
                    else if (raw is ushort u)
                        first = u;

                    if (first is null)
                        return null;
                    return MapChassisCode(first.Value);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    static string MapChassisCode(ushort code) =>
        code switch
        {
            3 or 4 or 5 or 6 or 7 or 13 or 15 or 21 or 24 or 35 => "Desktop",
            8 or 9 or 10 or 11 or 12 or 14 or 30 or 31 or 32 => "Portátil",
            23 or 28 => "Servidor em rack",
            26 or 27 => "Multi-sistema",
            17 or 18 => "Sistema integrado",
            _ => "Desconhecido",
        };

    static string? BuildModeloComputador(string? manufacturer, string? model)
    {
        var mMfr = CleanOemField(manufacturer);
        var mModel = CleanOemField(model);

        if (mMfr is null && mModel is null)
            return null;
        if (mMfr is null)
            return mModel;
        if (mModel is null)
            return mMfr;
        if (mMfr.Equals(mModel, StringComparison.OrdinalIgnoreCase))
            return mMfr;
        return $"{mMfr} {mModel}";
    }

    static string? CleanOemField(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = s.Trim();
        foreach (var sub in ModeloPlaceholderSubstrings)
        {
            if (t.Contains(sub, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return t.Length == 0 ? null : t;
    }

    static (string Utilizador, string? Dominio, string LinhaCompleta) ResolveUtilizadorDominio(string? wmiUserName)
    {
        if (!string.IsNullOrWhiteSpace(wmiUserName))
        {
            var t = wmiUserName.Trim();
            var idx = t.IndexOf('\\');
            if (idx > 0 && idx < t.Length - 1)
            {
                var dom = t[..idx].Trim();
                var user = t[(idx + 1)..].Trim();
                if (user.Length > 0)
                    return (user, dom.Length > 0 ? dom : null, t);
            }
        }

        var u = Environment.UserName?.Trim() ?? "";
        var d = Environment.UserDomainName?.Trim();
        if (u.Length == 0)
            return ("", string.IsNullOrEmpty(d) ? null : d, d ?? "");
        if (string.IsNullOrEmpty(d))
            return (u, null, u);
        return (u, d, $"{d}\\{u}");
    }
}
