using SistecHub.Modulos.AcessoRemoto;

namespace SistecHub.Core;

/// <summary>Verificação e instalação de actualizações via serviço Windows (instalação MSI).</summary>
public static class AppUpdateService
{
    static readonly object NotifySync = new();
    static string? _notifiedVersion;
    static string? _deferredRemoteVersion;
    static bool _monitorRunning;
    static bool _manualDialogOpen;
    static bool _applySignaled;
    static bool _sessionEndedSubscribed;
    static IWin32Window? _monitoredOwner;

    /// <summary>Disparado para reiniciar o app (após contagem regressiva de 10s ou «Reiniciar Agora»).</summary>
    public static event Action? UpdateRestartRequested;

    public static bool IsUpdateSupported => VelopackUpdateEngine.IsInstalled;

    public static string DisplayVersion => VelopackUpdateEngine.DisplayVersion;

    public static string GetUpdateStatusText()
    {
        var statusText = UpdateServiceCoordinator.DescribeStatusForUi(UpdateServiceCoordinator.TryReadStatus());
        return statusText + Environment.NewLine + $"Log: {UpdateActivityLog.LogFilePath}";
    }

    /// <summary>Verificação automática ao abrir o aplicativo e monitoramento contínuo a cada 30s.</summary>
    public static void BeginAutomaticUpdateMonitoring(IWin32Window? owner)
    {
        if (!IsUpdateSupported)
            return;

        _monitoredOwner = owner;

        lock (NotifySync)
        {
            if (!_sessionEndedSubscribed)
            {
                _sessionEndedSubscribed = true;
                AcessoRemotoAgentService.SessionEnded += OnRemoteSessionEnded;
            }
        }

        UpdateActivityLog.Info("Update", "Verificação automática solicitada.");
        UpdateServiceCoordinator.RequestImmediateCheck(resetStatus: false);
        StartBackgroundMonitor(owner);
    }

