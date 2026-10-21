using Velopack.Windows;

namespace SistecHub.Core;

/// <summary>
/// Garante que o SistecHub inicia automaticamente com o Windows (com o argumento --autostart)
/// para todos os utilizadores da máquina via CommonStartup (ProgramData), mantendo o comportamento
/// consistente e evitando inicializações duplicadas.
/// </summary>
internal static class WindowsStartupRegistration
{
    public static void EnsureRegistered(string? customExePath = null)
    {
        if (!OperatingSystem.IsWindows() || !VelopackUpdateEngine.IsInstalled)
            return;

        var targetExe = ResolveTargetExePath(customExePath);
        if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
            return;

        var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        var commonRegistered = false;

        // 1. Garante o atalho na pasta pública CommonStartup (All Users / ProgramData)
        // Isso assegura que QUALQUER utilizador que inicie sessão na máquina (novo ou existente)
        // terá o SistecHub iniciado automaticamente com --autostart.
        try
        {
            if (!string.IsNullOrWhiteSpace(commonStartup) && Directory.Exists(commonStartup))
            {
                EnsureStartupShortcut(commonStartup, targetExe);
                commonRegistered = ShortcutPointsToExeWithAutostart(
                    Path.Combine(commonStartup, $"{AppReleaseConfig.PackTitle}.lnk"), targetExe);
                if (commonRegistered)
                {
                    UpdateActivityLog.Info("App", "Atalho de arranque para todos os utilizadores (CommonStartup) garantido.");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.Warn("App", "Não foi possível registar em CommonStartup (permissões): " + ex.Message);
        }

        // 2. Se o atalho para todos os utilizadores (CommonStartup) está ativo, removemos
        // qualquer atalho duplicado na pasta Startup pessoal do utilizador atual.
        // Se CommonStartup NÃO pôde ser registado (ex: executado sem privilégios antes do serviço),
        // registamos na pasta do utilizador atual como fallback.
        try
        {
            var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (commonRegistered)
            {
                RemoveUserStartupShortcut();
            }
            else if (!string.IsNullOrWhiteSpace(userStartup) && Directory.Exists(userStartup))
            {
                EnsureStartupShortcut(userStartup, targetExe);
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.Warn("App", "Falha ao registar/limpar atalho de arranque do utilizador: " + ex.Message);
        }
    }

    /// <summary>
    /// Remove o atalho de inicialização do utilizador atual.
    /// </summary>
    public static bool RemoveUserStartupShortcut()
    {
        try
        {
            var userStartup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!string.IsNullOrWhiteSpace(userStartup))
            {
                var userLink = Path.Combine(userStartup, $"{AppReleaseConfig.PackTitle}.lnk");
                if (File.Exists(userLink))
                {
                    File.Delete(userLink);
                    UpdateActivityLog.Info("App", "Atalho individual do utilizador removido (CommonStartup activo).");
                    return true;
                }
            }
        }
        catch
        {
            // Ignora
        }

        return false;
    }

    /// <summary>
    /// Remove o atalho de inicialização automática de todas as localizações (ex: ao desinstalar).
    /// </summary>
    public static bool RemoveStartupShortcut()
    {
        var removed = RemoveUserStartupShortcut();

        try
        {
            var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (!string.IsNullOrWhiteSpace(commonStartup))
            {
                var commonLink = Path.Combine(commonStartup, $"{AppReleaseConfig.PackTitle}.lnk");
                if (File.Exists(commonLink))
                {
                    File.Delete(commonLink);
                    removed = true;
                }
            }
        }
        catch
        {
            // Ignora
        }

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

    public static string? ResolveTargetExePath(string? candidatePath = null)
    {
        try
        {
            var exePath = candidatePath ?? Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return null;

            var dir = Path.GetDirectoryName(exePath);
            if (dir is null)
                return null;

            var isService = string.Equals(Path.GetFileName(exePath), "SistecHub.Service.exe", StringComparison.OrdinalIgnoreCase);

            // 1. Se estivermos numa subpasta (ex: \current\), verificar se o stub raiz existe
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

            // 2. Se for chamado pelo serviço, procurar SistecHub.exe na mesma pasta
            if (isService)
            {
                var mainInDir = Path.Combine(dir, AppReleaseConfig.MainExeName);
                if (File.Exists(mainInDir))
                    return mainInDir;
            }

            // 3. Se for o próprio executável principal
            if (File.Exists(exePath) && !isService)
                return exePath;
        }
        catch
        {
            // Ignora
        }

        return null;
    }
}
