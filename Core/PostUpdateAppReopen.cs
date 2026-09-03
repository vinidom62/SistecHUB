namespace SistecHub.Core;

/// <summary>Relança o SistecHub na sessão do utilizador após actualização aplicada pelo serviço.</summary>
public static class PostUpdateAppReopen
{
    static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    public static async Task RunRecoveryLoopAsync(CancellationToken stoppingToken)
    {
        await TryReopenOnceAsync().ConfigureAwait(false);

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

            await TryReopenOnceAsync().ConfigureAwait(false);
        }
    }

    public static async Task TryReopenOnceAsync()
    {
        if (!UpdateServiceCoordinator.HasReopenAppRequest())
            return;

        var version = TryReadReopenRequestVersion();

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

        // 1. Dá prioridade ao watcher da sessão do utilizador e ignora a Sessão 0
        // (onde os hooks transitórios do Velopack como --veloapp-updated executam).
        if (SistecHubAppProcess.IsRunning(onlyInteractiveSession: true))
        {
            UpdateActivityLog.Info("Update", "SistecHub detectado na sessão do utilizador após actualização — pedido de reabertura concluído.");
            MarkReopenCompleted(version);
            return;
        }

        // Aguarda 4 segundos para dar tempo ao watcher de sessão do usuário iniciar o processo
        await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

        if (SistecHubAppProcess.IsRunning(onlyInteractiveSession: true))
        {
            UpdateActivityLog.Info("Update", "SistecHub relançado com sucesso pelo watcher do utilizador — pedido de reabertura concluído.");
            MarkReopenCompleted(version);
            return;
        }

        // 2. Fallback: se o watcher não iniciou, o serviço tenta relançar via sessão ativa do utilizador
        if (InteractiveUserAppLauncher.TryLaunchMainAppInActiveSession("serviço pós-actualização"))
            MarkReopenCompleted(version);
    }

    static void MarkReopenCompleted(string? version)
    {
        UpdateServiceCoordinator.ClearReopenAppRequest();
        UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
        {
            Phase = UpdateServicePhase.Completed,
            Message = string.IsNullOrWhiteSpace(version)
                ? "Atualização concluída — SistecHub reaberto."
                : $"Atualização concluída — versão {version}.",
            CurrentVersion = version,
            AvailableVersion = version,
        });
    }

    static string? TryReadReopenRequestVersion()
    {
        try
        {
            var path = UpdateServiceCoordinator.ReopenAppRequestFilePath;
            if (!File.Exists(path))
                return null;

            var text = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Fallback do pedido: timestamp ISO em vez da versão.
            if (DateTimeOffset.TryParse(text, out _))
                return null;

            return text;
        }
        catch
        {
            return null;
        }
    }
}
