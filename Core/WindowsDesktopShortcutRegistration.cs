using Velopack.Windows;

namespace SistecHub.Core;

/// <summary>
/// Garante que o atalho do SistecHub exista na Área de Trabalho após a instalação e atualizações.
/// </summary>
internal static class WindowsDesktopShortcutRegistration
{
    /// <summary>
    /// Verifica se a máquina possui o atalho na Área de Trabalho e, caso não tenha, cria-o automaticamente.
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

        if (HasDesktopShortcut(exePath))
            return;

        // 1. Tenta criar na Área de Trabalho pública (visível para todos os utilizadores da máquina)
        try
        {
            var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (TryCreateShortcut(commonDesktop, exePath))
            {
                UpdateActivityLog.Info("App", $"Atalho criado na Área de Trabalho pública ({commonDesktop}).");
                return;
            }
        }
        catch
        {
            // Sem permissão na pasta pública (executado por utilizador padrão); fallback para utilizador.
        }

        // 2. Fallback: cria na Área de Trabalho do utilizador atual
        try
        {
            var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (TryCreateShortcut(userDesktop, exePath))
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
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (HasValidShortcutInDirectory(commonDesktop, exePath))
            return true;

        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (HasValidShortcutInDirectory(userDesktop, exePath))
            return true;

        return false;
    }

    static bool HasValidShortcutInDirectory(string? directory, string exePath)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var defaultLink = Path.Combine(directory, $"{AppReleaseConfig.PackTitle}.lnk");
        if (File.Exists(defaultLink) && CheckAndUpdateShortcut(defaultLink, exePath))
            return true;

        try
        {
            foreach (var linkPath in Directory.EnumerateFiles(directory, "*.lnk"))
            {
                if (string.Equals(linkPath, defaultLink, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (CheckAndUpdateShortcut(linkPath, exePath))
                    return true;
            }
        }
        catch
        {
            // Ignora falhas de permissão ao listar arquivos da pasta.
        }

        return false;
    }

    static bool CheckAndUpdateShortcut(string linkPath, string exePath)
    {
        try
        {
            using var link = new ShellLink(linkPath);
            var target = link.Target;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            if (string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase))
                return true;

            // Se o atalho aponta para SistecHub.exe numa localização anterior (ex: pasta de versão pré-update),
            // atualiza o alvo para o executável atual mantendo o atalho válido.
            if (string.Equals(Path.GetFileName(target), AppReleaseConfig.MainExeName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    link.Target = exePath;
                    link.WorkingDirectory = Path.GetDirectoryName(exePath)!;
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

    static bool TryCreateShortcut(string? directory, string exePath)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var linkPath = Path.Combine(directory, $"{AppReleaseConfig.PackTitle}.lnk");
        using var link = new ShellLink();
        link.Target = exePath;
        link.Arguments = "";
        link.WorkingDirectory = Path.GetDirectoryName(exePath)!;
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
}
