using SistecHub.Core;
using SistecHub.UI;
using Velopack;

namespace SistecHub;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            // Define o contexto de DPI como PerMonitorV2 antes de qualquer inicialização de UI ou GDI
            SetProcessDpiAwarenessContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch { }

        // Força renderização em software para WPF para evitar corrupção de swapchain DirectX
        // e artefatos de renderização fantasma ao arrastar janelas entre múltiplos monitores.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        if (args.Any(a => string.Equals(a, "--remoto-system-helper", StringComparison.OrdinalIgnoreCase)))
        {
            SistecHub.Modulos.AcessoRemoto.AcessoRemotoSystemHelper.RunServer();
            return;
        }

        var connectTarget = ExtractRemoteTargetId(args);
        if (!string.IsNullOrWhiteSpace(connectTarget))
        {
            AppDebugLog.InstallGlobalHandlers();
            ApplicationConfiguration.Initialize();
            var settings = AppSettingsStore.Load();
            var serverUrl = settings.RemotoServerUrl;

            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            }

            var viewer = new SistecHub.Modulos.AcessoRemoto.AcessoRemotoViewerWindow(serverUrl, connectTarget, initialPassword: "");
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(viewer);
            viewer.ShowDialog();
            return;
        }

        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ =>
            {
                try
                {
                    WindowsServiceRegistration.EnsureRegisteredOrFail();
                    WindowsStartupRegistration.EnsureRegistered();
                    WindowsUriSchemeRegistration.EnsureRegistered();
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
                // Não relançar a UI aqui: o hook corre fora da sessão do utilizador.
                // O watcher na sessão do utilizador (ou o serviço via InteractiveUserAppLauncher) relança a UI.
                UpdateActivityLog.Info(
                    "Update",
                    $"Hook OnAfterUpdate — versão nova {v}. A reiniciar serviço; reabertura da UI fica a cargo do watcher/serviço.");
                WindowsServiceRegistration.TryEnsureServiceRunningAfterUpdate();
                WindowsDesktopShortcutRegistration.EnsureRegistered(force: true);
                WindowsStartupRegistration.EnsureRegistered();
                WindowsUriSchemeRegistration.EnsureRegistered();
                UpdateServiceCoordinator.WriteStatus(new UpdateServiceStatus
                {
                    Phase = UpdateServicePhase.Completed,
                    ProgressPercent = 100,
                    Message = $"Atualização concluída — versão {v}. A reabrir o SistecHub...",
                    AvailableVersion = v.ToString(),
                    CurrentVersion = v.ToString(),
                });
            })
            .OnRestarted(v => UpdateActivityLog.Info("Update", $"Hook OnRestarted — versão {v}."))
            .OnBeforeUninstallFastCallback(_ =>
            {
                WindowsServiceRegistration.Uninstall();
                WindowsStartupRegistration.RemoveStartupShortcut();
                WindowsDesktopShortcutRegistration.RemoveUserDesktopShortcuts();
                WindowsUriSchemeRegistration.Uninstall();
            })
            .Run();

        var isAutoStart = args.Any(a => string.Equals(a, "--autostart", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(a, "-minimized", StringComparison.OrdinalIgnoreCase));

        if (!SingleInstanceApp.TryEnterFirstInstance())
        {
            AppDebugLog.InstallGlobalHandlers();
            AppDebugLog.Warn("App", "Segunda instância detetada.");
            if (!isAutoStart)
            {
                AppDebugLog.Info("App", "A ativar janela existente.");
                SingleInstanceApp.TryActivateExisting();
            }
            return;
        }

        try
        {
            AppDebugLog.InstallGlobalHandlers();
            ApplicationConfiguration.Initialize();
            AppThemeManager.Initialize();
            AppDebugLog.LogStartupContext();
            ShowLastUpdateResultIfNeeded();
            WindowsServiceGuard.EnsureRunningOrExit();
            WindowsStartupRegistration.EnsureRegistered();
            WindowsDesktopShortcutRegistration.EnsureRegistered();
            WindowsUriSchemeRegistration.EnsureRegistered();

            if (!AppSettingsStore.IsInitialSetupComplete())
            {
                using var setup = new InitialSetupForm();
                if (setup.ShowDialog() != DialogResult.OK)
                    return;
            }

            var settings = AppSettingsStore.Load();
            var forceMinimized = args.Any(a => string.Equals(a, "-minimized", StringComparison.OrdinalIgnoreCase));
            var startMinimized = forceMinimized || (isAutoStart && settings.IniciarMinimizado);

            Application.Run(new MainForm(startMinimized));
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
            // Erros de atualização ocorrem em segundo plano e permanecem visíveis nas Configurações
            // e em update.log, sem interromper o utilizador com janelas de aviso na abertura do app.
            UpdateActivityLog.Warn("Update", "Último ciclo de atualização em segundo plano registou erro: " + status.Message);
            return;
        }

        if (status.Phase == UpdateServicePhase.Completed)
            UpdateActivityLog.Info("Update", status.Message);
    }

    static string? ExtractRemoteTargetId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("sistechub://remoto/", StringComparison.OrdinalIgnoreCase))
            {
                var id = arg.Substring("sistechub://remoto/".Length).Trim().TrimEnd('/');
                id = id.Replace(" ", "").Replace("-", "");
                if (!string.IsNullOrEmpty(id))
                    return id;
            }
            if (arg.StartsWith("--connect-remoto=", StringComparison.OrdinalIgnoreCase))
            {
                var id = arg.Substring("--connect-remoto=".Length).Trim().TrimEnd('/');
                id = id.Replace(" ", "").Replace("-", "");
                if (!string.IsNullOrEmpty(id))
                    return id;
            }
            if (string.Equals(arg, "--connect-remoto", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var id = args[i + 1].Trim().Replace(" ", "").Replace("-", "");
                if (!string.IsNullOrEmpty(id))
                    return id;
            }
        }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(nint dpiFlag);
}
