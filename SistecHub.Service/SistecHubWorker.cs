using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistecHub.Core;

namespace SistecHub.Service;

public sealed class SistecHubWorker : BackgroundService
{
    readonly ILogger<SistecHubWorker> _logger;
    readonly UpdateCheckWorker _updateWorker;

    public SistecHubWorker(ILogger<SistecHubWorker> logger, UpdateCheckWorker updateWorker)
    {
        _logger = logger;
        _updateWorker = updateWorker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Serviço {ServiceName} activo. SO: {OsVersion} | Conta: {User}",
            WindowsServiceConfig.ServiceName,
            Environment.OSVersion.VersionString,
            Environment.UserName);

        var updateTask = _updateWorker.RunLoopAsync(stoppingToken);

        try
        {
            await updateTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal.
        }

        _logger.LogInformation("Serviço {ServiceName} a encerrar.", WindowsServiceConfig.ServiceName);
    }
}
