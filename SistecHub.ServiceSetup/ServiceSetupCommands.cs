using System.Diagnostics;
using System.ServiceProcess;
using SistecHub.Core;

namespace SistecHub.ServiceSetup;

static class ServiceSetupCommands
{
    static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(30);

    public static int Install(string serviceExePath)
    {
        ServiceLogWriter.Info("Setup", $"install — serviço: {serviceExePath}");

        if (!File.Exists(serviceExePath))
        {
            ServiceLogWriter.Error("Setup", $"Executável não encontrado: {serviceExePath}");
            return 2;
        }

        try
        {
            if (ServiceExists())
            {
                ServiceLogWriter.Info("Setup", "Serviço existente — a actualizar binPath.");
                ConfigureServiceBinaryPath(serviceExePath);
            }
            else
            {
                CreateService(serviceExePath);
            }

            SetServiceDescription();
            ConfigureServiceRecovery();
            StartService();
            ServiceLogWriter.Info("Setup", "Serviço instalado e iniciado.");
            return 0;
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Setup", ex, "Falha no install.");
            return 1;
        }
    }

    public static int EnsureAfterUpdate(string serviceExePath)
    {
        ServiceLogWriter.Info("Setup", $"ensure-after-update — serviço: {serviceExePath}");

        if (!File.Exists(serviceExePath))
        {
            ServiceLogWriter.Error("Setup", $"Executável não encontrado: {serviceExePath}");
            return 2;
        }

        try
        {
            if (!ServiceExists())
                return Install(serviceExePath);

            ConfigureServiceBinaryPath(serviceExePath);
            StartService();
            ServiceLogWriter.Info("Setup", "Serviço actualizado e activo.");
            return 0;
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Setup", ex, "Falha no ensure-after-update.");
            return 1;
        }
    }

    public static int Uninstall()
    {
        ServiceLogWriter.Info("Setup", "uninstall");

        if (!ServiceExists())
            return 0;

        try
        {
            StopService();
            var exitCode = RunSc($"delete {WindowsServiceConfig.ServiceName}");
            if (exitCode != 0)
                throw new InvalidOperationException($"sc delete falhou com código {exitCode}.");

            ServiceLogWriter.Info("Setup", "Serviço removido.");
            return 0;
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Setup", ex, "Falha no uninstall.");
            return 1;
        }
    }

    static bool ServiceExists()
    {
        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static void CreateService(string serviceExePath)
    {
        var binPath = QuoteScBinaryPath(serviceExePath);
        var exitCode = RunSc(
            $"create {WindowsServiceConfig.ServiceName} binPath= {binPath} start= auto DisplayName= \"{WindowsServiceConfig.DisplayName}\"");

        if (exitCode != 0)
            throw new InvalidOperationException($"sc create falhou com código {exitCode}.");
    }

    static void ConfigureServiceBinaryPath(string serviceExePath)
    {
        var binPath = QuoteScBinaryPath(serviceExePath);
        var exitCode = RunSc($"config {WindowsServiceConfig.ServiceName} binPath= {binPath} start= auto");

        if (exitCode != 0)
            throw new InvalidOperationException($"sc config falhou com código {exitCode}.");
    }

    static void SetServiceDescription() =>
        RunSc($"description {WindowsServiceConfig.ServiceName} \"{WindowsServiceConfig.Description}\"");

    static void ConfigureServiceRecovery()
    {
        RunSc($"failure {WindowsServiceConfig.ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
    }

    static void StartService()
    {
        using var controller = new ServiceController(WindowsServiceConfig.ServiceName);

        if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            return;

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
    }

    static void StopService()
    {
        using var controller = new ServiceController(WindowsServiceConfig.ServiceName);

        if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
        {
            controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWaitTimeout);
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWaitTimeout);
    }

    static string QuoteScBinaryPath(string path) => $"\"{path}\"";

    static int RunSc(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (process is null)
            return -1;

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        if (!string.IsNullOrWhiteSpace(output))
            ServiceLogWriter.Info("SC", output.Trim());

        if (!string.IsNullOrWhiteSpace(error))
            ServiceLogWriter.Warn("SC", error.Trim());

        return process.ExitCode;
    }
}
