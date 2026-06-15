using SistecHub.Core;
using Velopack;

namespace SistecHub.Service;

/// <summary>Verifica, transfere e aplica actualizações Velopack (privilégios elevados do serviço).</summary>
public sealed class UpdateCheckWorker
{
    static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan RequestPollInterval = TimeSpan.FromSeconds(3);

    readonly ILogger<UpdateCheckWorker> _logger;

    public UpdateCheckWorker(ILogger<UpdateCheckWorker> logger) =>
        _logger = logger;

    public async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Update worker activo. Intervalo: {Hours}h.", CheckInterval.TotalHours);
        UpdateActivityLog.Info("Update", "Serviço de actualização iniciado.");
        LogEnvironment();

        await DelayUntilWorkDueAsync(StartupDelay, stoppingToken).ConfigureAwait(false);

        var lastAutomaticCheck = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var installRequested = UpdateServiceCoordinator.TryConsumeInstallRequest();
            var checkRequested = UpdateServiceCoordinator.TryConsumeCheckRequest();
            var shouldApplyPending = ShouldApplyPendingNow();
            var automaticDue = DateTime.UtcNow - lastAutomaticCheck >= CheckInterval;

            if (installRequested)
                UpdateActivityLog.Info("Update", "Pedido de instalação recebido do utilizador.");

            if (checkRequested)
                UpdateActivityLog.Info("Update", "Pedido de verificação recebido.");

            if (shouldApplyPending)
                UpdateActivityLog.Info("Update", "Actualização pendente detectada — app fechado, a aplicar.");

            var shouldRun = installRequested || checkRequested || shouldApplyPending || automaticDue;

            if (shouldRun)
            {
                try
                {
                    await RunCycleAsync(installRequested || shouldApplyPending, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ciclo de actualização falhou.");
                    UpdateActivityLog.LogException("Update", ex, "Ciclo de actualização falhou.");
                    WriteStatus(UpdateServicePhase.Error, ex.Message);
                }

                if (automaticDue && !installRequested && !checkRequested && !shouldApplyPending)
                    lastAutomaticCheck = DateTime.UtcNow;
            }

            var wait = installRequested || checkRequested || shouldApplyPending
                ? TimeSpan.FromSeconds(10)
                : CheckInterval;

            await DelayUntilWorkDueAsync(wait, stoppingToken).ConfigureAwait(false);
        }
    }

    static bool ShouldApplyPendingNow() =>
        VelopackUpdateEngine.PendingRestart is not null && !SistecHubAppProcess.IsRunning();

    async Task DelayUntilWorkDueAsync(TimeSpan maxWait, CancellationToken stoppingToken)
    {
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline && !stoppingToken.IsCancellationRequested)
        {
            if (UpdateServiceCoordinator.HasPendingWorkRequest() || ShouldApplyPendingNow())
                return;

            var remaining = deadline - DateTime.UtcNow;
            var step = remaining < RequestPollInterval ? remaining : RequestPollInterval;
            if (step <= TimeSpan.Zero)
                return;

            await Task.Delay(step, stoppingToken).ConfigureAwait(false);
        }
    }

    static void LogEnvironment()
    {
        UpdateActivityLog.Info(
            "Update",
            $"Velopack={VelopackUpdateEngine.IsInstalled} | Versão={VelopackUpdateEngine.DisplayVersion} | Exe={Environment.ProcessPath}");
    }

    async Task RunCycleAsync(bool userInstallFlow, CancellationToken cancellationToken)
    {
        if (!VelopackUpdateEngine.IsInstalled)
        {
            _logger.LogWarning("Velopack não detectado — actualizações automáticas desactivadas.");
            UpdateActivityLog.Warn("Update", "Velopack não detectado no serviço — actualizações desactivadas.");
            WriteStatus(UpdateServicePhase.Idle, "Instalação Velopack não detectada.");
            return;
        }

        UpdateActivityLog.Info("Update", "A iniciar ciclo de actualização.");
        WriteStatus(UpdateServicePhase.Checking, "A verificar actualizações...", VelopackUpdateEngine.DisplayVersion);

        if (VelopackUpdateEngine.PendingRestart is { } pending)
        {
            UpdateActivityLog.Info("Update", $"Actualização já transferida (pendente): {pending.Version}");
            TryApplyAsync(pending, userInstallFlow, cancellationToken);
            return;
        }

        UpdateInfo? update;
        try
        {
            update = await VelopackUpdateEngine.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao verificar actualizações.");
            UpdateActivityLog.LogException("Update", ex, "Falha ao verificar actualizações.");
            WriteStatus(UpdateServicePhase.Error, "Falha ao verificar actualizações: " + ex.Message);
            return;
        }

        if (update is null)
        {
            _logger.LogInformation("Sem actualizações — versão {Version}.", VelopackUpdateEngine.DisplayVersion);
            UpdateActivityLog.Info("Update", $"Sem actualizações — versão {VelopackUpdateEngine.DisplayVersion}.");
            WriteStatus(
                UpdateServicePhase.UpToDate,
                userInstallFlow ? "Já está na versão mais recente." : "Versão actual instalada.",
                VelopackUpdateEngine.DisplayVersion);
            return;
        }

        var newVersion = update.TargetFullRelease.Version.ToString();
        UpdateActivityLog.Info("Update", $"Nova versão encontrada: {newVersion} (actual: {VelopackUpdateEngine.DisplayVersion}).");
        WriteStatus(
            UpdateServicePhase.Downloading,
            $"A transferir versão {newVersion}...",
            VelopackUpdateEngine.DisplayVersion,
            newVersion);

        try
        {
            await VelopackUpdateEngine.DownloadUpdatesAsync(
                update,
                progress: p =>
                {
                    if (p is 0 or 25 or 50 or 75 or 100)
                        UpdateActivityLog.Info("Update", $"Transferência {newVersion}: {p}%");
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao transferir actualização.");
            UpdateActivityLog.LogException("Update", ex, "Falha ao transferir actualização.");
            WriteStatus(UpdateServicePhase.Error, "Falha ao transferir: " + ex.Message, availableVersion: newVersion);
            return;
        }

        UpdateActivityLog.Info("Update", $"Transferência concluída — versão {newVersion}.");
        TryApplyAsync(update.TargetFullRelease, userInstallFlow, cancellationToken);
    }

    void TryApplyAsync(VelopackAsset asset, bool userInstallFlow, CancellationToken cancellationToken)
    {
        var version = asset.Version.ToString();

        if (SistecHubAppProcess.IsRunning())
        {
            UpdateActivityLog.Info("Update", $"Versão {version} pronta, mas SistecHub.exe ainda está aberto.");
            WriteStatus(
                UpdateServicePhase.PendingAppClose,
                $"Versão {version} pronta. Feche o SistecHub para instalar.",
                VelopackUpdateEngine.DisplayVersion,
                version);
            return;
        }

        UpdateActivityLog.Info("Update", $"A aplicar versão {version}...");
        WriteStatus(
            UpdateServicePhase.Applying,
            $"A instalar versão {version}...",
            VelopackUpdateEngine.DisplayVersion,
            version);

        VelopackUpdateEngine.ScheduleApplyAndExit(asset);
    }

    static void WriteStatus(
        UpdateServicePhase phase,
        string message,
        string? currentVersion = null,
        string? availableVersion = null)
    {
        UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
        {
            LastUpdateUtc = DateTimeOffset.UtcNow,
            Phase = phase,
            Message = message,
            CurrentVersion = currentVersion,
            AvailableVersion = availableVersion,
        });
    }
}
