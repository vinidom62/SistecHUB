using Velopack.Windows;

namespace SistecHub.Core;

/// <summary>
/// Garante que exista exatamente um atalho do SistecHub na Área de Trabalho,
/// desduplicando cópias redundantes entre a Área de Trabalho pública e a do utilizador.
/// </summary>
internal static class WindowsDesktopShortcutRegistration
{
    /// <summary>
    /// Verifica se a máquina possui atalho na Área de Trabalho.
    /// Se já existir na Área de Trabalho pública, remove atalhos redundantes do utilizador para evitar 2 ícones.
    /// Se não existir nenhum, cria-o automaticamente.
    /// </summary>
    /// <param name="force">Se true, ignora a validação de ambiente de desenvolvimento/instalação.</param>
    public static void EnsureRegistered(bool force = false)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!force && !AppUpdateService.IsUpdateSupported)
            return;

        var exePath = ResolveMainAppExePath();
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var rootStubPath = ResolveRootStubExePath(exePath);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        var hasCommonShortcut = HasValidShortcutInDirectory(commonDesktop, exePath, rootStubPath);

        // Se já existe atalho público (visível para todos os utilizadores da máquina),
        // qualquer atalho na Área de Trabalho do utilizador é redundante e faz o Windows exibir 2 ícones.
        if (hasCommonShortcut)
        {
            if (RemoveUserDesktopShortcuts(userDesktop))
            {
                UpdateActivityLog.Info("App", "Atalho duplicado no Desktop do utilizador removido (já existe atalho público).");
            }
            return;
        }

        // Se já existe atalho válido no Desktop do utilizador, não precisa criar outro
        if (HasValidShortcutInDirectory(userDesktop, exePath, rootStubPath))
            return;

        // Nenhum atalho existe: cria na Área de Trabalho do utilizador
        var targetExe = rootStubPath ?? exePath;
        try
        {
            if (TryCreateShortcut(userDesktop, targetExe))
            {
                UpdateActivityLog.Info("App", $"Atalho criado na Área de Trabalho do utilizador ({userDesktop}).");
            }
            else
            {
                UpdateActivityLog.Warn("App", "Não foi possível criar o atalho na Área de Trabalho.");
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.Warn("App", "Falha ao criar atalho na Área de Trabalho: " + ex.Message);
        }
    }

    /// <summary>
    /// Determina se a máquina já possui o atalho do SistecHub na Área de Trabalho (pública ou do utilizador).
    /// </summary>
    public static bool HasDesktopShortcut()
    {
        var exePath = ResolveMainAppExePath();
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        return HasDesktopShortcut(exePath);
    }

    public static bool HasDesktopShortcut(string exePath)
    {
        var rootStubPath = ResolveRootStubExePath(exePath);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (HasValidShortcutInDirectory(commonDesktop, exePath, rootStubPath))
            return true;

        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (HasValidShortcutInDirectory(userDesktop, exePath, rootStubPath))
            return true;

        return false;
    }

    /// <summary>
    /// Remove atalhos do SistecHub na Área de Trabalho do utilizador atual.
    /// Útil durante desinstalação ou para remover cópias duplicadas quando o atalho público existe.
    /// </summary>
    public static bool RemoveUserDesktopShortcuts(string? userDesktop = null)
    {
        userDesktop ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(userDesktop) || !Directory.Exists(userDesktop))
            return false;

        var removedAny = false;
        var defaultLink = Path.Combine(userDesktop, $"{AppReleaseConfig.PackTitle}.lnk");

        if (File.Exists(defaultLink))
        {
            try
            {
                File.Delete(defaultLink);
                removedAny = true;
            }
            catch
            {
                // Melhor esforço
            }
        }

        try
        {
            foreach (var linkPath in Directory.EnumerateFiles(userDesktop, "*.lnk"))
            {
                if (string.Equals(linkPath, defaultLink, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var link = new ShellLink(linkPath);
                    var target = link.Target;
                    if (!string.IsNullOrWhiteSpace(target) &&
                        string.Equals(Path.GetFileName(target), AppReleaseConfig.MainExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(linkPath);
                        removedAny = true;
                    }
                }
                catch
                {
                    // Ignora atalhos de terceiros ou com falha de leitura
                }
            }
        }
        catch
        {
            // Ignora falhas de listagem
        }

        return removedAny;
    }

    static bool HasValidShortcutInDirectory(string? directory, string exePath, string? rootStubPath)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var defaultLink = Path.Combine(directory, $"{AppReleaseConfig.PackTitle}.lnk");
        if (File.Exists(defaultLink) && CheckAndUpdateShortcut(defaultLink, exePath, rootStubPath))
            return true;

        try
        {
            foreach (var linkPath in Directory.EnumerateFiles(directory, "*.lnk"))
            {
                if (string.Equals(linkPath, defaultLink, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CheckAndUpdateShortcut(linkPath, exePath, rootStubPath))
                    return true;
            }
        }
        catch
        {
            // Ignora falhas de permissão ao listar arquivos da pasta.
        }

        return false;
    }

    static bool CheckAndUpdateShortcut(string linkPath, string exePath, string? rootStubPath)
    {
        try
        {
            using var link = new ShellLink(linkPath);
            var target = link.Target;
            if (string.IsNullOrWhiteSpace(target))
            {
                var fileName = Path.GetFileNameWithoutExtension(linkPath);
                return string.Equals(fileName, AppReleaseConfig.PackTitle, StringComparison.OrdinalIgnoreCase);
            }

            // Alvo exato atual ou stub raiz existente é 100% válido
            if (string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(rootStubPath) && string.Equals(target, rootStubPath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Se o atalho aponta para SistecHub.exe
            if (string.Equals(Path.GetFileName(target), AppReleaseConfig.MainExeName, StringComparison.OrdinalIgnoreCase))
            {
                // Se o arquivo apontado ainda existe (ex: stub raiz ou versão instalada), é válido
                if (File.Exists(target))
                    return true;

                // Se o destino não existe (ex: pasta de versão pré-update removida), atualiza o alvo
                try
                {
                    var newTarget = rootStubPath ?? exePath;
                    link.Target = newTarget;
                    link.WorkingDirectory = Path.GetDirectoryName(newTarget)!;
                    link.Save(linkPath);
                }
                catch
                {
                    // Melhor esforço na atualização de caminho.
                }
                return true;
            }

            return false;
        }
        catch
        {
            // Se o arquivo existe e o nome base coincide, considera como existente.
            var fileName = Path.GetFileNameWithoutExtension(linkPath);
            return string.Equals(fileName, AppReleaseConfig.PackTitle, StringComparison.OrdinalIgnoreCase);
        }
    }

    static bool TryCreateShortcut(string? directory, string targetExe)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var linkPath = Path.Combine(directory, $"{AppReleaseConfig.PackTitle}.lnk");
        using var link = new ShellLink();
        link.Target = targetExe;
        link.Arguments = "";
        link.WorkingDirectory = Path.GetDirectoryName(targetExe)!;
        link.Description = AppReleaseConfig.PackTitle;
        link.Save(linkPath);
        return true;
    }

    static string? ResolveMainAppExePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        if (string.Equals(Path.GetFileName(exePath), AppReleaseConfig.MainExeName, StringComparison.OrdinalIgnoreCase))
            return exePath;

        var dir = Path.GetDirectoryName(exePath);
        if (dir is null)
            return null;

        var candidate = Path.Combine(dir, AppReleaseConfig.MainExeName);
        return File.Exists(candidate) ? candidate : null;
    }

    static string? ResolveRootStubExePath(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (dir is null)
                return null;

            // Se exePath está em "current", o stub fica no diretório pai
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

            // Se já está no diretório raiz
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
