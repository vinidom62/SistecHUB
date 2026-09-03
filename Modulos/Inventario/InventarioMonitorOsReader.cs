using System.Management;
using System.Text;
using Microsoft.Win32;

namespace SistecHub.Modulos.Inventario;

/// <summary>Monitores (WMI) e sistema operacional (registo).</summary>
internal static class InventarioMonitorOsReader
{
    public static IReadOnlyList<MonitorInventario> ReadMonitors()
    {
        var idRows = QueryWmiMonitorIds();
        idRows.Sort(static (a, b) => string.CompareOrdinal(a.InstanceName, b.InstanceName));

        return idRows
            .Select(row => new MonitorInventario(row.Modelo, row.Serial))
            .ToList();
    }

    public static SistemaOperacionalInventario ReadSistemaOperacional()
    {
        var nome = "Windows";
        string? displayVer = null;
        string? build = null;
        int? ubr = null;
        DateTime? install = null;

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k is not null)
            {
                nome = k.GetValue("ProductName") as string ?? nome;
                displayVer = k.GetValue("DisplayVersion") as string;
                build = k.GetValue("CurrentBuild") as string;
                ubr = k.GetValue("UBR") as int?;
                if (ubr is null && k.GetValue("UBR") is uint u)
                    ubr = (int)u;

                install = TryReadInstallDate(k.GetValue("InstallDate"));
            }
        }
        catch
        {
            // ignorar
        }

        var buildNumber = 0;
        if (!int.TryParse(build, out buildNumber))
            buildNumber = Environment.OSVersion.Version.Build;

        // No Windows 11, a Microsoft frequentemente mantém ProductName como "Windows 10 ..." no registro para compatibilidade.
        // Toda build >= 22000 é Windows 11.
        if (buildNumber >= 22000)
        {
            if (nome.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
            {
                nome = nome.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            }
            else if (!nome.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
            {
                nome = "Windows 11 " + nome;
            }
        }

        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(displayVer))
            parts.Add(displayVer.Trim());
        if (!string.IsNullOrWhiteSpace(build))
        {
            var b = build.Trim();
            parts.Add(ubr is > 0 ? $"{b}.{ubr}" : b);
        }

        if (parts.Count == 0)
            parts.Add(Environment.OSVersion.Version.ToString());

        var versaoAtual = string.Join(" ", parts);
        var dataIso = install is { } d ? d.ToString("yyyy-MM-dd") : null;

        var (statusAtivacao, _, canal, partialKey) = ReadLicenseStatus();
        var chaveAtivacao = ReadProductKey(partialKey);

        return new SistemaOperacionalInventario(
            nome.Trim(),
            arch,
            versaoAtual,
            dataIso,
            statusAtivacao,
            chaveAtivacao,
            canal);
    }

    static (string StatusAtivacao, bool Ativado, string? Canal, string? PartialKey) ReadLicenseStatus()
    {
        var statusText = "Não ativado";
        var ativado = false;
        string? canal = null;
        string? partialKey = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description, LicenseStatus, PartialProductKey, ProductKeyChannel, ApplicationId " +
                "FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL");

            foreach (var o in searcher.Get())
            {
                using (o as IDisposable)
                {
                    var appId = o["ApplicationId"]?.ToString();
                    var name = o["Name"]?.ToString() ?? "";
                    var desc = o["Description"]?.ToString() ?? "";

                    var isWindows = (appId != null && appId.StartsWith("55c92734", StringComparison.OrdinalIgnoreCase))
                        || name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0
                        || desc.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!isWindows)
                        continue;

                    var status = o["LicenseStatus"] != null ? Convert.ToInt32(o["LicenseStatus"]) : -1;
                    var key = o["PartialProductKey"]?.ToString();
                    var ch = o["ProductKeyChannel"]?.ToString();

                    if (status == 1)
                    {
                        ativado = true;
                        statusText = "Ativado";
                        canal = ch;
                        partialKey = key;
                        break;
                    }

                    if (statusText == "Não ativado")
                    {
                        statusText = status switch
                        {
                            0 => "Não licenciado",
                            2 => "Carência inicial (OOBGrace)",
                            3 => "Carência (OOTGrace)",
                            4 => "Não genuíno",
                            5 => "Não ativado (Notificação)",
                            6 => "Carência estendida",
                            _ => "Não ativado",
                        };
                        canal = ch;
                        partialKey = key;
                    }
                }
            }
        }
        catch
        {
            statusText = "Desconhecido";
        }

        return (statusText, ativado, canal, partialKey);
    }

    static string? ReadProductKey(string? partialKey)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k?.GetValue("DigitalProductId") is byte[] raw)
            {
                var decoded = DecodeDigitalProductId(raw);
                if (!string.IsNullOrWhiteSpace(decoded))
                    return decoded;
            }
        }
        catch
        {
            // ignorar
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            foreach (var o in searcher.Get())
            {
                using (o as IDisposable)
                {
                    var key = o["OA3xOriginalProductKey"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(key) && key.Length >= 20)
                        return key;
                }
            }
        }
        catch
        {
            // ignorar
        }

        if (!string.IsNullOrWhiteSpace(partialKey))
            return $"***** - ***** - ***** - ***** - {partialKey.Trim()}";

        return null;
    }

    static string? DecodeDigitalProductId(byte[] digitalProductId)
    {
        if (digitalProductId == null || digitalProductId.Length < 67)
            return null;

        try
        {
            const string digits = "BCDFGHJKMPQRTVWXY2346789";
            const int keyStartIndex = 52;
            const int decodeLength = 15;
            const int decodeStringLength = 29;

            var hexPid = new byte[decodeLength];
            Array.Copy(digitalProductId, keyStartIndex, hexPid, 0, decodeLength);

            var isWin8 = (byte)((digitalProductId[66] / 6) & 1);
            hexPid[14] = (byte)((hexPid[14] & 0xF7) | ((isWin8 & 2) * 4));

            var keyChars = new char[decodeStringLength];
            var last = 0;

            for (var i = decodeStringLength - 1; i >= 0; i--)
            {
                if ((i + 1) % 6 == 0)
                {
                    keyChars[i] = '-';
                }
                else
                {
                    var digitMapIndex = 0;
                    for (var j = decodeLength - 1; j >= 0; j--)
                    {
                        var byteValue = (digitMapIndex << 8) | hexPid[j];
                        hexPid[j] = (byte)(byteValue / 24);
                        digitMapIndex = byteValue % 24;
                        last = digitMapIndex;
                    }
                    keyChars[i] = digits[digitMapIndex];
                }
            }

            if (isWin8 == 1)
            {
                var rawKey = new string(keyChars).Replace("-", "");
                if (rawKey.Length >= 25 && last < rawKey.Length)
                {
                    var keyString = rawKey.Substring(1).Insert(last, "N");
                    if (keyString.Length >= 25)
                    {
                        return $"{keyString.Substring(0, 5)}-{keyString.Substring(5, 5)}-{keyString.Substring(10, 5)}-{keyString.Substring(15, 5)}-{keyString.Substring(20, 5)}";
                    }
                }
            }

            return new string(keyChars);
        }
        catch
        {
            return null;
        }
    }

    static DateTime? TryReadInstallDate(object? raw)
    {
        if (raw is null)
            return null;
        try
        {
            if (raw is int i32)
                return DateTimeOffset.FromUnixTimeSeconds((uint)i32).LocalDateTime.Date;
            if (raw is uint u32)
                return DateTimeOffset.FromUnixTimeSeconds(u32).LocalDateTime.Date;
            if (raw is long l)
                return DateTimeOffset.FromUnixTimeSeconds(l).LocalDateTime.Date;
            if (raw is string s && uint.TryParse(s.Trim(), out var u))
                return DateTimeOffset.FromUnixTimeSeconds(u).LocalDateTime.Date;
        }
        catch
        {
            return null;
        }

        return null;
    }

    sealed class IdRow
    {
        public string InstanceName { get; set; } = "";
        public string? Modelo { get; set; }
        public string? Serial { get; set; }
    }

    static List<IdRow> QueryWmiMonitorIds()
    {
        var list = new List<IdRow>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
            foreach (var o in searcher.Get())
            {
                using (o as IDisposable)
                {
                    var inst = o["InstanceName"]?.ToString() ?? "";
                    if (inst.Length == 0)
                        continue;

                    var friendly = DecodeWmiChar16(o["UserFriendlyName"]);
                    var mfr = DecodeWmiChar16(o["ManufacturerName"]);
                    var product = DecodeWmiChar16(o["ProductCodeID"]);
                    var serial = DecodeWmiChar16(o["SerialNumberID"]);

                    var modelo = !string.IsNullOrWhiteSpace(friendly)
                        ? friendly.Trim()
                        : CombineModel(mfr, product);

                    if (serial is not null && IsPlaceholderSerial(serial))
                        serial = null;

                    list.Add(new IdRow
                    {
                        InstanceName = inst,
                        Modelo = string.IsNullOrWhiteSpace(modelo) ? null : modelo.Trim(),
                        Serial = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim(),
                    });
                }
            }
        }
        catch
        {
            // ignorar
        }

        return list;
    }

    static string? CombineModel(string? mfr, string? product)
    {
        var a = mfr?.Trim() ?? "";
        var b = product?.Trim() ?? "";
        if (a.Length == 0 && b.Length == 0)
            return null;
        if (a.Length == 0)
            return b;
        if (b.Length == 0)
            return a;
        return $"{a} {b}";
    }

    static bool IsPlaceholderSerial(string s)
    {
        var t = s.Trim();
        return t.Length == 0
               || t.Equals("0", StringComparison.Ordinal)
               || t.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    static string? DecodeWmiChar16(object? v)
    {
        if (v is not ushort[] arr || arr.Length == 0)
            return null;
        var sb = new StringBuilder(arr.Length);
        foreach (var u in arr)
        {
            if (u == 0)
                break;
            if (u is >= 32 and < 0xFFFE)
                sb.Append((char)u);
        }

        var s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}
