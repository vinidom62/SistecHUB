using Microsoft.Extensions.Hosting;
using SistecHub.Core;
using Velopack;

namespace SistecHub.Service;

/// <summary>Verifica, transfere e aplica actualizações Velopack (privilégios elevados do serviço).</summary>
public sealed class UpdateCheckWorker
{
    static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan RequestPollInterval = TimeSpan.FromSeconds(1);

    readonly ILogger<UpdateCheckWorker> _logger;
    readonly IHostApplicationLifetime _appLifetime;

    public UpdateCheckWorker(ILogger<UpdateCheckWorker> logger, IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _appLifetime = appLifetime;
    }

    public async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Update worker activo. Intervalo: {Hours}h.", CheckInterval.TotalHours);
        UpdateActivityLog.Info("Update", "Serviço de actualização iniciado.");
        LogEnvironment();
        NormalizeStatusAfterStartupIfNeeded();

        await DelayUntilWorkDueAsync(StartupDelay, stoppingToken).ConfigureAwait(false);

        var lastAutomaticCheck = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var installRequested = UpdateServiceCoordinator.TryConsumeInstallRequest();
            var checkRequested = UpdateServiceCoordinator.TryConsumeCheckRequest();
            var betaCheckRequested = UpdateServiceCoordinator.TryConsumeBetaCheckRequest();
            var shouldApplyPending = ShouldApplyPendingNow();
            var automaticDue = DateTime.UtcNow - lastAutomaticCheck >= CheckInterval;

            if (installRequested)
                UpdateActivityLog.Info("Update", "Pedido de instalação recebido do utilizador.");

            if (checkRequested)
                UpdateActivityLog.Info("Update", "Pedido de verificação estável recebido.");

            if (betaCheckRequested)
                UpdateActivityLog.Info("Update", "Pedido de verificação Beta (pré-releases) recebido.");

            if (shouldApplyPending)
                UpdateActivityLog.Info("Update", "Actualização pendente detectada — app fechado, a aplicar.");

            var shouldRun = installRequested || checkRequested || betaCheckRequested
                || shouldApplyPending || automaticDue;

