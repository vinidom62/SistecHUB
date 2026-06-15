using Microsoft.Extensions.DependencyInjection;

using Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Logging;

using SistecHub.Core;

using SistecHub.Service.Logging;



namespace SistecHub.Service;



static class Program

{

    public static void Main(string[] args)

    {

        var builder = Host.CreateApplicationBuilder(args);



        builder.Services.AddWindowsService(options =>

        {

            options.ServiceName = WindowsServiceConfig.ServiceName;

        });



        builder.Services.AddSingleton<UpdateCheckWorker>();

        builder.Services.AddHostedService<SistecHubWorker>();



        builder.Logging.ClearProviders();

        builder.Logging.SetMinimumLevel(LogLevel.Debug);

        builder.Logging.AddProvider(new ServiceFileLoggerProvider());



        if (OperatingSystem.IsWindows())

        {

            builder.Logging.AddEventLog(settings =>

            {

                settings.SourceName = WindowsServiceConfig.DisplayName;

            });

        }



        var host = builder.Build();



        var startupLogger = host.Services

            .GetRequiredService<ILoggerFactory>()

            .CreateLogger("Startup");



        startupLogger.LogInformation(

            "SistecHub Service a arrancar. Log: {LogFilePath} | Versão: {Version} | Exe: {ExePath}",

            ServiceLogWriter.LogFilePath,

            typeof(Program).Assembly.GetName().Version?.ToString() ?? "?",

            Environment.ProcessPath ?? "(desconhecido)");



        host.Run();

    }

}

