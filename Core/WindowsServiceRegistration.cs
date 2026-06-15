using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace SistecHub.Core;

/// <summary>Regista, actualiza e remove o serviço Windows via <c>SistecHub.ServiceSetup.exe</c> (elevado).</summary>
internal static class WindowsServiceRegistration
{
    const int ErrorCancelled = 1223;
    static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(30);

    public static void EnsureRegisteredOrFail()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var serviceExe = ResolveServiceExecutablePath();
        if (serviceExe is null)
            throw new WindowsServiceSetupFailedException("SistecHub.Service.exe não encontrado na pasta de instalação.");

        if (!PerMachineUpdatePermissions.IsPerMachinePath(serviceExe))
            return;

        ServiceLogWriter.Info("Install", $"A registar serviço (obrigatório). Executável: {serviceExe}");

        RunServiceSetupOrFail("install", $"install --service-exe \"{serviceExe}\"");

        if (!WindowsServiceGuard.IsRunning())
        {
            throw new WindowsServiceSetupFailedException(
                "O serviço SistecHub Service não ficou em execução após a instalação.");
        }

        ServiceLogWriter.Info("Install", "Serviço registado, em execução e validado.");
    }

    /// <summary>Paragem best-effort durante hooks Velopack (sem UAC — o serviço já pode ter parado).</summary>
    public static void TryStopServiceForUpdate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        TryStopServiceBestEffort();
    }

    /// <summary>Reinício best-effort após hooks Velopack (SCM recovery também reinicia o serviço).</summary>
    public static void TryEnsureServiceRunningAfterUpdate()
    {
        if (!OperatingSystem.IsWindows())
            return;

        TryStartServiceBestEffort();

        if (WindowsServiceGuard.IsRunning())
            return;

        var serviceExe = ResolveServiceExecutablePath();
        if (serviceExe is null || !WindowsServiceGuard.ServiceExists())
            return;

        ServiceLogWriter.Warn("Update", "Serviço parado após actualização — a sincronizar binPath.");
        TryRunServiceSetupBestEffort($"ensure-after-update --service-exe \"{serviceExe}\"");
    }

    static void TryStopServiceBestEffort()
    {
        if (!WindowsServiceGuard.ServiceExists())
            return;

        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
                return;

            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWaitTimeout);
            ServiceLogWriter.Info("Update", "Serviço parado.");
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Update", ex, "Paragem best-effort do serviço (pode já estar parado).");
        }
    }

    static void TryStartServiceBestEffort()
    {
        if (!WindowsServiceGuard.ServiceExists())
        {
            EnsureRegisteredOrFail();
            return;
        }

        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                return;

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
            ServiceLogWriter.Info("Update", "Serviço reiniciado após actualização.");
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Update", ex, "Reinício best-effort do serviço (SCM recovery pode actuar).");
        }
    }

    public static void Uninstall()
    {
        if (!OperatingSystem.IsWindows() || !WindowsServiceGuard.ServiceExists())
            return;

        ServiceLogWriter.Info("Uninstall", "A remover serviço Windows...");

        try
        {
            RunServiceSetupOrFail("uninstall", "uninstall");
            ServiceLogWriter.Info("Uninstall", "Serviço removido.");
        }
        catch (WindowsServiceSetupFailedException ex)
        {
            ServiceLogWriter.LogException("Uninstall", ex, "Falha ao remover serviço.");
        }
    }

    static void RunServiceSetupOrFail(string action, string arguments)
    {
        try
        {
            var exitCode = RunServiceSetup(arguments);
            if (exitCode == 0)
                return;

            throw new WindowsServiceSetupFailedException(
                $"SistecHub.ServiceSetup.exe ({action}) terminou com código {exitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new WindowsServiceSetupFailedException(
                "Permissão de administrador (UAC) recusada. O serviço SistecHub é obrigatório.",
                userCancelledElevation: true,
                inner: ex);
        }
    }

    static string? ResolveServiceExecutablePath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var exeDir = Path.GetDirectoryName(exePath);
        if (exeDir is null)
            return null;

        var serviceExe = Path.Combine(exeDir, WindowsServiceConfig.ExecutableFileName);
        return File.Exists(serviceExe) ? serviceExe : null;
    }

    static string? ResolveServiceSetupPath()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var exeDir = Path.GetDirectoryName(exePath);
        if (exeDir is null)
            return null;

        var setupExe = Path.Combine(exeDir, WindowsServiceConfig.ServiceSetupFileName);
        return File.Exists(setupExe) ? setupExe : null;
    }

    static void TryRunServiceSetupBestEffort(string arguments)
    {
        try
        {
            var exitCode = RunServiceSetup(arguments, requireElevation: false);
            if (exitCode != 0)
                ServiceLogWriter.Warn("Setup", $"ServiceSetup ({arguments}) terminou com código {exitCode}.");
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("Setup", ex, "ServiceSetup best-effort falhou.");
        }
    }

    static int RunServiceSetup(string arguments, bool requireElevation = true)
    {
        var setupPath = ResolveServiceSetupPath()
            ?? throw new WindowsServiceSetupFailedException("SistecHub.ServiceSetup.exe não encontrado.");

        ServiceLogWriter.Info("Setup", $"A invocar{(requireElevation ? " (UAC)" : "")}: {setupPath} {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = arguments,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (requireElevation && !IsProcessElevated())
            startInfo.Verb = "runas";

        using var process = Process.Start(startInfo)
            ?? throw new WindowsServiceSetupFailedException("Não foi possível iniciar SistecHub.ServiceSetup.exe.");

        process.WaitForExit(60_000);
        return process.ExitCode;
    }

    static bool IsProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
