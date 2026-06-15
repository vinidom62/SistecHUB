using System.Diagnostics;
using Velopack.Locators;

namespace SistecHub.Core;

/// <summary>
/// Velopack associa a instalação ao executável principal (<see cref="AppReleaseConfig.MainExeName"/>).
/// O serviço Windows corre a partir de <c>SistecHub.Service.exe</c> na mesma pasta — sem isto,
/// <see cref="VelopackUpdateEngine"/> não detecta a instalação.
/// </summary>
static class VelopackInstallLocator
{
    static IVelopackLocator? _cached;

    public static IVelopackLocator Create()
    {
        if (_cached is not null)
            return _cached;

        var defaultLocator = VelopackLocator.CreateDefaultForPlatform(null, null);
        var processPath = defaultLocator.Process.GetCurrentProcessPath();

        if (IsMainAppExecutable(processPath))
            return _cached = defaultLocator;

        var mainExe = ResolveMainExecutablePath();
        if (mainExe is null)
            return _cached = defaultLocator;

        UpdateActivityLog.Info(
            "Update",
            $"Velopack: a usar locator do app principal ({mainExe}) em vez de {processPath}.");

        return _cached = VelopackLocator.CreateDefaultForPlatform(
            new MainAppProcessImpl(mainExe, defaultLocator.Process),
            null);
    }

    static bool IsMainAppExecutable(string? processPath) =>
        !string.IsNullOrWhiteSpace(processPath)
        && string.Equals(
            Path.GetFileName(processPath),
            AppReleaseConfig.MainExeName,
            StringComparison.OrdinalIgnoreCase);

    static string? ResolveMainExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var dir = Path.GetDirectoryName(processPath);
        if (dir is null)
            return null;

        var mainExe = Path.Combine(dir, AppReleaseConfig.MainExeName);
        return File.Exists(mainExe) ? mainExe : null;
    }

    sealed class MainAppProcessImpl(string mainExePath, IProcessImpl inner) : IProcessImpl
    {
        public string GetCurrentProcessPath() => mainExePath;

        public uint GetCurrentProcessId() => inner.GetCurrentProcessId();

        public void StartProcess(
            string exePath,
            IEnumerable<string> args,
            string workingDirectory,
            bool asAdmin) =>
            inner.StartProcess(exePath, args, workingDirectory, asAdmin);

        public void Exit(int exitCode) => inner.Exit(exitCode);
    }
}
