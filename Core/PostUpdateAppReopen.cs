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

    public static Task TryReopenOnceAsync()
    {
        if (!UpdateServiceCoordinator.HasReopenAppRequest())
            return Task.CompletedTask;

        var version = TryReadReopenRequestVersion();

        if (SistecHubAppProcess.IsRunning())
        {
            UpdateActivityLog.Info("Update", "SistecHub detectado após actualização — pedido de reabertura concluído.");
            MarkReopenCompleted(version);
            return Task.CompletedTask;
        }

        if (InteractiveUserAppLauncher.TryLaunchMainAppInActiveSession("serviço pós-actualização"))
            MarkReopenCompleted(version);

        return Task.CompletedTask;
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
