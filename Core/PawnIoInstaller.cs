using System.Diagnostics;
using Microsoft.Win32;

namespace SistecHub.Core;

/// <summary>
/// Instala o driver PawnIO (substituto do WinRing0 no LibreHardwareMonitor 0.9.5+).
/// Instalador oficial: https://github.com/namazso/PawnIO.Setup
/// </summary>
public static class PawnIoInstaller
{
    public const string SetupFileName = "PawnIO_setup.exe";
    const string MinVersionText = "2.2.0";
    static readonly Version MinVersion = new(2, 2, 0);

    /// <summary>ERROR_SUCCESS_REBOOT_REQUIRED (3010) — instalação OK, reinício pendente.</summary>
    public const int ExitRebootRequired = 3010;

    public static bool IsInstalled => TryGetInstalledVersion() is not null;

    public static bool MeetsMinimumVersion
    {
        get
        {
            var installed = TryGetInstalledVersion();
            return installed is not null && installed >= MinVersion;
        }
    }

    public static Version? TryGetInstalledVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var subKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (Version.TryParse(subKey?.GetValue("DisplayVersion") as string, out var version))
                    return version;
            }
            catch
            {
                // tenta a vista seguinte
            }
        }

        return null;
    }

    /// <summary>
    /// Garante PawnIO ≥ 2.2.0. Sem efeito se já estiver OK.
    /// Falhas são registadas e não propagadas (inventário degrada sem o driver).
    /// </summary>
    public static void EnsureInstalled(string? searchDirectory = null)
    {
        if (MeetsMinimumVersion)
        {
            ServiceLogWriter.Info("PawnIO", $"Já instalado (v{TryGetInstalledVersion()}).");
            return;
        }

        var setupPath = ResolveSetupPath(searchDirectory);
        if (setupPath is null)
        {
            ServiceLogWriter.Warn(
                "PawnIO",
                $"{SetupFileName} não encontrado junto da instalação — sensores LHM de CPU/MB podem falhar.");
            return;
        }

        var current = TryGetInstalledVersion();
        ServiceLogWriter.Info(
            "PawnIO",
            current is null
                ? $"A instalar a partir de {setupPath}…"
                : $"A actualizar v{current} → ≥{MinVersionText} ({setupPath})…");

        try
        {
            var exitCode = RunSilentInstall(setupPath);
            if (exitCode is 0 or ExitRebootRequired)
            {
                var after = TryGetInstalledVersion();
                ServiceLogWriter.Info(
                    "PawnIO",
                    exitCode == ExitRebootRequired
                        ? $"Instalado (v{after ?? TryParse(MinVersionText)}) — reinício do Windows pode ser necessário."
                        : $"Instalado com sucesso (v{after ?? TryParse(MinVersionText)}).");
            }
            else
            {
                ServiceLogWriter.Warn("PawnIO", $"Instalador terminou com código {exitCode}.");
            }
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("PawnIO", ex, "Falha ao instalar PawnIO.");
        }

        TryRemoveLegacyWinRing0Artifacts(searchDirectory);
    }

    /// <summary>Remove .sys WinRing0 legados extraídos pelo LHM ≤ 0.9.4 (ex.: SistecHub.Service.sys).</summary>
    public static void TryRemoveLegacyWinRing0Artifacts(string? searchDirectory = null)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(searchDirectory) && Directory.Exists(searchDirectory))
            dirs.Add(searchDirectory);

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
            dirs.Add(exeDir);

        var names = new[]
        {
            "SistecHub.Service.sys",
            "SistecHub.sys",
            "WinRing0x64.sys",
            "WinRing0.sys",
        };

        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(dir, name);
                try
                {
                    if (!File.Exists(path))
                        continue;
                    File.Delete(path);
                    ServiceLogWriter.Info("PawnIO", $"Removido driver legado: {path}");
                }
                catch (Exception ex)
                {
                    ServiceLogWriter.Warn("PawnIO", $"Não foi possível remover {path}: {ex.Message}");
                }
            }
        }
    }

    static string? ResolveSetupPath(string? searchDirectory)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(searchDirectory))
            candidates.Add(Path.Combine(searchDirectory, SetupFileName));

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir))
            candidates.Add(Path.Combine(exeDir, SetupFileName));

        // Velopack: ServiceSetup em current\; assets partilhados na mesma pasta após publish.
        candidates.Add(Path.Combine(AppContext.BaseDirectory, SetupFileName));

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    static int RunSilentInstall(string setupPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "-install -silent",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? AppContext.BaseDirectory,
        });

        if (process is null)
            throw new InvalidOperationException($"Não foi possível iniciar {SetupFileName}.");

        // Instalação de driver pode demorar em máquinas lentas.
        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException("Timeout a instalar PawnIO (180s).");
        }

        return process.ExitCode;
    }

    static Version? TryParse(string text) =>
        Version.TryParse(text, out var v) ? v : null;
}
