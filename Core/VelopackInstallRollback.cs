using System.Diagnostics;

namespace SistecHub.Core;

/// <summary>Remove a instalação Velopack quando o registo do serviço falha.</summary>
internal static class VelopackInstallRollback
{
    public static void TryUninstallSilently(string reason)
    {
        if (!OperatingSystem.IsWindows())
            return;

        ServiceLogWriter.Error("Install", $"Instalação cancelada: {reason}");

        var updateExe = ResolveUpdateExePath();
        if (updateExe is null)
        {
            ServiceLogWriter.Warn("Install", "Update.exe não encontrado — rollback manual pode ser necessário.");
            return;
        }

        try
        {
            ServiceLogWriter.Info("Install", $"A executar rollback: \"{updateExe}\" uninstall --silent");

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = updateExe,
                Arguments = "uninstall --silent",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas",
            });

            process?.WaitForExit(60_000);
            ServiceLogWriter.Info("Install", "Rollback concluído.");
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Install", ex, "Falha ao executar rollback (Update.exe uninstall).");
        }
    }

    static string? ResolveUpdateExePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var exeDir = Path.GetDirectoryName(exePath);
        if (exeDir is null)
            return null;

        if (string.Equals(Path.GetFileName(exeDir), "current", StringComparison.OrdinalIgnoreCase))
        {
            var rootDir = Directory.GetParent(exeDir)?.FullName;
            if (rootDir is null)
                return null;

            var updateExe = Path.Combine(rootDir, "Update.exe");
            return File.Exists(updateExe) ? updateExe : null;
        }

        var fallback = Path.Combine(exeDir, "Update.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
