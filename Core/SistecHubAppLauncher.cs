using System.Diagnostics;
using Velopack;

namespace SistecHub.Core;

/// <summary>Inicia o executável principal do SistecHub após actualização.</summary>
internal static class SistecHubAppLauncher
{
    public static bool TryStartMainApp(string? reason = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (VelopackUpdateEngine.IsInstalled)
            {
                UpdateActivityLog.Info("Update", $"A iniciar {AppReleaseConfig.MainExeName} via Update.exe{(reason is null ? "" : $" ({reason})")}.");
                var locator = VelopackInstallLocator.Create();
                UpdateExe.Start(locator, waitPid: 0, startArgs: null);
                return true;
            }

            var exePath = ResolveMainExecutablePath();
            if (exePath is null)
            {
                UpdateActivityLog.Error("Update", "Não foi possível localizar SistecHub.exe para reiniciar.");
                return false;
            }

            UpdateActivityLog.Info("Update", $"A iniciar {exePath}{(reason is null ? "" : $" ({reason})")}.");
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            });
            return true;
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao reiniciar o SistecHub.");
            return false;
        }
    }

    static string? ResolveMainExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var dir = Path.GetDirectoryName(processPath);
        if (dir is null)
            return null;

        var candidate = Path.Combine(dir, AppReleaseConfig.MainExeName);
        return File.Exists(candidate) ? candidate : null;
    }
}
