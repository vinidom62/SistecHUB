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

        return new SistemaOperacionalInventario(
            nome.Trim(),
            arch,
            versaoAtual,
            dataIso);
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
