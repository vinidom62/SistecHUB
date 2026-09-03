namespace SistecHub.UI;

internal sealed class SettingsPasswordForm : Form
{
    const string SettingsPassword = "admin";

    readonly TextBox _passwordTextBox;
    readonly Label _errorLabel;

    public SettingsPasswordForm(
        string formTitle = "Acesso às configurações",
        string titleText = "Senha necessária",
        string subtitleText = "Digite a senha para abrir as configurações.")
    {
        Text = formTitle;
        ClientSize = new Size(420, 220);
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

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Padding = new Padding(32, 28, 32, 28),
        };

        var title = new Label
        {
            Text = titleText,
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };

        var subtitle = new Label
        {
            Text = subtitleText,
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        var passwordLabel = new Label
        {
            Text = "Senha",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6),
        };

        _passwordTextBox = new TextBox
        {
            Width = 340,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = true,
        };
        _passwordTextBox.KeyDown += OnPasswordKeyDown;

        _errorLabel = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(220, 38, 38),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false,
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 18, 0, 0),
        };

        var confirmButton = new Button
        {
            Text = "Entrar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
        };
        confirmButton.FlatAppearance.BorderSize = 0;
        confirmButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        confirmButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);
        confirmButton.Click += (_, _) => TryConfirm();

        var cancelButton = new Button
        {
            Text = "Cancelar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(226, 232, 240),
            Cursor = Cursors.Hand,
        };
        cancelButton.FlatAppearance.BorderSize = 0;

        buttons.Controls.Add(confirmButton);
        buttons.Controls.Add(cancelButton);

        AcceptButton = confirmButton;
        CancelButton = cancelButton;

        stack.Controls.Add(title);
        stack.Controls.Add(subtitle);
        stack.Controls.Add(passwordLabel);
        stack.Controls.Add(_passwordTextBox);
        stack.Controls.Add(_errorLabel);
        stack.Controls.Add(buttons);

        Controls.Add(stack);

        Shown += (_, _) => _passwordTextBox.Focus();
    }

    void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            TryConfirm();
        }
    }

    void TryConfirm()
    {
        if (string.Equals(_passwordTextBox.Text, SettingsPassword, StringComparison.Ordinal))
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _errorLabel.Text = "Senha incorreta.";
        _errorLabel.Visible = true;
        _passwordTextBox.SelectAll();
        _passwordTextBox.Focus();
    }
}
