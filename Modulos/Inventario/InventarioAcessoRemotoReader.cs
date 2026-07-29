using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SistecHub.Modulos.Inventario;

/// <summary>Lê identificadores de software de acesso remoto instalado (ficheiros de configuração / registo).</summary>
internal static class InventarioAcessoRemotoReader
{
    static readonly Regex[] AnyDeskIdLinePatterns =
    {
        new(@"^\s*ad\.(?:__)?anynet\.id\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^\s*anynet\.id\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    /// <summary>ID público AnyDesk (número), quando existir configuração local.</summary>
    public static AcessoRemotoInventario ReadAcessoRemoto()
    {
        var id = TryReadAnyDeskIdFromConfFiles() ?? TryReadAnyDeskIdFromRegistry();
        return new AcessoRemotoInventario(id);
    }

    static string? TryReadAnyDeskIdFromConfFiles()
    {
        foreach (var path in EnumerateAnyDeskConfPaths())
        {
            var id = TryReadAnyDeskIdFromFile(path);
            if (id is not null)
                return id;
        }

        return null;
    }

    static IEnumerable<string> EnumerateAnyDeskConfPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in CollectAnyDeskConfPaths())
        {
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(p);
            }
            catch
            {
                continue;
            }

            if (seen.Add(full))
                yield return full;
        }
    }

    static List<string> CollectAnyDeskConfPaths()
    {
        var list = new List<string>();
        var confNames = new[] { "service.conf", "system.conf", "user.conf" };

        void AddRootFiles(string rootDir)
        {
            foreach (var name in confNames)
            {
                var p = Path.Combine(rootDir, name);
                if (File.Exists(p))
                    list.Add(p);
            }
        }

        void AddAdSubdirs(string rootDir)
        {
            try
            {
                if (!Directory.Exists(rootDir))
                    return;
                foreach (var sub in Directory.EnumerateDirectories(rootDir, "ad_*", SearchOption.TopDirectoryOnly))
                {
                    foreach (var name in confNames)
                    {
                        var p = Path.Combine(sub, name);
                        if (File.Exists(p))
                            list.Add(p);
                    }
                }
            }
            catch
            {
                // ignorar
            }
        }

        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AnyDesk");
        AddRootFiles(common);
        AddAdSubdirs(common);

        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnyDesk");
        AddRootFiles(roaming);
        AddAdSubdirs(roaming);

        return list;
    }

    static string? TryReadAnyDeskIdFromFile(string path)
    {
        try
        {
            foreach (var raw in File.ReadLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd('\r');
                foreach (var re in AnyDeskIdLinePatterns)
                {
                    var m = re.Match(line);
                    if (!m.Success)
                        continue;
                    var normalized = NormalizeAnyDeskId(m.Groups[1].Value);
                    if (normalized is not null)
                        return normalized;
                }
            }
        }
        catch
        {
            // ignorar
        }

        return null;
    }

    static string? NormalizeAnyDeskId(string raw)
    {
        var v = raw.Trim().Trim('"', '\'');
        if (v.Length is < 6 or > 32)
            return null;
        // Endereço AnyDesk clássico: só dígitos (9–10 é o mais comum).
        if (Regex.IsMatch(v, @"^\d{6,15}$"))
            return v;
        return null;
    }

    static string? TryReadAnyDeskIdFromRegistry()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var subName in new[] { @"SOFTWARE\AnyDesk", @"SOFTWARE\WOW6432Node\AnyDesk" })
                    {
                        using var k = baseKey.OpenSubKey(subName);
                        if (k is null)
                            continue;
                        foreach (var valueName in new[] { "AnyDeskID", "ID", "AdAnyNetId", "ad.anynet.id" })
                        {
                            var s = k.GetValue(valueName)?.ToString()?.Trim();
                            var n = NormalizeAnyDeskId(s ?? "");
                            if (n is not null)
                                return n;
                        }
                    }
                }
                catch
                {
                    // ignorar
                }
            }
        }

        return null;
    }
}
