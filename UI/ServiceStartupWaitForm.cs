using System.ServiceProcess;
using SistecHub.Core;

namespace SistecHub.UI;

/// <summary>Aguarda o serviço durante ou após uma actualização, com novas tentativas automáticas.</summary>
internal sealed class ServiceStartupWaitForm : Form
{
    const int WaitPhaseSeconds = 60;
    const int MaxCycles = 5;

    readonly Label _messageLabel;
    readonly Label _countdownLabel;
    readonly System.Windows.Forms.Timer _timer;

    int _secondsInPhase;
    int _cycle;
    bool _inCountdownPhase;

    public ServiceStartupWaitForm()
    {
        Text = "SistecHub — actualização";
        ClientSize = new Size(480, 200);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = true;

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(28, 28, 28, 24),
            BackColor = Color.Transparent,
        };
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _messageLabel = new Label
        {
            Text = "A aplicar actualização, aguarde o serviço reiniciar...",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            Margin = new Padding(0, 0, 0, 16),
        };

        _countdownLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
        };

        stack.Controls.Add(_messageLabel, 0, 0);
        stack.Controls.Add(_countdownLabel, 0, 1);
        Controls.Add(stack);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        Shown += (_, _) =>
        {
            TryStartService();
            UpdateLabels();
            _timer.Start();
        };
    }

    void OnTimerTick(object? sender, EventArgs e)
    {
        if (WindowsServiceGuard.IsRunning())
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _secondsInPhase++;

        if (!_inCountdownPhase)
        {
            if (_secondsInPhase % 5 == 0)
                TryStartService();

            if (_secondsInPhase >= WaitPhaseSeconds)
            {
                _inCountdownPhase = true;
                _secondsInPhase = 0;
            }
        }
        else if (_secondsInPhase >= WaitPhaseSeconds)
        {
            _cycle++;
            if (_cycle >= MaxCycles)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            _inCountdownPhase = false;
            _secondsInPhase = 0;
            TryStartService();
        }

        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (!_inCountdownPhase)
        {
            _messageLabel.Text = "A aplicar actualização, aguarde o serviço reiniciar...";
            var remaining = WaitPhaseSeconds - _secondsInPhase;
            _countdownLabel.Text = remaining > 0
                ? $"A verificar o serviço... ({remaining}s)"
                : "A preparar nova tentativa...";
            return;
        }

        _messageLabel.Text = "O serviço ainda está a reiniciar.";
        var retryIn = WaitPhaseSeconds - _secondsInPhase;
        _countdownLabel.Text = retryIn > 0
            ? $"Tentando novamente em {retryIn} segundo{(retryIn == 1 ? "" : "s")}..."
            : "A tentar novamente...";
    }

    static void TryStartService()
    {
        if (!WindowsServiceGuard.ServiceExists())
            return;

        try
        {
            using var controller = new ServiceController(WindowsServiceConfig.ServiceName);
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                return;

            controller.Start();
        }
        catch
        {
            // Melhor esforço durante recuperação pós-update.
        }
    }
}
