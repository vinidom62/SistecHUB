using Velopack;
using Velopack.Sources;

namespace SistecHub.Core;

/// <summary>Verificação, download e aplicação Velopack sem UI (usado pelo serviço Windows).</summary>
public static class VelopackUpdateEngine
{
    static UpdateManager? _manager;

    static UpdateManager Manager => _manager ??= new UpdateManager(
        new GithubSource(AppReleaseConfig.GitHubRepoUrl, accessToken: null, prerelease: false),
        options: null,
        locator: VelopackInstallLocator.Create());

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return Manager.IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string DisplayVersion =>
        IsInstalled && Manager.CurrentVersion is { } v
            ? v.ToString()
            : AppVersion.Current;

    public static VelopackAsset? PendingRestart
    {
        get
        {
            try
            {
                return Manager.UpdatePendingRestart;
            }
            catch
            {
                return null;
            }
        }
    }

    public static async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return null;

        return await Manager.CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public static async Task DownloadUpdatesAsync(
        UpdateInfo update,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return;

        await Manager.DownloadUpdatesAsync(update, progress, cancellationToken).ConfigureAwait(false);
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
            Manager.ApplyUpdatesAndExit(asset);
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "ApplyUpdatesAndExit falhou.");
            UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
            {
                Phase = UpdateServicePhase.Error,
                Message = "Falha ao aplicar actualização: " + ex.Message,
                AvailableVersion = asset.Version.ToString(),
            });
            throw;
        }
    }
}
