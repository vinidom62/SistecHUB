namespace SistecHub.Core;

/// <summary>
/// Em instalações em Program Files, concede escrita na pasta do Velopack para utilizadores
/// autenticados, permitindo que <c>Update.exe</c> aplique atualizações sem pedir UAC.
/// </summary>
internal static class PerMachineUpdatePermissions
{
    public static void EnsureInstallFolderWritableForUpdates()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var rootDir = TryResolveVelopackRootDirectory();
        if (rootDir is null || !IsPerMachinePath(rootDir))
            return;

        FileSystemAclHelper.GrantAuthenticatedUsersModifyAccess(rootDir);
    }

    internal static bool IsPerMachinePath(string path) =>
        path.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase);

    static string? TryResolveVelopackRootDirectory()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var exeDir = Path.GetDirectoryName(exePath);
        if (exeDir is null)
            return null;

        if (string.Equals(Path.GetFileName(exeDir), "current", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(exeDir)?.FullName;

        return exeDir;
    }
}
