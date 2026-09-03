using Velopack.Windows;

namespace SistecHub.Core;

/// <summary>Garante que o SistecHub inicia automaticamente quando o Windows inicia (MSI / Program Files).</summary>
internal static class WindowsStartupRegistration
{
    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows() || !AppUpdateService.IsUpdateSupported)
            return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        try
        {
            EnsureStartupShortcut(Environment.SpecialFolder.CommonStartup, exePath);
        }
        catch
        {
            try
            {
                EnsureStartupShortcut(Environment.SpecialFolder.Startup, exePath);
            }
            catch
            {
                // Melhor esforço: falha não impede o app de iniciar.
            }
        }
    }

    static void EnsureStartupShortcut(Environment.SpecialFolder startupFolder, string exePath)
    {
        var startupDir = Environment.GetFolderPath(startupFolder);
        var linkPath = Path.Combine(startupDir, $"{AppReleaseConfig.PackTitle}.lnk");

        if (ShortcutPointsToExe(linkPath, exePath))
            return;

        using var link = new ShellLink();
        link.Target = exePath;
        link.Arguments = "--autostart";
        link.WorkingDirectory = Path.GetDirectoryName(exePath)!;
        link.Description = AppReleaseConfig.PackTitle;
        link.Save(linkPath);
    }

    static bool ShortcutPointsToExe(string linkPath, string exePath)
    {
        if (!File.Exists(linkPath))
            return false;

        try
        {
            using var link = new ShellLink(linkPath);
            return string.Equals(link.Target, exePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(link.Arguments, "--autostart", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
