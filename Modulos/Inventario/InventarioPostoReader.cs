using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

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
        var numeroSerie = ReadBiosSerialNumber();
        var (user, domain, line) = ResolveUtilizadorDominio(wmiUser);

        return new PostoTrabalhoInventario(tipo, modelo, numeroSerie, user, domain, line);
    }

    static string? ReadBiosSerialNumber()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT SerialNumber FROM Win32_BIOS");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    var sn = o["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(sn))
                        return sn;
                }

                break;
            }
        }
        catch
        {
            // ignorar
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
            {
                using (o)
                {
                    var sn = o["IdentifyingNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(sn))
                        return sn;
                }

                break;
            }
        }
        catch
        {
            // ignorar
        }

        return null;
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
        // Com a tela bloqueada, Win32_ComputerSystem.UserName passa a ser DOMINIO\HOSTNAME$.
        if (TryNormalizeUser(wmiUserName, out var fromWmi))
            return fromWmi;

        if (TryGetInteractiveSessionUser(out var fromSession))
            return fromSession;

        if (TryGetLastLoggedOnUser(out var fromLogonUi))
            return fromLogonUi;

        return FromEnvironmentOrEmpty();
    }

    static bool TryNormalizeUser(string? raw, out (string Utilizador, string? Dominio, string LinhaCompleta) result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var t = raw.Trim();
        string user;
        string? domain = null;

        var idx = t.IndexOf('\\');
        if (idx > 0 && idx < t.Length - 1)
        {
            domain = t[..idx].Trim();
            user = t[(idx + 1)..].Trim();
            if (domain.Length == 0)
                domain = null;
        }
        else
        {
            user = t;
        }

        if (user.Length == 0 || IsComputerOrServiceAccount(user))
            return false;

        result = (user, domain, domain is null ? user : $"{domain}\\{user}");
        return true;
    }

    static bool IsComputerOrServiceAccount(string user)
    {
        if (user.EndsWith("$", StringComparison.Ordinal))
            return true;

        var machine = Environment.MachineName?.Trim() ?? "";
        if (machine.Length > 0 && user.Equals(machine, StringComparison.OrdinalIgnoreCase))
            return true;

        return user.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
            || user.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase)
            || user.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase)
            || user.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);
    }

    static (string Utilizador, string? Dominio, string LinhaCompleta) FromEnvironmentOrEmpty()
    {
        var u = Environment.UserName?.Trim() ?? "";
        var d = Environment.UserDomainName?.Trim();
        if (u.Length == 0 || IsComputerOrServiceAccount(u))
            return ("", null, "");
        if (string.IsNullOrEmpty(d))
            return (u, null, u);
        return (u, d, $"{d}\\{u}");
    }

    static bool TryGetInteractiveSessionUser(out (string Utilizador, string? Dominio, string LinhaCompleta) result)
    {
        result = default;
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var consoleSession = WTSGetActiveConsoleSessionId();
            if (consoleSession is not 0xFFFFFFFF and not 0
                && TryQuerySessionUser(consoleSession, out result))
                return true;

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessionInfo, out var count)
                || sessionInfo == IntPtr.Zero)
                return false;

            try
            {
                (string Utilizador, string? Dominio, string LinhaCompleta)? fallback = null;
                var iter = sessionInfo;
                for (var i = 0; i < count; i++)
                {
                    var session = Marshal.PtrToStructure<WTS_SESSION_INFO>(iter);
                    iter = IntPtr.Add(iter, Marshal.SizeOf<WTS_SESSION_INFO>());
                    if (session.SessionId is 0)
                        continue;
                    if (session.State is not (
                        WTS_CONNECTSTATE_CLASS.WTSActive
                        or WTS_CONNECTSTATE_CLASS.WTSConnected
                        or WTS_CONNECTSTATE_CLASS.WTSDisconnected))
                        continue;
                    if (!TryQuerySessionUser((uint)session.SessionId, out var candidate))
                        continue;

                    if (session.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        result = candidate;
                        return true;
                    }

                    fallback ??= candidate;
                }

                if (fallback is { } found)
                {
                    result = found;
                    return true;
                }
            }
            finally
            {
                WTSFreeMemory(sessionInfo);
            }
        }
        catch
        {
            // ignorar
        }

        return false;
    }

    static bool TryQuerySessionUser(
        uint sessionId,
        out (string Utilizador, string? Dominio, string LinhaCompleta) result)
    {
        result = default;
        var user = QuerySessionString(sessionId, WTS_INFO_CLASS.WTSUserName);
        if (string.IsNullOrWhiteSpace(user) || IsComputerOrServiceAccount(user))
            return false;

        var domain = QuerySessionString(sessionId, WTS_INFO_CLASS.WTSDomainName);
        if (string.IsNullOrWhiteSpace(domain))
            domain = null;

        result = (user, domain, domain is null ? user : $"{domain}\\{user}");
        return true;
    }

    static string? QuerySessionString(uint sessionId, WTS_INFO_CLASS infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
            return null;

        try
        {
            var s = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                WTSFreeMemory(buffer);
        }
    }

    static bool TryGetLastLoggedOnUser(out (string Utilizador, string? Dominio, string LinhaCompleta) result)
    {
        result = default;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            if (key is null)
                return false;

            if (TryNormalizeUser(key.GetValue("LastLoggedOnSAMUser") as string, out result))
                return true;

            return TryNormalizeUser(key.GetValue("LastLoggedOnUser") as string, out result);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        uint sessionId,
        WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer,
        out uint pBytesReturned);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pointer);

    enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit,
    }
}
