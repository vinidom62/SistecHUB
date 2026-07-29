using System.Net.NetworkInformation;
using System.Text;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.Modulos.Inventario;

/// <summary>Garante que existe um ID de máquina GLPI; regista via PluginSistechubMachine se necessário.</summary>
internal static class InventarioMachineRegistration
{
    public static bool HasMachineId(AppUserSettings settings) =>
        InventarioPluginPayloadJson.ParsePluginMachineId(settings.GlpiMachineId) > 0;

    /// <summary>
    /// Se ainda não houver ID, cria a máquina no GLPI, grava o ID nas definições e devolve-o.
    /// Devolve <c>null</c> quando já existia ID (nada a fazer).
    /// </summary>
    public static async Task<int?> EnsureRegisteredAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (HasMachineId(settings))
            return null;

        if (!int.TryParse(settings.EntityId?.Trim(), out var entityId) || entityId < 1)
        {
            throw new InvalidOperationException(
                "Entidade não configurada. Conclua as configurações antes de inventariar a máquina.");
        }

        var mac = TryGetPrimaryMacAddress();
        if (string.IsNullOrWhiteSpace(mac))
        {
            throw new InvalidOperationException(
                "Não foi possível obter o endereço MAC desta máquina.");
        }

        var hostname = Environment.MachineName?.Trim() ?? "";
        if (hostname.Length == 0)
            throw new InvalidOperationException("Não foi possível obter o hostname desta máquina.");

        var utilizadorDominio = InventarioPostoReader.ReadPostoTrabalho().UtilizadorDominio?.Trim() ?? "";
        if (utilizadorDominio.Length == 0)
        {
            var user = Environment.UserName?.Trim() ?? "";
            var domain = Environment.UserDomainName?.Trim() ?? "";
            utilizadorDominio = string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(user)
                ? user
                : $"{domain}\\{user}";
        }

        var createdId = await GlpiApiClient.PostPluginSistechubMachineAsync(
                settings,
                hostname,
                entityId,
                mac,
                utilizadorDominio,
                cancellationToken)
            .ConfigureAwait(false);

        settings.GlpiMachineId = createdId.ToString();
        AppSettingsStore.Save(settings);
        return createdId;
    }

    static readonly string[] IgnoredNicNameFragments =
    [
        "HYPER-V",
        "RADMIN",
        "HAMACHI",
    ];

    /// <summary>
    /// MAC da placa com maior tráfego, ignorando Hyper-V / Radmin / Hamachi.
    /// Inclui placas offline (ex.: cabo desligado) e usa WMI como fallback.
    /// Formato <c>AA:BB:CC:DD:EE:FF</c>.
    /// </summary>
    public static string? TryGetPrimaryMacAddress()
    {
        try
        {
            var fromManaged = TryGetMacFromNetworkInterfaces();
            if (!string.IsNullOrWhiteSpace(fromManaged))
                return fromManaged;

            return TryGetMacFromWmi();
        }
        catch
        {
            try
            {
                return TryGetMacFromWmi();
            }
            catch
            {
                return null;
            }
        }
    }

    static string? TryGetMacFromNetworkInterfaces()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.NetworkInterfaceType is not (
                    NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                && !IsIgnoredVirtualNic(n))
            .Select(n =>
            {
                long totalBytes = 0;
                try
                {
                    var stats = n.GetIPStatistics();
                    totalBytes = stats.BytesSent + stats.BytesReceived;
                }
                catch
                {
                }

                return new NicCandidate(
                    n.Name,
                    n.Description,
                    n.GetPhysicalAddress().GetAddressBytes(),
                    totalBytes,
                    n.OperationalStatus == OperationalStatus.Up,
                    NicPriority(n.NetworkInterfaceType));
            })
            .Where(c => IsValidMacBytes(c.MacBytes))
            .ToList();

        return PickBestMac(nics);
    }

    static string? TryGetMacFromWmi()
    {
        var nics = new List<NicCandidate>();

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, Description, MACAddress, NetEnabled, Speed FROM Win32_NetworkAdapter " +
                "WHERE MACAddress IS NOT NULL AND PhysicalAdapter = TRUE");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject o in results)
            {
                using (o)
                {
                    var name = o["Name"]?.ToString() ?? "";
                    var description = o["Description"]?.ToString() ?? "";
                    if (IsIgnoredVirtualName($"{name} {description}"))
                        continue;

                    var macRaw = o["MACAddress"]?.ToString();
                    var macBytes = ParseMacBytes(macRaw);
                    if (!IsValidMacBytes(macBytes))
                        continue;

                    var enabled = o["NetEnabled"] is bool b && b;
                    long speed = 0;
                    try
                    {
                        if (o["Speed"] is not null)
                            speed = Convert.ToInt64(o["Speed"]);
                    }
                    catch
                    {
                    }

                    nics.Add(new NicCandidate(
                        name,
                        description,
                        macBytes!,
                        speed,
                        enabled,
                        5));
                }
            }
        }
        catch
        {
            return null;
        }

        return PickBestMac(nics);
    }

    static string? PickBestMac(List<NicCandidate> nics)
    {
        if (nics.Count == 0)
            return null;

        var best = nics
            .OrderByDescending(c => c.IsUp)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.TypePriority)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        return FormatMac(best.MacBytes);
    }

    static bool IsIgnoredVirtualNic(NetworkInterface nic) =>
        IsIgnoredVirtualName($"{nic.Name} {nic.Description}");

    static bool IsIgnoredVirtualName(string blob) =>
        IgnoredNicNameFragments.Any(fragment =>
            blob.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    static bool IsValidMacBytes(byte[]? bytes) =>
        bytes is { Length: 6 } && bytes.Any(b => b != 0);

    static byte[]? ParseMacBytes(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return null;

        var hex = mac.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim();
        if (hex.Length != 12)
            return null;

        try
        {
            var bytes = new byte[6];
            for (var i = 0; i < 6; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    static int NicPriority(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => 0,
        NetworkInterfaceType.GigabitEthernet => 0,
        NetworkInterfaceType.FastEthernetT => 0,
        NetworkInterfaceType.Wireless80211 => 1,
        _ => 10,
    };

    sealed record NicCandidate(
        string Name,
        string Description,
        byte[] MacBytes,
        long Score,
        bool IsUp,
        int TypePriority);

    static string FormatMac(byte[] bytes)
    {
        var sb = new StringBuilder(17);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
                sb.Append(':');
            sb.Append(bytes[i].ToString("X2"));
        }

        return sb.ToString();
    }
}
