using SistecHub.Core;
using SistecHub.UI;
using Velopack;

namespace SistecHub;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ =>
            {
                try
                {
                    WindowsServiceRegistration.EnsureRegisteredOrFail();
                    WindowsStartupRegistration.EnsureRegistered();
                }
                catch (WindowsServiceSetupFailedException ex)
                {
                    var reason = ex.UserCancelledElevation
                        ? "UAC recusado — serviço obrigatório não registado."
                        : ex.Message;
                    VelopackInstallRollback.TryUninstallSilently(reason);
                    throw;
                }
            })
            .OnBeforeUpdateFastCallback(v =>
            {
                UpdateActivityLog.Info("Update", $"Hook OnBeforeUpdate — versão actual {v}.");
                WindowsServiceRegistration.TryStopServiceForUpdate();
            })
            .OnAfterUpdateFastCallback(v =>
            {
                UpdateActivityLog.Info("Update", $"Hook OnAfterUpdate — versão nova {v}.");
                WindowsServiceRegistration.TryEnsureServiceRunningAfterUpdate();

                if (SistecHubAppLauncher.TryStartMainApp("hook OnAfterUpdate"))
                {
                    UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
                    {
                        Phase = UpdateServicePhase.Completed,
                        Message = $"Actualização concluída — versão {v}.",
                        AvailableVersion = v.ToString(),
                        CurrentVersion = v.ToString(),
                    });
                }
                else
                {
                    UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
                    {
                        Phase = UpdateServicePhase.Error,
                        Message = "Actualização instalada, mas o SistecHub não pôde ser aberto automaticamente.",
                        AvailableVersion = v.ToString(),
                    });
                }
            })
            .OnRestarted(v => UpdateActivityLog.Info("Update", $"Hook OnRestarted — versão {v}."))
            .OnBeforeUninstallFastCallback(_ => WindowsServiceRegistration.Uninstall())
            .Run();

        if (!SingleInstanceApp.TryEnterFirstInstance())
        {
            AppDebugLog.InstallGlobalHandlers();
            AppDebugLog.Warn("App", "Segunda instância detetada — a activar janela existente.");
            SingleInstanceApp.TryActivateExisting();
            return;
        }

        try
        {
            AppDebugLog.InstallGlobalHandlers();
            ApplicationConfiguration.Initialize();
            AppDebugLog.LogStartupContext();
            ShowLastUpdateResultIfNeeded();
            WindowsServiceGuard.EnsureRunningOrExit();
            WindowsStartupRegistration.EnsureRegistered();

            if (!AppSettingsStore.IsInitialSetupComplete())
            {
                using var setup = new InitialSetupForm();
                if (setup.ShowDialog() != DialogResult.OK)
                    return;
            }

            Application.Run(new MainForm());
        }
        finally
        {
            SingleInstanceApp.ReleaseFirstInstance();
        }
    }

    static void ShowLastUpdateResultIfNeeded()
    {
        var status = UpdateServiceCoordinator.TryReadStatus();
        if (status is null)
            return;

        if (status.Phase == UpdateServicePhase.Error)
        {
            MessageBox.Show(
                "A última actualização falhou:\n\n" + status.Message
                + "\n\nConsulte update.log em Modo Debug ou em ProgramData\\SistecHub.",
                "Actualização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (status.Phase == UpdateServicePhase.Completed)
            UpdateActivityLog.Info("Update", status.Message);
    }
}
