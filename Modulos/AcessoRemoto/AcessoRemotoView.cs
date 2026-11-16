using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.UI;

namespace SistecHub.Modulos.AcessoRemoto;

public sealed class AcessoRemotoView : UserControl
{
    readonly AcessoRemotoWpfControl _wpfView;
    readonly System.Windows.Forms.Timer _feedbackHideTimer;
    readonly System.Windows.Forms.Timer _connectErrorHideTimer;
    readonly System.Windows.Forms.Timer _sessionDurationTimer;
    DateTimeOffset? _currentSessionStartTime;

    public AcessoRemotoView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _wpfView = new AcessoRemotoWpfControl();
        _wpfView.ReconnectClicked += (_, _) => AcessoRemotoAgentService.Reconnect();
        _wpfView.CopyBothClicked += (_, _) => CopyBothToClipboard();
        _wpfView.OpenChatClicked += (_, _) => AcessoRemotoChatForm.ShowOrActivateChat();
        _wpfView.HostDisconnectClicked += (_, _) => AcessoRemotoAgentService.DisconnectActiveSession();
        _wpfView.ActiveEndSessionClicked += (_, _) =>
        {
            var res = MessageBox.Show(
                this,
                "Deseja realmente encerrar a sessão de acesso remoto com o técnico?",
                "Encerrar Acesso Remoto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                AcessoRemotoAgentService.DisconnectActiveSession();
            }
        };
        _wpfView.ConnectToRemoteClicked += (_, _) => OnConnectToRemoteClicked();

        _feedbackHideTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _feedbackHideTimer.Tick += (_, _) =>
        {
            _feedbackHideTimer.Stop();
            if (!IsDisposed)
                _wpfView.HideCopyFeedback();
        };

        _connectErrorHideTimer = new System.Windows.Forms.Timer { Interval = 3500 };
        _connectErrorHideTimer.Tick += (_, _) =>
        {
            _connectErrorHideTimer.Stop();
            if (!IsDisposed)
                _wpfView.HideConnectError();
        };

        _sessionDurationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _sessionDurationTimer.Tick += (_, _) =>
        {
            if (!IsDisposed && _wpfView.IsActiveSessionCardVisible)
            {
                _wpfView.SetDurationText(FormatActiveDuration());
            }
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Controls.Add(host);

        AcessoRemotoAgentService.StateChanged += OnServiceStateChanged;
        HandleDestroyed += OnViewHandleDestroyed;
        Load += OnViewLoad;
    }

    void OnConnectToRemoteClicked()
    {
        var targetId = _wpfView.TargetIdInput;
        var password = _wpfView.PasswordInput;

        if (string.IsNullOrEmpty(targetId))
        {
            ShowConnectError("Por favor, informe o ID do computador remoto.");
            _wpfView.FocusTargetIdInput();
            return;
        }

        if (!string.IsNullOrEmpty(password) && !GlpiTechnicianSession.IsAuthenticated)
        {
            ShowConnectError("O acesso por senha requer login do técnico Angelus. Faça o login para continuar.");
            _wpfView.PromptGlpiLogin();
            return;
        }

        _wpfView.HideConnectError();
        var serverUrl = AppSettingsStore.Load().RemotoServerUrl;

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        }

        var viewer = new AcessoRemotoViewerWindow(serverUrl, targetId, password);
        ElementHost.EnableModelessKeyboardInterop(viewer);
        viewer.Show();
        viewer.Activate();
    }

    void ShowConnectError(string message)
    {
        _wpfView.ShowConnectError(message);
        _connectErrorHideTimer.Stop();
        _connectErrorHideTimer.Start();
    }

    void OnViewLoad(object? sender, EventArgs e)
    {
        Load -= OnViewLoad;
        AcessoRemotoAgentService.Start();
        ApplyStateToUi(AcessoRemotoAgentService.GetSnapshot());
    }

    void OnViewHandleDestroyed(object? sender, EventArgs e)
    {
        HandleDestroyed -= OnViewHandleDestroyed;
        AcessoRemotoAgentService.StateChanged -= OnServiceStateChanged;
        _sessionDurationTimer.Stop();
        _sessionDurationTimer.Dispose();
        _feedbackHideTimer.Dispose();
        _connectErrorHideTimer.Dispose();
    }

    void OnServiceStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            if (!IsDisposed)
                ApplyStateToUi(AcessoRemotoAgentService.GetSnapshot());
        });
    }

    string FormatActiveDuration()
    {
        if (_currentSessionStartTime is null)
            return "Duração: 00:00:00";

        var elapsed = DateTimeOffset.Now - _currentSessionStartTime.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return $"Duração: {elapsed:hh\\:mm\\:ss}";
    }

    void ApplyStateToUi(AcessoRemotoStateSnapshot snap)
    {
        if (snap.IsSessionActive)
        {
            _currentSessionStartTime = snap.SessionStartTime ?? DateTimeOffset.Now;
            _sessionDurationTimer.Start();
        }
        else
        {
            _sessionDurationTimer.Stop();
            _currentSessionStartTime = null;
        }

        _wpfView.ApplyState(snap, FormatActiveDuration());
    }

    void CopyBothToClipboard()
    {
        var snap = AcessoRemotoAgentService.GetSnapshot();
        var idText = string.IsNullOrWhiteSpace(snap.AssignedId) ? snap.FormattedId : snap.FormattedId;
        Clipboard.SetText(idText);
        ShowCopyFeedback("ID de acesso copiado!");
    }

    void ShowCopyFeedback(string message, bool isError = false)
    {
        _wpfView.ShowCopyFeedback(message, isError);
        _feedbackHideTimer.Stop();
        _feedbackHideTimer.Start();
    }
}