            if (shouldRun)
            {
                try
                {
                    var isExplicitInstall = installRequested || shouldApplyPending;

                    await RunCycleAsync(
                            userInstallFlow: isExplicitInstall,
                            includePrerelease: betaCheckRequested,
                            stoppingToken)
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

                if (automaticDue
                    && !installRequested
                    && !checkRequested
                    && !betaCheckRequested
                    && !shouldApplyPending)
                    lastAutomaticCheck = DateTime.UtcNow;

                MemoryOptimizer.TrimWorkingSet();
            }

            var wait = installRequested || checkRequested || betaCheckRequested || shouldApplyPending
                ? TimeSpan.FromSeconds(10)
                : CheckInterval;

            await DelayUntilWorkDueAsync(wait, stoppingToken).ConfigureAwait(false);
        }
    }

    static bool ShouldApplyPendingNow() =>
        VelopackUpdateEngine.PendingRestart is { } pending
        && !string.Equals(VelopackUpdateEngine.DisplayVersion, pending.Version.ToString(), StringComparison.OrdinalIgnoreCase)
        && !SistecHubAppProcess.IsRunning();

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

    static void NormalizeStatusAfterStartupIfNeeded()
    {
        var status = UpdateServiceCoordinator.TryReadStatus();
        if (status is null)
            return;

        if (status.Phase is UpdateServicePhase.Applying or UpdateServicePhase.PendingAppClose
            && !string.IsNullOrWhiteSpace(status.AvailableVersion)
            && string.Equals(status.AvailableVersion, VelopackUpdateEngine.DisplayVersion, StringComparison.OrdinalIgnoreCase))
        {
            var currentVer = VelopackUpdateEngine.DisplayVersion;
            UpdateActivityLog.Info("Update", $"Serviço iniciado já na versão {currentVer} — a normalizar status pós-atualização para Completed.");
            WriteStatus(
                UpdateServicePhase.Completed,
                $"Atualização concluída — versão {currentVer}.",
                currentVer,
                currentVer,
                100);
        }
    }

    async Task RunCycleAsync(bool userInstallFlow, bool includePrerelease, CancellationToken cancellationToken)
    {
        if (!VelopackUpdateEngine.IsInstalled)
        {
            _logger.LogWarning("Velopack não detectado — actualizações automáticas desactivadas.");
            UpdateActivityLog.Warn("Update", "Velopack não detectado no serviço — actualizações desactivadas.");
            WriteStatus(UpdateServicePhase.Idle, "Instalação Velopack não detectada.");
            return;
        }

        var channel = includePrerelease ? "Beta (pré-releases)" : "estável";
        UpdateActivityLog.Info("Update", $"A iniciar ciclo de actualização ({channel}).");

        if (VelopackUpdateEngine.PendingRestart is { } pending
            && !string.Equals(VelopackUpdateEngine.DisplayVersion, pending.Version.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            UpdateActivityLog.Info("Update", $"Actualização já transferida (pendente): {pending.Version}");
            await TryApplyAsync(pending, userInstallFlow, cancellationToken).ConfigureAwait(false);
            return;
        }

        WriteStatus(
            UpdateServicePhase.Checking,
            includePrerelease ? "A verificar atualizações Beta..." : "A verificar actualizações...",
            VelopackUpdateEngine.DisplayVersion);

        UpdateInfo? update;
        try
        {
            update = await VelopackUpdateEngine
                .CheckForUpdatesAsync(includePrerelease, cancellationToken)
                .ConfigureAwait(false);
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
            UpdateActivityLog.Info(
                "Update",
                $"Sem actualizações ({channel}) — versão {VelopackUpdateEngine.DisplayVersion}.");
            WriteStatus(
                UpdateServicePhase.UpToDate,
                includePrerelease
                    ? "Sem pré-releases mais recentes."
                    : userInstallFlow
                        ? "Já está na versão mais recente."
                        : "Versão actual instalada.",
                VelopackUpdateEngine.DisplayVersion);
            return;
        }

        var newVersion = update.TargetFullRelease.Version.ToString();
        UpdateActivityLog.Info(
            "Update",
            $"Nova versão encontrada ({channel}): {newVersion} (actual: {VelopackUpdateEngine.DisplayVersion}).");
        WriteStatus(
            UpdateServicePhase.Downloading,
            includePrerelease
                ? $"A transferir versão Beta {newVersion}..."
                : $"A transferir versão {newVersion}...",
            VelopackUpdateEngine.DisplayVersion,
            newVersion,
            progressPercent: 0);

        try
        {
            var lastReportedProgress = -1;
            await VelopackUpdateEngine.DownloadUpdatesAsync(
                update,
                includePrerelease,
                progress: p =>
                {
                    var clamped = Math.Clamp(p, 0, 100);
                    if (clamped != lastReportedProgress)
                    {
                        lastReportedProgress = clamped;
                        var isMilestone = clamped is 0 or 25 or 50 or 75 or 100;
                        WriteStatus(
                            UpdateServicePhase.Downloading,
                            includePrerelease
                                ? $"A transferir versão Beta {newVersion} ({clamped}%)..."
                                : $"A transferir versão {newVersion} ({clamped}%)...",
                            VelopackUpdateEngine.DisplayVersion,
                            newVersion,
                            progressPercent: clamped,
                            logActivity: isMilestone);
                    }
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
        await TryApplyAsync(update.TargetFullRelease, userInstallFlow, cancellationToken).ConfigureAwait(false);
    }

    async Task TryApplyAsync(VelopackAsset asset, bool userInstallFlow, CancellationToken cancellationToken)
    {
        var version = asset.Version.ToString();

        // Se a versão do asset já é a versão atual em execução/instalada, marca como concluída e evita loop
        if (string.Equals(VelopackUpdateEngine.DisplayVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            UpdateActivityLog.Info("Update", $"Versão {version} já se encontra aplicada no disco. A marcar atualização como concluída.");
            WriteStatus(
                UpdateServicePhase.Completed,
                $"Atualização concluída — versão {version}.",
                version,
                version,
                100);
            return;
        }

        // Se o utilizador solicitou a instalação (ou fechou após o aviso de contagem regressiva),
        // aguarda o encerramento gracioso do SistecHub.
        if (userInstallFlow)
        {
            UpdateActivityLog.Info("Update", $"Instalação da versão {version} iniciada. A aguardar encerramento do SistecHub...");
            var exitedCleanly = await SistecHubAppProcess.WaitForAllExitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);

            if (!exitedCleanly)
            {
                UpdateActivityLog.Warn("Update", "Existem processos SistecHub.exe ainda em execução após tolerância de fecho. A terminar instâncias para proceder à actualização.");
                SistecHubAppProcess.TerminateAll();
                await SistecHubAppProcess.WaitForAllExitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
        }

        if (SistecHubAppProcess.IsRunning())
        {
            UpdateActivityLog.Info("Update", $"Versão {version} pronta, mas SistecHub.exe ainda está aberto.");
            WriteStatus(
                UpdateServicePhase.PendingAppClose,
                $"Versão {version} pronta. Feche o SistecHub para instalar.",
                VelopackUpdateEngine.DisplayVersion,
                version,
                100);
            return;
        }

        UpdateActivityLog.Info("Update", $"A aplicar versão {version}...");
        WriteStatus(
            UpdateServicePhase.Applying,
            $"A instalar versão {version}...",
            VelopackUpdateEngine.DisplayVersion,
            version,
            100);

        // Suspende e encerra o processo auxiliar --remoto-system-helper e qualquer instância residual
        // para que o Update.exe possa substituir todos os ficheiros em current/ sem bloqueios.
        RemotoSasCoordinator.SuspendAndStopHelperForUpdate();
        SistecHubAppProcess.TerminateAll(includeSystemHelper: true);
        await SistecHubAppProcess.WaitForAllExitAsync(TimeSpan.FromSeconds(3), cancellationToken, includeSystemHelper: true)
            .ConfigureAwait(false);

        try
        {
            VelopackUpdateEngine.ScheduleApplyAndExit(asset);
            UpdateActivityLog.Info("Update", "Update.exe iniciado. A encerrar o serviço de forma limpa (SERVICE_STOPPED) para permitir a substituição dos ficheiros.");
            _appLifetime.StopApplication();
        }
        catch
        {
            RemotoSasCoordinator.ResumeHelperAfterUpdateFailure();
            throw;
        }
    }

    static void WriteStatus(
        UpdateServicePhase phase,
        string message,
        string? currentVersion = null,
        string? availableVersion = null,
        int progressPercent = 0,
        bool logActivity = true)
    {
        UpdateServiceCoordinator.WriteStatus(
            new UpdateServiceStatus
            {
                LastUpdateUtc = DateTimeOffset.UtcNow,
                Phase = phase,
                ProgressPercent = progressPercent,
                Message = message,
                CurrentVersion = currentVersion,
                AvailableVersion = availableVersion,
            },
            logActivity);
    }
}
