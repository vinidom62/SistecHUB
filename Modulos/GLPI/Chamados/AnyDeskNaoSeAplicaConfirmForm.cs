using System.Windows.Forms.Integration;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Confirmação ao marcar AnyDesk como "não se aplica" (WPF).</summary>
internal sealed class AnyDeskNaoSeAplicaConfirmForm : Form
{
    readonly AnyDeskNaoSeAplicaWpfControl _wpfControl;
    readonly System.Windows.Forms.Timer _cooldownTimer;
    int _segundosRestantes = 3;

    public AnyDeskNaoSeAplicaConfirmForm()
    {
        Text = "Acesso Remoto";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(480, 210);
        MinimumSize = new Size(480, 210);
        MaximumSize = new Size(480, 210);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfControl = new AnyDeskNaoSeAplicaWpfControl();
        _wpfControl.CancelRequested += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _wpfControl.ConfirmRequested += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);

        _cooldownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _cooldownTimer.Tick += OnCooldownTick;
        Shown += (_, _) => _cooldownTimer.Start();
        FormClosed += (_, _) =>
        {
            _cooldownTimer.Stop();
            _cooldownTimer.Dispose();
        };
    }

    void OnCooldownTick(object? sender, EventArgs e)
    {
        _segundosRestantes--;
        _wpfControl.UpdateCountdown(_segundosRestantes);
        if (_segundosRestantes <= 0)
        {
            _cooldownTimer.Stop();
        }
    }
}
