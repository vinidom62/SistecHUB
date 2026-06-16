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

    /// <summary>Verificação manual iniciada nas Configurações.</summary>
    public static async Task CheckForUpdatesManuallyAsync(
        IWin32Window? owner,
        CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
        {
            MessageBox.Show(
                owner,
                "Actualizações só estão disponíveis na instalação MSI (Program Files).",
                "Verificar actualização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        UpdateActivityLog.Info("Update", "Utilizador clicou em «Verificar actualização».");
        UpdateServiceCoordinator.RequestImmediateCheck();
        await WaitForCheckResultAsync(owner, cancellationToken).ConfigureAwait(true);
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
                await MonitorUntilSettledAsync(owner, manualFlow, TimeSpan.FromMinutes(10), CancellationToken.None)
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

    static async Task WaitForCheckResultAsync(IWin32Window? owner, CancellationToken cancellationToken)
    {
        var result = await MonitorUntilSettledAsync(owner, manualFlow: true, TimeSpan.FromMinutes(3), cancellationToken)
            .ConfigureAwait(true);

        if (result == MonitorResult.TimedOut)
        {
            MessageBox.Show(
                owner,
                "A verificação demorou demais.\n\n"
                + "Confirme que «SistecHub Service» está em execução.\n\n"
                + "Log: " + UpdateActivityLog.LogFilePath,
                "Verificar actualização",
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
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        UpdateServicePhase? lastPhase = UpdateServiceCoordinator.TryReadStatus()?.Phase;
        var deadline = DateTime.UtcNow + timeout;

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
                    MessageBox.Show(
                        owner,
                        $"O SistecHub já está na versão mais recente ({DisplayVersion}).",
                        "Verificar actualização",
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
                        "Verificar actualização",
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

        UpdateActivityLog.Info("Update", $"Actualização {version} pronta — a mostrar contagem regressiva.");

        if (owner is null)
        {
            UpdateActivityLog.Warn("Update", "Sem janela principal — não foi possível mostrar contagem regressiva.");
            return;
        }

        try
        {
            RunOnUiThread(owner, () => ShowCountdownAndRestart(owner, version));
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao mostrar contagem regressiva de actualização.");
        }
    }

    static void RunOnUiThread(IWin32Window owner, Action action)
    {
        if (owner is Control { InvokeRequired: true } control)
            control.BeginInvoke(action);
        else
            action();
    }

    static void ShowCountdownAndRestart(IWin32Window owner, string version)
    {
        using var countdown = new SistecHub.UI.UpdateCountdownForm(version, seconds: 10);
        countdown.ShowDialog(owner);

        UpdateActivityLog.Info("Update", "Reinício para aplicar actualização.");
        SignalApplyOnExit();
        UpdateRestartRequested?.Invoke();
    }

    /// <summary>Chamado ao fechar o app quando há update pendente — o serviço instala em silêncio.</summary>
    public static void SignalApplyOnExit()
    {
        if (!IsUpdateSupported || !IsUpdateReadyToApply())
            return;

        UpdateServiceCoordinator.RequestInstall();
        UpdateActivityLog.Info("Update", "App a fechar — actualização será aplicada pelo serviço.");
    }
}
