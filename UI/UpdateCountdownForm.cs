namespace SistecHub.UI;

/// <summary>Contagem regressiva antes de reiniciar o app para aplicar actualização.</summary>
internal sealed class UpdateCountdownForm : Form
{
    readonly Label _countdownLabel;
    readonly System.Windows.Forms.Timer _timer;
    int _secondsRemaining;

    public UpdateCountdownForm(string version, int seconds = 10)
    {
        _secondsRemaining = seconds;

        Text = "Actualização disponível";
        ClientSize = new Size(460, 180);
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
            RowCount = 3,
            Padding = new Padding(28, 24, 28, 24),
            BackColor = Color.Transparent,
        };
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = $"Versão {version} pronta para instalar",
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            Margin = new Padding(0, 0, 0, 8),
        };

        var info = new Label
        {
            Text = "O SistecHub irá reiniciar automaticamente para aplicar a actualização.",
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            Margin = new Padding(0, 0, 0, 12),
        };

        _countdownLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
        };

        stack.Controls.Add(title, 0, 0);
        stack.Controls.Add(info, 0, 1);
        stack.Controls.Add(_countdownLabel, 0, 2);
        Controls.Add(stack);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        Shown += (_, _) =>
        {
            UpdateCountdownText();
            _timer.Start();
        };
    }

    void OnTimerTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            Complete();
            return;
        }

        UpdateCountdownText();
    }

    void UpdateCountdownText() =>
        _countdownLabel.Text = $"Reiniciando em {_secondsRemaining} segundo{(_secondsRemaining == 1 ? "" : "s")}...";

    void Complete()
    {
        _timer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }
}
