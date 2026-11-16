using System.Windows.Forms.Integration;

namespace SistecHub.UI;

/// <summary>Contagem regressiva antes de reiniciar o app para aplicar actualização (WPF).</summary>
internal sealed class UpdateCountdownForm : Form
{
    readonly UpdateCountdownWpfControl _wpfControl;
    readonly System.Windows.Forms.Timer _timer;
    int _secondsRemaining;

    public UpdateCountdownForm(string version, int seconds = 10)
    {
        _secondsRemaining = seconds;

        Text = "Atualização disponível";
        ClientSize = new Size(460, 180);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ControlBox = true;
        ShowInTaskbar = true;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        TopMost = true;

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfControl = new UpdateCountdownWpfControl();
        _wpfControl.SetTitle(version);

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTimerTick;

        Shown += (_, _) =>
        {
            _wpfControl.SetCountdown(_secondsRemaining);
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

        _wpfControl.SetCountdown(_secondsRemaining);
    }

    void Complete()
    {
        _timer.Stop();
        DialogResult = DialogResult.OK;
        Close();
    }
}
