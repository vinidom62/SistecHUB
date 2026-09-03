using System.ServiceProcess;
using SistecHub.UI;

namespace SistecHub.Core;

/// <summary>Exige que o serviço Windows esteja activo antes de executar o SistecHub (instalações MSI).</summary>
public static class WindowsServiceGuard
{
    static readonly TimeSpan ServiceWaitTimeout = TimeSpan.FromSeconds(15);

    public static bool IsRequiredForCurrentInstall =>
        OperatingSystem.IsWindows() && AppUpdateService.IsUpdateSupported;

    public static bool ServiceExists()
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

    public static bool IsRunning()
    {
        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            return controller.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static void EnsureRunningOrExit()
    {
        if (!IsRequiredForCurrentInstall)
            return;

        if (!ServiceExists())
        {
            ShowBlockedMessage(
                "O serviço SistecHub Service não está instalado.\n\n"
                + "Reinstale o SistecHub usando o instalador MSI (.msi) e aceite a permissão de administrador (UAC).");
            Environment.Exit(1);
        }

        if (IsRunning())
            return;

        if (UpdateServiceCoordinator.IsServiceRecoveryLikelyUpdateRelated())
        {
            ServiceLogWriter.Info("App", "Serviço indisponível durante recuperação de actualização — a aguardar.");

            var isSilent = Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "-minimized", StringComparison.OrdinalIgnoreCase));

            if (isSilent)
            {
                // Em arranque silencioso/minimizado, aguarda o serviço em segundo plano sem abrir nenhuma janela na tela
                for (var i = 0; i < 20 && !IsRunning(); i++)
                {
                    Thread.Sleep(1000);
                }

                if (IsRunning())
                    return;
            }
            else
            {
                using var waitForm = new ServiceStartupWaitForm();
                if (waitForm.ShowDialog() == DialogResult.OK && IsRunning())
                    return;
            }
        }

        ServiceLogWriter.Warn("App", "Serviço parado — a tentar iniciar...");
        AppDebugLog.Warn("App", "Serviço SistecHubService parado; a tentar iniciar.");

        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, ServiceWaitTimeout);
        }
        catch (Exception ex)
        {
            ServiceLogWriter.LogException("App", ex, "Não foi possível iniciar o serviço.");
            AppDebugLog.LogException("App", ex, "Falha ao iniciar SistecHubService.");
        }

        if (IsRunning())
            return;

        ShowBlockedMessage(
            "O serviço SistecHub Service não está em execução.\n\n"
            + "Se acabou de atualizar, aguarde um momento e abra o SistecHub novamente.\n\n"
            + "Caso contrário, abra «Serviços» (services.msc) e inicie «SistecHub Service».");
        Environment.Exit(1);
    }

    static void ShowBlockedMessage(string message)
    {
        try
        {
            MessageBox.Show(
                message,
                "SistecHub — serviço obrigatório",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Sem UI disponível (ex.: hook Velopack).
        }
    }
}
