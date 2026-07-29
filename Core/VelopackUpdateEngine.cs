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
    /// Aplica via Velopack e termina o processo actual (serviço).
    /// Os hooks Velopack no app principal reiniciam o serviço após a actualização.
    /// </summary>
    public static void ScheduleApplyAndExit(VelopackAsset asset)
    {
        if (!IsInstalled)
        {
            UpdateActivityLog.Error("Update", "ScheduleApplyAndExit abortado — Velopack não detectado.");
            return;
        }

        UpdateActivityLog.Info("Update", $"A aplicar versão {asset.Version} via Velopack (processo actual termina).");
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
            StableManager.ApplyUpdatesAndExit(asset);
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "ApplyUpdatesAndExit falhou.");
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
