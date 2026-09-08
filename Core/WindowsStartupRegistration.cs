using Velopack.Windows;

namespace SistecHub.Core;

/// <summary>
/// Garante que o SistecHub inicia automaticamente com o Windows (com o argumento --autostart),
/// mantendo exatamente 1 atalho no Inicializar do utilizador e limpando atalhos legados do CommonStartup.
/// </summary>
internal static class WindowsStartupRegistration
{
    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows() || !AppUpdateService.IsUpdateSupported)
            return;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var rootStubPath = ResolveRootStubExePath(exePath);
        var targetExe = rootStubPath ?? exePath;

        // 1. Remove qualquer atalho antigo/legado na pasta pública CommonStartup
        // (ex: gerado por versões anteriores ou instalador Velopack sem --autostart)
        // para evitar que o Windows inicialize 2 instâncias do SistecHub.
        CleanupLegacyCommonStartup();

        // 2. Garante o atalho oficial com argumento --autostart na pasta Startup do utilizador
        try
        {
            var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            EnsureStartupShortcut(userStartup, targetExe);
        }
        catch (Exception ex)
        {
            UpdateActivityLog.Warn("App", "Falha ao registar arranque automático: " + ex.Message);
        }
    }

    /// <summary>
    /// Remove qualquer atalho legado do SistecHub em CommonStartup (ProgramData).
    /// </summary>
    public static bool CleanupLegacyCommonStartup()
    {
        try
        {
            var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (string.IsNullOrWhiteSpace(commonStartup) || !Directory.Exists(commonStartup))
                return false;

            var legacyLink = Path.Combine(commonStartup, $"{AppReleaseConfig.PackTitle}.lnk");
            if (File.Exists(legacyLink))
            {
                File.Delete(legacyLink);
                UpdateActivityLog.Info("App", "Atalho legado em CommonStartup removido com sucesso.");
                return true;
            }
        }
        catch
        {
            // Melhor esforço: pode falhar se executado sem elevação; o serviço em segundo plano (SYSTEM) também tenta limpar.
        }

        return false;
    }

    /// <summary>
    /// Remove o atalho de inicialização automática do utilizador (ex: ao desinstalar).
    /// </summary>
    public static bool RemoveStartupShortcut()
    {
        var removed = false;
        try
        {
            var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var userLink = Path.Combine(userStartup, $"{AppReleaseConfig.PackTitle}.lnk");
            if (File.Exists(userLink))
            {
                File.Delete(userLink);
                removed = true;
            }
        }
        catch
        {
            // Ignora
        }

        CleanupLegacyCommonStartup();
        return removed;
    }

    static void EnsureStartupShortcut(string startupDir, string targetExe)
    {
        if (string.IsNullOrWhiteSpace(startupDir) || !Directory.Exists(startupDir))
            return;

        var linkPath = Path.Combine(startupDir, $"{AppReleaseConfig.PackTitle}.lnk");

        if (ShortcutPointsToExeWithAutostart(linkPath, targetExe))
            return;

        using var link = new ShellLink();
        link.Target = targetExe;
        link.Arguments = "--autostart";
        link.WorkingDirectory = Path.GetDirectoryName(targetExe)!;
        link.Description = AppReleaseConfig.PackTitle;
        link.Save(linkPath);
    }

    static bool ShortcutPointsToExeWithAutostart(string linkPath, string targetExe)
    {
        if (!File.Exists(linkPath))
            return false;

        try
        {
            using var link = new ShellLink(linkPath);
            var hasAutostart = string.Equals(link.Arguments, "--autostart", StringComparison.OrdinalIgnoreCase);
            if (!hasAutostart)
                return false;

            if (string.Equals(link.Target, targetExe, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(Path.GetFileName(link.Target), AppReleaseConfig.MainExeName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    static string? ResolveRootStubExePath(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (dir is null)
                return null;

            if (string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
            {
                var parentDir = Path.GetDirectoryName(dir);
                if (parentDir is not null)
                {
                    var stub = Path.Combine(parentDir, AppReleaseConfig.MainExeName);
                    if (File.Exists(stub))
                        return stub;
                }
            }

            if (File.Exists(exePath))
                return exePath;
        }
        catch
        {
            // Ignora
        }

        return null;
    }
}
