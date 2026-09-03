namespace SistecHub.Core;

/// <summary>Verificação e instalação de actualizações via serviço Windows (instalação MSI).</summary>
public static class AppUpdateService
{
    static readonly object NotifySync = new();
    static string? _notifiedVersion;
    static bool _monitorRunning;

    /// <summary>Disparado para reiniciar o app (após contagem regressiva ou «Reiniciar agora»).</summary>
    public static event Action? UpdateRestartRequested;

    public static bool IsUpdateSupported => VelopackUpdateEngine.IsInstalled;

    public static string DisplayVersion => VelopackUpdateEngine.DisplayVersion;

    public static string GetUpdateStatusText()
    {
        var statusText = UpdateServiceCoordinator.DescribeStatusForUi(UpdateServiceCoordinator.TryReadStatus());
        return statusText + Environment.NewLine + $"Log: {UpdateActivityLog.LogFilePath}";
    }

    /// <summary>Verificação automática ao abrir o aplicativo.</summary>
    public static void BeginAutomaticUpdateMonitoring(IWin32Window? owner)
    {
        if (!IsUpdateSupported)
            return;

        UpdateActivityLog.Info("Update", "Verificação automática solicitada.");
        UpdateServiceCoordinator.RequestImmediateCheck();
        StartBackgroundMonitor(owner, manualFlow: false);
    }