    static void OnRemoteSessionEnded(object? sender, EventArgs e)
    {
        if (!IsUpdateSupported || !IsUpdateReadyToApply())
            return;

        UpdateActivityLog.Info("Update", "Sessão de suporte remoto encerrada — a retomar aplicação da atualização pendente.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(800).ConfigureAwait(false);
            PromptRestartForUpdateIfNeeded(_monitoredOwner);
        });
    }

    /// <summary>Verificação manual iniciada nas Configurações (releases estáveis).</summary>
    public static Task CheckForUpdatesManuallyAsync(
        IWin32Window? owner,
        CancellationToken cancellationToken = default) =>
        RunManualUpdateDialogAsync(owner, includePrerelease: false, cancellationToken);

    /// <summary>Verificação manual de pré-releases — nunca usada pela verificação automática.</summary>
    public static Task CheckForBetaUpdatesManuallyAsync(
        IWin32Window? owner,
        CancellationToken cancellationToken = default) =>
        RunManualUpdateDialogAsync(owner, includePrerelease: true, cancellationToken);

    static Task RunManualUpdateDialogAsync(
        IWin32Window? owner,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsUpdateSupported)
        {
            MessageBox.Show(
                owner,
                "Atualizações só estão disponíveis na instalação MSI (Program Files).",
                includePrerelease ? "Verificar atualização Beta" : "Verificar atualizações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        if (IsRemoteSupportSessionActive())
        {
            MessageBox.Show(
                owner,
                "Há uma sessão de suporte remoto em andamento.\n\nAguarde o término do atendimento antes de verificar ou aplicar atualizações.",
                includePrerelease ? "Verificar atualização Beta" : "Verificar atualizações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        UpdateActivityLog.Info(
            "Update",
            includePrerelease
                ? "Utilizador clicou em «Verificar atualização Beta»."
                : "Utilizador clicou em «Verificar atualizações».");

        lock (NotifySync)
        {
            if (_manualDialogOpen)
                return Task.CompletedTask;
            _manualDialogOpen = true;
        }

        try
        {
            using var dlg = new SistecHub.UI.UpdateProgressForm(includePrerelease);
            ShowDialogSafely(dlg, owner);

            if (dlg.RestartConfirmed)
            {
                if (IsRemoteSupportSessionActive())
                {
                    lock (NotifySync)
                    {
                        _deferredRemoteVersion = dlg.TargetVersion;
                        _notifiedVersion = null;
                    }
                    UpdateActivityLog.Info("Update", $"Sessão remota ativa ao concluir verificação manual da versão {dlg.TargetVersion} — reinício adiado para o fim do atendimento.");
                    return Task.CompletedTask;
                }

                lock (NotifySync)
                {
                    if (!string.IsNullOrWhiteSpace(dlg.TargetVersion))
                        _notifiedVersion = dlg.TargetVersion;
                }

                SignalApplyOnExit(showProgressWindowOverride: true);
                UpdateRestartRequested?.Invoke();
            }
        }
        finally
        {
            lock (NotifySync)
            {
                _manualDialogOpen = false;
            }
        }

        return Task.CompletedTask;
    }

    static void StartBackgroundMonitor(IWin32Window? owner)
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
                await RunContinuousMonitorLoopAsync(owner).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UpdateActivityLog.LogException("Update", ex, "Monitor contínuo de actualização falhou.");
            }
            finally
            {
                lock (NotifySync)
                    _monitorRunning = false;
            }
        });
    }

    static async Task RunContinuousMonitorLoopAsync(IWin32Window? owner)
    {
        UpdateServicePhase? lastPhase = UpdateServiceCoordinator.TryReadStatus()?.Phase;
        var startupFastCheckUntil = DateTime.UtcNow.AddMinutes(2);

        // Fase inicial rápida após abertura do app (verifica a cada 3s nos primeiros 2 minutos)
        while (DateTime.UtcNow < startupFastCheckUntil)
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            CheckBackgroundStatusOnce(owner, ref lastPhase);

            var status = UpdateServiceCoordinator.TryReadStatus();
            if (status?.Phase is UpdateServicePhase.UpToDate or UpdateServicePhase.Completed)
                break;
        }

        // Monitoramento contínuo em background a cada 30 segundos enquanto o app estiver aberto
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            CheckBackgroundStatusOnce(owner, ref lastPhase);
        }
    }

    static void CheckBackgroundStatusOnce(IWin32Window? owner, ref UpdateServicePhase? lastPhase)
    {
        var status = UpdateServiceCoordinator.TryReadStatus();
        if (status is not null && status.Phase != lastPhase)
        {
            UpdateActivityLog.Info("Update", $"Progresso em background: {status.Phase} — {status.Message}");
            lastPhase = status.Phase;
        }

        if (IsUpdateReadyToApply())
        {
            PromptRestartForUpdateIfNeeded(owner);
        }
    }

    public static bool IsUpdateReadyToApply()
    {
        if (VelopackUpdateEngine.PendingRestart is { } pending)
        {
            var pendingVer = pending.Version.ToString();
            if (!string.Equals(pendingVer, DisplayVersion, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var status = UpdateServiceCoordinator.TryReadStatus();
        if (status?.Phase == UpdateServicePhase.PendingAppClose)
        {
            if (string.IsNullOrWhiteSpace(status.AvailableVersion)
                || !string.Equals(status.AvailableVersion, DisplayVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static void PromptRestartForUpdateIfNeeded(IWin32Window? owner)
    {
        var version = VelopackUpdateEngine.PendingRestart?.Version.ToString()
            ?? UpdateServiceCoordinator.TryReadStatus()?.AvailableVersion;

        if (string.IsNullOrWhiteSpace(version)
            || string.Equals(version, DisplayVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Protege sessões ativas de suporte remoto — não derruba o atendimento em andamento
        if (IsRemoteSupportSessionActive())
        {
            lock (NotifySync)
            {
                if (!string.Equals(_deferredRemoteVersion, version, StringComparison.Ordinal))
                {
                    _deferredRemoteVersion = version;
                    UpdateActivityLog.Info(
                        "Update",
                        $"Atualização {version} pronta, mas há uma sessão de suporte remoto ativa. O reinício aguarda o término do suporte.");
                }
            }
            return;
        }

        lock (NotifySync)
        {
            if (_manualDialogOpen)
                return;

            if (string.Equals(_notifiedVersion, version, StringComparison.Ordinal))
                return;
            _notifiedVersion = version;
            _deferredRemoteVersion = null;
        }

        UpdateActivityLog.Info("Update", $"Actualização {version} pronta para aplicação.");

        var targetWindow = ResolveMainForm(owner);

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
            if (IsRemoteSupportSessionActive())
            {
                lock (NotifySync)
                {
                    if (string.Equals(_notifiedVersion, version, StringComparison.Ordinal))
                        _notifiedVersion = null;
                }
                UpdateActivityLog.Info("Update", $"Sessão remota iniciada antes do aviso da versão {version} — reinício adiado até o término do suporte.");
                return;
            }

            // Regra de exibição:
            // - Se o SistecHub estiver aberto na tela OU minimizado na barra de tarefas (form.Visible == true):
            //   Exibe a janela com aviso de download concluído, 10 segundos de leitura e botão [Reiniciar Agora],
            //   e em seguida o Watcher exibe a tela "Aplicando arquivos..." durante a cópia.
            // - Se o SistecHub estiver oculto na bandeja do sistema (System Tray, form.Visible == false):
            //   Atualiza e reinicia de forma 100% discreta sem exibir nenhuma janela.
            var showUi = IsWindowOpenOrInTaskbar(owner);

            if (showUi)
            {
                UpdateActivityLog.Info(
                    "Update",
                    $"SistecHub aberto ou minimizado na barra de tarefas — a mostrar aviso de 10s para versão {version}.");
                using var progressForm = new SistecHub.UI.UpdateProgressForm(readyVersion: version, countdownSeconds: 10);
                ShowDialogSafely(progressForm, owner);

                if (IsRemoteSupportSessionActive())
                {
                    lock (NotifySync)
                    {
                        _deferredRemoteVersion = version;
                        _notifiedVersion = null;
                    }
                    UpdateActivityLog.Info("Update", $"Sessão remota iniciada durante o aviso da versão {version} — reinício adiado para o fim do atendimento.");
                    return;
                }

                UpdateActivityLog.Info("Update", "Contagem regressiva de 10s concluída — a reiniciar para aplicar actualização.");
            }
            else
            {
                UpdateActivityLog.Info(
                    "Update",
                    $"SistecHub oculto na bandeja do sistema (System Tray) — a atualizar de forma discreta para versão {version}.");
            }

            SignalApplyOnExit(showProgressWindowOverride: showUi);
            UpdateRestartRequested?.Invoke();
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao processar reinício de actualização.");
        }
    }

    static void ShowDialogSafely(Form dialog, IWin32Window? owner)
    {
        var formOwner = ResolveMainForm(owner) as Form;
        if (formOwner is not null && !formOwner.IsDisposed && formOwner.Visible && formOwner.WindowState != FormWindowState.Minimized)
        {
            dialog.ShowDialog(formOwner);
        }
        else
        {
            dialog.ShowDialog();
        }
    }

    static bool IsRemoteSupportSessionActive()
    {
        try
        {
            return AcessoRemotoAgentService.GetSnapshot().IsSessionActive;
        }
        catch
        {
            return false;
        }
    }

    static IWin32Window? ResolveMainForm(IWin32Window? candidate)
    {
        if (candidate is Form f && !f.IsDisposed)
            return f;

        if (candidate is Control c && !c.IsDisposed)
        {
            var parent = c.FindForm();
            if (parent is not null && !parent.IsDisposed)
                return parent;
        }

        if (_monitoredOwner is Form mf && !mf.IsDisposed)
            return mf;

        if (Application.OpenForms.Count > 0)
        {
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm is SistecHub.UI.MainForm && !openForm.IsDisposed)
                    return openForm;
            }
            return Application.OpenForms[0];
        }

        return candidate;
    }

    /// <summary>
    /// Retorna <c>true</c> se a janela do SistecHub estiver aberta na tela OU minimizada na barra de tarefas
    /// (<c>form.Visible == true</c>). Retorna <c>false</c> se estiver oculta na bandeja do sistema (System Tray).
    /// </summary>
    public static bool IsWindowOpenOrInTaskbar(IWin32Window? window)
    {
        var resolved = ResolveMainForm(window);

        if (resolved is Form form)
        {
            return !form.IsDisposed
                && form.Visible
                && form.Opacity > 0;
        }

        if (resolved is Control control)
        {
            var parentForm = control.FindForm();
            if (parentForm is not null)
                return IsWindowOpenOrInTaskbar(parentForm);

            return !control.IsDisposed && control.Visible;
        }

        return false;
    }

    /// <summary>Chamado ao fechar o app quando há update pendente — o serviço instala e o watcher relança na sessão do utilizador.</summary>
    public static void SignalApplyOnExit(bool? showProgressWindowOverride = null)
    {
        if (!IsUpdateSupported || !IsUpdateReadyToApply())
            return;

        lock (NotifySync)
        {
            if (_applySignaled)
                return;
            _applySignaled = true;
        }

        var version = VelopackUpdateEngine.PendingRestart?.Version.ToString()
            ?? UpdateServiceCoordinator.TryReadStatus()?.AvailableVersion;

        var showProgressWindow = showProgressWindowOverride ?? IsWindowOpenOrInTaskbar(_monitoredOwner);

        UpdateServiceCoordinator.RequestReopenAppAfterUpdate(version, startMinimized: !showProgressWindow);
        UpdateServiceCoordinator.RequestInstall(version);
        UserSessionUpdateWatcher.LaunchWatcherProcess(version, showProgressWindow);
        UpdateActivityLog.Info(
            "Update",
            $"App a fechar — actualização ({version}) será aplicada pelo serviço e relançada na sessão do utilizador (showProgressWindow={showProgressWindow}).");
    }
}
