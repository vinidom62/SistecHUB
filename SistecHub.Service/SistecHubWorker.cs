using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistecHub.Core;

namespace SistecHub.Service;

public sealed class SistecHubWorker : BackgroundService
{
    readonly ILogger<SistecHubWorker> _logger;
    readonly UpdateCheckWorker _updateWorker;
    readonly InventarioWorker _inventarioWorker;

    public SistecHubWorker(
        ILogger<SistecHubWorker> logger,
        UpdateCheckWorker updateWorker,
        InventarioWorker inventarioWorker)
    {
        _logger = logger;
        _updateWorker = updateWorker;
        _inventarioWorker = inventarioWorker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Serviço {ServiceName} activo. SO: {OsVersion} | Conta: {User}",
            WindowsServiceConfig.ServiceName,
            Environment.OSVersion.VersionString,
            Environment.UserName);

        var updateTask = _updateWorker.RunLoopAsync(stoppingToken);
        var reopenTask = PostUpdateAppReopen.RunRecoveryLoopAsync(stoppingToken);
        var inventarioTask = _inventarioWorker.RunLoopAsync(stoppingToken);

        try
        {
            await Task.WhenAll(updateTask, reopenTask, inventarioTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal.
        }

        _logger.LogInformation("Serviço {ServiceName} a encerrar.", WindowsServiceConfig.ServiceName);
    }
}
