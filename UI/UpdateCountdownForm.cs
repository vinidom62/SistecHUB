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
        ClientSize = new Size(460, 220);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
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
            RowCount = 4,
            Padding = new Padding(28, 24, 28, 20),
            BackColor = Color.Transparent,
        };
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
            Margin = new Padding(0, 0, 0, 16),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var restartNow = new Button
        {
            Text = "Reiniciar agora",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(14, 0, 14, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = ShellTheme.Accent,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        restartNow.FlatAppearance.BorderSize = 0;
        restartNow.Click += (_, _) => Complete(DialogResult.OK);

        var postpone = new Button
        {
            Text = "Adiar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(241, 245, 249),
            ForeColor = ShellTheme.TextPrimary,
            Cursor = Cursors.Hand,
        };
        postpone.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
        postpone.FlatAppearance.BorderSize = 1;
        postpone.Click += (_, _) => Complete(DialogResult.Cancel);

        buttons.Controls.Add(restartNow);
        buttons.Controls.Add(postpone);

        stack.Controls.Add(title, 0, 0);
        stack.Controls.Add(info, 0, 1);
        stack.Controls.Add(_countdownLabel, 0, 2);
        stack.Controls.Add(buttons, 0, 3);
        Controls.Add(stack);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        Shown += (_, _) =>
        {
            UpdateCountdownText();
            _timer.Start();
        };

        FormClosing += (_, e) =>
        {
            if (DialogResult is DialogResult.None && e.CloseReason == CloseReason.UserClosing)
                e.Cancel = true;
        };
    }

    void OnTimerTick(object? sender, EventArgs e)
    {
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            Complete(DialogResult.OK);
            return;
        }

        UpdateCountdownText();
    }

    void UpdateCountdownText() =>
        _countdownLabel.Text = $"Reiniciando em {_secondsRemaining} segundo{(_secondsRemaining == 1 ? "" : "s")}...";

    void Complete(DialogResult result)
    {
        _timer.Stop();
        DialogResult = result;
        Close();
    }
}
