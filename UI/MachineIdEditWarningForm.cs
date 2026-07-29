namespace SistecHub.UI;

/// <summary>Aviso antes de permitir editar o ID da máquina do inventário.</summary>
internal sealed class MachineIdEditWarningForm : Form
{
    public MachineIdEditWarningForm()
    {
        Text = "ID da máquina";
        ClientSize = new Size(440, 200);
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

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
            Padding = new Padding(32, 28, 32, 28),
        };

        stack.Controls.Add(new Label
        {
            Text = "Atenção",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
        });

        stack.Controls.Add(new Label
        {
            Text = "NÃO MODIFICAR SEM CONHECIMENTO",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(185, 28, 28),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 20),
        });

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        var backButton = new Button
        {
            Text = "Voltar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel,
        };
        backButton.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
        backButton.FlatAppearance.BorderSize = 1;

        var continueButton = new Button
        {
            Text = "Continuar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK,
        };
        continueButton.FlatAppearance.BorderSize = 0;
        continueButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);
        continueButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(67, 56, 202);

        buttons.Controls.Add(backButton);
        buttons.Controls.Add(continueButton);
        stack.Controls.Add(buttons);

        Controls.Add(stack);
        AcceptButton = continueButton;
        CancelButton = backButton;
    }
}
