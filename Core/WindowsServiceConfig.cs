namespace SistecHub.Core;

/// <summary>Identificadores do serviço Windows (fonte única para app, serviço e ServiceSetup).</summary>
public static class WindowsServiceConfig
{
    public const string ServiceName = "SistecHubService";

    public const string DisplayName = "SistecHub Service";

    public const string Description =
        "Serviço de suporte do SistecHub para tarefas em background (ex.: atualizações automáticas).";

    public const string ExecutableFileName = "SistecHub.Service.exe";

    public const string ServiceSetupFileName = "SistecHub.ServiceSetup.exe";

    public const string LogFileName = "service.log";
}
