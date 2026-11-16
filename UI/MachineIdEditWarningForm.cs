using System.Windows.Forms.Integration;

namespace SistecHub.UI;

/// <summary>Aviso antes de permitir editar o ID da máquina do inventário (WPF).</summary>
internal sealed class MachineIdEditWarningForm : Form
{
    public MachineIdEditWarningForm()
    {
        Text = "ID da máquina";
        ClientSize = new Size(440, 190);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        var wpfView = new MachineIdEditWarningWpfControl();
        wpfView.BackRequested += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        wpfView.ContinueRequested += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = wpfView,
        };

        Controls.Add(host);
    }
}