    /// <summary>Verificação manual iniciada nas Configurações (releases estáveis).</summary>
    public static async Task CheckForUpdatesManuallyAsync(
        IWin32Window? owner,
        CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
        {
            MessageBox.Show(
                owner,
                "Atualizações só estão disponíveis na instalação MSI (Program Files).",
                "Verificar atualizações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        UpdateActivityLog.Info("Update", "Utilizador clicou em «Verificar atualizações».");
        UpdateServiceCoordinator.RequestImmediateCheck();
        await WaitForCheckResultAsync(owner, includePrerelease: false, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Verificação manual de pré-releases — nunca usada pela verificação automática.</summary>
    public static async Task CheckForBetaUpdatesManuallyAsync(
        IWin32Window? owner,
        CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
        {
            MessageBox.Show(
                owner,
                "Atualizações só estão disponíveis na instalação MSI (Program Files).",
                "Verificar atualização Beta",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        UpdateActivityLog.Info("Update", "Utilizador clicou em «Verificar atualização Beta».");
        UpdateServiceCoordinator.RequestImmediateBetaCheck();
        await WaitForCheckResultAsync(owner, includePrerelease: true, cancellationToken).ConfigureAwait(true);
    }

    static void StartBackgroundMonitor(IWin32Window? owner, bool manualFlow)
    {
        lock (NotifySync)
        {
            if (_monitorRunning)
                return;
            _monitorRunning = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await MonitorUntilSettledAsync(
                        owner,
                        manualFlow,
                        includePrerelease: false,
                        TimeSpan.FromMinutes(10),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UpdateActivityLog.LogException("Update", ex, "Monitor automático de actualização falhou.");
            }
            finally
            {
                lock (NotifySync)
                    _monitorRunning = false;
            }
        });
    }

    static async Task WaitForCheckResultAsync(
        IWin32Window? owner,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        var title = includePrerelease ? "Verificar atualização Beta" : "Verificar atualizações";
        var result = await MonitorUntilSettledAsync(
                owner,
                manualFlow: true,
                includePrerelease,
                TimeSpan.FromMinutes(3),
                cancellationToken)
            .ConfigureAwait(true);

        if (result == MonitorResult.TimedOut)
        {
            MessageBox.Show(
                owner,
                "A verificação demorou demais.\n\n"
                + "Confirme que «SistecHub Service» está em execução.\n\n"
                + "Log: " + UpdateActivityLog.LogFilePath,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    enum MonitorResult
    {
        Settled,
        TimedOut,
    }

    static async Task<MonitorResult> MonitorUntilSettledAsync(
        IWin32Window? owner,
        bool manualFlow,
        bool includePrerelease,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        UpdateServicePhase? lastPhase = UpdateServiceCoordinator.TryReadStatus()?.Phase;
        var deadline = DateTime.UtcNow + timeout;
        var title = includePrerelease ? "Verificar atualização Beta" : "Verificar atualizações";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

            var status = UpdateServiceCoordinator.TryReadStatus();
            if (status?.Phase != lastPhase && status is not null)
            {
                UpdateActivityLog.Info("Update", $"Progresso: {status.Phase} — {status.Message}");
                lastPhase = status.Phase;
            }

            if (IsUpdateReadyToApply())
            {
                PromptRestartForUpdateIfNeeded(owner);
                return MonitorResult.Settled;
            }

            if (status?.Phase == UpdateServicePhase.UpToDate)
            {
                if (manualFlow)
                {
                    var message = includePrerelease
                        ? $"Não há pré-releases mais recentes.\nVersão actual: {DisplayVersion}."
                        : $"O SistecHub já está na versão mais recente ({DisplayVersion}).";
                    MessageBox.Show(
                        owner,
                        message,
                        title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return MonitorResult.Settled;
            }

            if (status?.Phase == UpdateServicePhase.Error)
            {
                if (manualFlow)
                {
                    MessageBox.Show(
                        owner,
                        status.Message + "\n\nDetalhes em:\n" + UpdateActivityLog.LogFilePath,
                        title,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return MonitorResult.Settled;
            }
        }

        if (IsUpdateReadyToApply())
        {
            PromptRestartForUpdateIfNeeded(owner);
            return MonitorResult.Settled;
        }

        return manualFlow ? MonitorResult.TimedOut : MonitorResult.Settled;
    }

    static bool IsUpdateReadyToApply() =>
        VelopackUpdateEngine.PendingRestart is not null
        || UpdateServiceCoordinator.TryReadStatus()?.Phase == UpdateServicePhase.PendingAppClose;

    static void PromptRestartForUpdateIfNeeded(IWin32Window? owner)
    {
        var version = VelopackUpdateEngine.PendingRestart?.Version.ToString()
            ?? UpdateServiceCoordinator.TryReadStatus()?.AvailableVersion;

        if (string.IsNullOrWhiteSpace(version))
            return;

        lock (NotifySync)
        {
            if (string.Equals(_notifiedVersion, version, StringComparison.Ordinal))
                return;
            _notifiedVersion = version;
        }

        UpdateActivityLog.Info("Update", $"Actualização {version} pronta.");

        var targetWindow = owner ?? Form.ActiveForm ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);

        if (targetWindow is Control { InvokeRequired: true } control)
        {
            control.BeginInvoke(() => ApplyOrPromptRestart(targetWindow, version));
        }
        else
        {
            ApplyOrPromptRestart(targetWindow, version);
        }
    }

    static void ApplyOrPromptRestart(IWin32Window? owner, string version)
    {
        try
        {
            // O aviso de 10s só é exibido se o SistecHub estiver aberto e visível na tela.
            // Se estiver em segundo plano (minimizado na barra ou no tabuleiro), aplica sem exibir nada.
            if (IsWindowVisibleOnScreen(owner))
            {
                UpdateActivityLog.Info("Update", $"SistecHub aberto na tela — a mostrar contagem regressiva de 10s para versão {version}.");
                using var countdown = new SistecHub.UI.UpdateCountdownForm(version, seconds: 10);
                countdown.ShowDialog(owner);
                UpdateActivityLog.Info("Update", "Contagem regressiva concluída — reinício para aplicar actualização.");
            }
            else
            {
                UpdateActivityLog.Info("Update", $"SistecHub em segundo plano (minimizado ou oculto) — a reiniciar silenciosamente para aplicar actualização {version}.");
            }

            SignalApplyOnExit();
            UpdateRestartRequested?.Invoke();
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao processar reinício de actualização.");
        }
    }

    static bool IsWindowVisibleOnScreen(IWin32Window? window)
    {
        if (window is Form form)
        {
            return !form.IsDisposed
                && form.Visible
                && form.WindowState != FormWindowState.Minimized
                && form.Opacity > 0;
        }

        if (window is Control control)
        {
            var parentForm = control.FindForm();
            if (parentForm is not null)
                return IsWindowVisibleOnScreen(parentForm);

            return !control.IsDisposed && control.Visible;
        }

        return false;
    }

    /// <summary>Chamado ao fechar o app quando há update pendente — o serviço instala em silêncio.</summary>
    public static void SignalApplyOnExit()
    {
        if (!IsUpdateSupported || !IsUpdateReadyToApply())
            return;

        var version = VelopackUpdateEngine.PendingRestart?.Version.ToString()
            ?? UpdateServiceCoordinator.TryReadStatus()?.AvailableVersion;

        UserSessionUpdateWatcher.LaunchWatcherProcess(version);
        UpdateServiceCoordinator.RequestReopenAppAfterUpdate(version);
        UpdateServiceCoordinator.RequestInstall();
        UpdateActivityLog.Info("Update", "App a fechar — actualização será aplicada pelo serviço e relançada na sessão do utilizador.");
    }
}
