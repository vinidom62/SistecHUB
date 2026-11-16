using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace SistecHub.Core;

/// <summary>Verificação, download e aplicação Velopack sem UI (usado pelo serviço Windows).</summary>
public static class VelopackUpdateEngine
{
    static UpdateManager? _stableManager;
    static UpdateManager? _prereleaseManager;

    static UpdateManager StableManager =>
        _stableManager ??= CreateManager(includePrerelease: false);

    static UpdateManager PrereleaseManager =>
        _prereleaseManager ??= CreateManager(includePrerelease: true);

    static UpdateManager CreateManager(bool includePrerelease) =>
        new(
            new GithubSource(AppReleaseConfig.GitHubRepoUrl, accessToken: null, prerelease: includePrerelease),
            options: null,
            locator: VelopackInstallLocator.Create());

    static UpdateManager ManagerFor(bool includePrerelease) =>
        includePrerelease ? PrereleaseManager : StableManager;

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return StableManager.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string DisplayVersion =>
        IsInstalled && StableManager.CurrentVersion is { } v
            ? v.ToString()
            : AppVersion.Current;

    public static VelopackAsset? PendingRestart
    {
        get
        {
            try
            {
                return StableManager.UpdatePendingRestart;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <param name="includePrerelease">
    /// Só true no fluxo manual «Verificar atualização Beta». Automático e estável usam false.
    /// </param>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync(
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return null;

        return await ManagerFor(includePrerelease).CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public static async Task DownloadUpdatesAsync(
        UpdateInfo update,
        bool includePrerelease = false,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return;

        await ManagerFor(includePrerelease)
            .DownloadUpdatesAsync(update, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Agenda a aplicação da atualização pelo <c>Update.exe</c> do Velopack aguardando o encerramento
    /// limpo deste processo (<c>--waitPid</c>) sem chamar <c>Environment.Exit</c> diretamente,
    /// permitindo que o serviço Windows notifique <c>SERVICE_STOPPED</c> ao SCM sem crash (Event 7031).
    /// </summary>
    public static void ScheduleApplyAndExit(VelopackAsset asset)
    {
        if (!IsInstalled)
        {
            UpdateActivityLog.Error("Update", "ScheduleApplyAndExit abortado — Velopack não detectado.");
            return;
        }

        UpdateActivityLog.Info("Update", $"A agendar aplicação da versão {asset.Version} via Update.exe (aguardando fecho gracioso do serviço).");
        UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
        {
            Phase = UpdateServicePhase.Applying,
            Message = $"A instalar versão {asset.Version}...",
            CurrentVersion = DisplayVersion,
            AvailableVersion = asset.Version.ToString(),
        });

        try
        {
            UpdateServiceCoordinator.RequestReopenAppAfterUpdate(asset.Version.ToString());

            var locator = VelopackInstallLocator.Create();
            var updateExe = locator.UpdateExePath;
            var pid = Environment.ProcessId;
            var logPath = Path.Combine(SharedMachineStorage.RootPath, "velopack-apply.log");

            string? packagePath = null;
            if (!string.IsNullOrWhiteSpace(locator.PackagesDir) && !string.IsNullOrWhiteSpace(asset.FileName))
            {
                var candidate = Path.Combine(locator.PackagesDir, asset.FileName);
                if (File.Exists(candidate))
                    packagePath = candidate;
            }

            if (!string.IsNullOrWhiteSpace(updateExe) && File.Exists(updateExe))
            {
                var argsBuilder = $"apply --waitPid {pid} --silent --norestart";
                if (!string.IsNullOrWhiteSpace(packagePath))
                    argsBuilder += $" --package \"{packagePath}\"";
                argsBuilder += $" --log \"{logPath}\"";

                var workingDir = Path.GetDirectoryName(updateExe)
                    ?? locator.RootAppDir
                    ?? AppContext.BaseDirectory;

                var startInfo = new ProcessStartInfo
                {
                    FileName = updateExe,
                    Arguments = argsBuilder,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                var proc = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Não foi possível iniciar o processo Update.exe.");

                UpdateActivityLog.Info(
                    "Update",
                    $"Update.exe iniciado (PID {proc.Id}) com argumentos: {argsBuilder}");
            }
            else
            {
                UpdateActivityLog.Warn("Update", "Update.exe não localizado diretamente no caminho padrão; a usar WaitExitThenApplyUpdates.");
                StableManager.WaitExitThenApplyUpdates(asset, silent: true, restart: false);
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "ScheduleApplyAndExit falhou.");
            UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
            {
                Phase = UpdateServicePhase.Error,
                Message = "Falha ao aplicar atualização: " + ex.Message,
                AvailableVersion = asset.Version.ToString(),
            });
            throw;
        }
    }
}
