namespace SistecHub.Core;

/// <summary>Relança o SistecHub na sessão do utilizador após actualização aplicada pelo serviço.</summary>
public static class PostUpdateAppReopen
{
    static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    public static async Task RunRecoveryLoopAsync(CancellationToken stoppingToken)
    {
        await TryReopenOnceAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RetryInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!UpdateServiceCoordinator.HasReopenAppRequest())
                continue;

            await TryReopenOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public static async Task TryReopenOnceAsync(CancellationToken stoppingToken = default)
    {
        if (stoppingToken.IsCancellationRequested || !UpdateServiceCoordinator.HasReopenAppRequest())
            return;

        if (UpdateServiceCoordinator.HasPendingWorkRequest())
            return;

        if (!UpdateServiceCoordinator.TryReadReopenAppRequest(out var version, out var startMinimized))
            return;

        // Se o serviço ainda está a verificar, descarregar ou aplicar o update,
        // NÃO relançar ainda — aguardar o ciclo terminar completamente.
        var status = UpdateServiceCoordinator.TryReadStatus();
        if (status?.Phase is UpdateServicePhase.Checking
            or UpdateServicePhase.Downloading
            or UpdateServicePhase.Applying
            or UpdateServicePhase.PendingAppClose)
        {
            return;
        }

        // Se a versão pretendida ainda não foi aplicada no disco, aguarda o Velopack concluir.
        if (!string.IsNullOrWhiteSpace(version)
            && !string.Equals(VelopackUpdateEngine.DisplayVersion, version, StringComparison.OrdinalIgnoreCase)
            && status?.Phase != UpdateServicePhase.Completed
            && status?.Phase != UpdateServicePhase.Error)
        {
            return;
        }

        // Se o instalador Update.exe do Velopack ainda estiver em execução, aguarda terminar.
        try
        {
            if (System.Diagnostics.Process.GetProcessesByName("Update").Length > 0)
                return;
        }
        catch
        {
            // Ignora falha ao consultar processos.
        }

        // 1. Dá prioridade ao watcher da sessão do utilizador e ignora a Sessão 0
        // (onde os hooks transitórios do Velopack como --veloapp-updated executam).
        if (SistecHubAppProcess.IsRunning(onlyInteractiveSession: true))
        {
            UpdateActivityLog.Info("Update", "SistecHub detectado na sessão do utilizador após actualização — pedido de reabertura concluído.");
            MarkReopenCompleted(version);
            return;
        }

        // Aguarda 10 segundos para dar tempo ao watcher de sessão do utilizador finalizar a tela de progresso e iniciar o processo
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested || UpdateServiceCoordinator.HasPendingWorkRequest())
            return;

        if (SistecHubAppProcess.IsRunning(onlyInteractiveSession: true))
        {
            UpdateActivityLog.Info("Update", "SistecHub relançado com sucesso pelo watcher do utilizador — pedido de reabertura concluído.");
            MarkReopenCompleted(version);
            return;
        }

        // 2. Fallback: se o watcher não iniciou, o serviço tenta relançar via sessão ativa do utilizador
        if (InteractiveUserAppLauncher.TryLaunchMainAppInActiveSession("serviço pós-actualização", forceMinimized: startMinimized))
            MarkReopenCompleted(version);
    }

    static void MarkReopenCompleted(string? version)
    {
        UpdateServiceCoordinator.ClearReopenAppRequest();
        var existing = UpdateServiceCoordinator.TryReadStatus();
        if (existing?.Phase == UpdateServicePhase.Error)
            return;

        var currentVer = VelopackUpdateEngine.DisplayVersion;
        var resolvedVer = !string.IsNullOrWhiteSpace(version) ? version : currentVer;

        UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
        {
            Phase = UpdateServicePhase.Completed,
            ProgressPercent = 100,
            Message = string.IsNullOrWhiteSpace(resolvedVer)
                ? "Atualização concluída — SistecHub reaberto."
                : $"Atualização concluída — versão {resolvedVer}.",
            CurrentVersion = currentVer,
            AvailableVersion = resolvedVer,
        });
    }
}
