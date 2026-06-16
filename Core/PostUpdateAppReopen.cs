namespace SistecHub.Core;

/// <summary>Relança o SistecHub após actualização aplicada pelo serviço.</summary>
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

    public static Task TryReopenOnceAsync()
    {
        if (!UpdateServiceCoordinator.HasReopenAppRequest())
            return Task.CompletedTask;

        if (SistecHubAppProcess.IsRunning())
        {
            UpdateActivityLog.Info("Update", "SistecHub detectado após actualização — pedido de reabertura concluído.");
            UpdateServiceCoordinator.ClearReopenAppRequest();
            return Task.CompletedTask;
        }

        if (InteractiveUserAppLauncher.TryLaunchMainAppInActiveSession("serviço pós-actualização"))
            UpdateServiceCoordinator.ClearReopenAppRequest();

        return Task.CompletedTask;
    }
}
