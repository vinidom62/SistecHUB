using System.Windows.Forms.Integration;

namespace SistecHub.UI;

internal sealed class SettingsPasswordForm : Form
{
    const string SettingsPassword = "admin";

    readonly SettingsPasswordWpfControl _wpfView;

    public SettingsPasswordForm(
        string formTitle = "Acesso às configurações",
        string titleText = "Senha necessária",
        string subtitleText = "Digite a senha para abrir as configurações.")
    {
        Text = formTitle;
        ClientSize = new Size(440, 270);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        _wpfView = new SettingsPasswordWpfControl(titleText, subtitleText);
        _wpfView.ConfirmRequested += (_, _) => TryConfirm();
        _wpfView.CancelRequested += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Controls.Add(host);

        Shown += (_, _) =>
        {
            AdjustHeightToContent();
            host.Focus();
            _wpfView.FocusPassword();
        };
    }

    void AdjustHeightToContent()
    {
        var scale = DeviceDpi / 96.0;
        var widthDip = Math.Max(360, ClientSize.Width / scale);
        _wpfView.Measure(new System.Windows.Size(widthDip, double.PositiveInfinity));
        var desiredPx = (int)Math.Ceiling(_wpfView.DesiredSize.Height * scale);
        if (desiredPx > ClientSize.Height)
            ClientSize = new Size(ClientSize.Width, desiredPx);
    }

    void TryConfirm()
    {
        if (string.Equals(_wpfView.Password, SettingsPassword, StringComparison.Ordinal))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _wpfView.ShowIncorrectPasswordError();
        AdjustHeightToContent();
    }
}
