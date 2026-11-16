using System.Windows.Forms.Integration;
using SistecHub.UI;

namespace SistecHub.Modulos.AcessoRemoto;

internal sealed class AcessoRemotoChatForm : Form
{
    static AcessoRemotoChatForm? _instance;
    static readonly object InstanceLock = new();

    readonly AcessoRemotoChatWpfControl _wpfChat;

    public static void ShowOrActivateChat()
    {
        lock (InstanceLock)
        {
            if (_instance is not null && !_instance.IsDisposed)
            {
                if (_instance.InvokeRequired)
                {
                    _instance.BeginInvoke(ShowOrActivateChat);
                    return;
                }

                if (_instance.WindowState == FormWindowState.Minimized)
                    _instance.WindowState = FormWindowState.Normal;

                _instance.Show();
                _instance.BringToFront();
                _instance.Activate();
                return;
            }
        }

        for (int i = 0; i < Application.OpenForms.Count; i++)
        {
            var openForm = Application.OpenForms[i];
            if (openForm is not null && !openForm.IsDisposed && openForm.IsHandleCreated && openForm.InvokeRequired)
            {
                openForm.BeginInvoke(ShowOrActivateChat);
                return;
            }
        }

        lock (InstanceLock)
        {
            if (_instance is not null && !_instance.IsDisposed)
            {
                if (_instance.WindowState == FormWindowState.Minimized)
                    _instance.WindowState = FormWindowState.Normal;

                _instance.Show();
                _instance.BringToFront();
                _instance.Activate();
                return;
            }

            var form = new AcessoRemotoChatForm();
            _instance = form;

            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Cursor.Position);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(
                Math.Max(workingArea.Left, workingArea.Right - form.Width - 24),
                Math.Max(workingArea.Top, workingArea.Bottom - form.Height - 24));

            form.Show();
            form.BringToFront();
            form.Activate();
        }
    }

    public static void NotifySessionEnded()
    {
        CloseIfOpen();
    }

    public static void CloseIfOpen()
    {
        lock (InstanceLock)
        {
            if (_instance is null || _instance.IsDisposed)
                return;

            try
            {
                var form = _instance;
                if (form.InvokeRequired)
                    form.BeginInvoke(() => form.Close());
                else
                    form.Close();
            }
            catch
            {
                // Ignorado
            }
            finally
            {
                _instance = null;
            }
        }
    }

    public AcessoRemotoChatForm()
    {
        Text = "Suporte Remoto - Chat";
        ClientSize = new Size(410, 550);
        MinimumSize = new Size(320, 420);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfChat = new AcessoRemotoChatWpfControl();
        _wpfChat.SendRequested += async (_, _) => await SendMessageAsync().ConfigureAwait(true);
        _wpfChat.DisconnectRequested += (_, _) =>
        {
            var result = MessageBox.Show(
                this,
                "Deseja realmente encerrar a sessão de acesso remoto com o técnico?",
                "Encerrar Acesso Remoto",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                AcessoRemotoAgentService.DisconnectActiveSession();
                Close();
            }
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfChat,
        };

        Controls.Add(host);

        _pingTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _pingTimer.Tick += async (_, _) => await CheckPingAsync().ConfigureAwait(false);

        AcessoRemotoAgentService.ChatMessageAdded += OnChatMessageAdded;
        AcessoRemotoAgentService.StateChanged += OnAgentStateChanged;
        FormClosed += OnFormClosed;
        Load += OnFormLoad;
    }

    readonly System.Windows.Forms.Timer _pingTimer;

    private async Task CheckPingAsync()
    {
        try
        {
            var host = "angelus.sisteconsultoria.com.br";
            var serverUrl = AcessoRemotoAgentService.GetSnapshot().ServerUrl;
            if (Uri.TryCreate(serverUrl.Replace("ws://", "http://").Replace("wss://", "https://"), UriKind.Absolute, out var uri))
            {
                host = uri.Host;
            }

            using var pinger = new System.Net.NetworkInformation.Ping();
            var reply = await pinger.SendPingAsync(host, 1500).ConfigureAwait(false);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success && !IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                        _wpfChat.SetPing(reply.RoundtripTime);
                });
            }
        }
        catch
        {
            // Silencioso
        }
    }

    private void UpdateTechnicianInfo()
    {
        var snap = AcessoRemotoAgentService.GetSnapshot();
        var name = !string.IsNullOrWhiteSpace(snap.ConnectedTechnicianName)
            ? snap.ConnectedTechnicianName
            : "Técnico de Suporte";
        var avatar = !string.IsNullOrWhiteSpace(snap.ConnectedTechnicianAvatar)
            ? snap.ConnectedTechnicianAvatar
            : null;

        _wpfChat.SetTechnician(name, avatar);
    }

    private void OnAgentStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(UpdateTechnicianInfo);
    }

    void OnFormLoad(object? sender, EventArgs e)
    {
        Load -= OnFormLoad;
        UpdateTechnicianInfo();
        _pingTimer.Start();
        _ = Task.Run(CheckPingAsync);
        RebuildHistory();
        _wpfChat.FocusInput();
    }

    void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _pingTimer.Stop();
        AcessoRemotoAgentService.ChatMessageAdded -= OnChatMessageAdded;
        AcessoRemotoAgentService.StateChanged -= OnAgentStateChanged;
        lock (InstanceLock)
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }
    }

    void OnChatMessageAdded(object? sender, AcessoRemotoChatEntry entry)
    {
        if (AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(entry.Text))
            return;

        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;

            if (entry.IsFromTechnician)
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;

                Show();
                BringToFront();
                Activate();
                _wpfChat.FocusInput();
            }

            _wpfChat.AddMessage(entry);
        });
    }

    void RebuildHistory()
    {
        _wpfChat.ClearMessages();
        var history = AcessoRemotoAgentService.GetChatHistory();
        if (history.Count == 0)
        {
            _wpfChat.AddMessage(new AcessoRemotoChatEntry(
                Sender: "Sistema",
                Text: "Início do atendimento com suporte remoto.",
                Time: DateTime.Now.ToString("HH:mm"),
                IsFromTechnician: false,
                IsSystemEvent: true));
            return;
        }

        foreach (var entry in history)
        {
            if (!AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(entry.Text))
                _wpfChat.AddMessage(entry);
        }
    }

    async Task SendMessageAsync()
    {
        if (!AcessoRemotoAgentService.GetSnapshot().IsSessionActive)
            return;

        var text = _wpfChat.InputText;
        if (string.IsNullOrEmpty(text))
            return;

        _wpfChat.SetSendEnabled(false);
        try
        {
            var sent = await AcessoRemotoAgentService.SendChatMessageAsync(text).ConfigureAwait(true);
            if (sent)
                _wpfChat.InputText = "";
        }
        finally
        {
            if (!IsDisposed)
            {
                _wpfChat.SetSendEnabled(true);
                _wpfChat.FocusInput();
            }
        }
    }
}
