namespace SistecHub.UI;

internal sealed class HomeView : UserControl
{
    public HomeView()
    {
        BackColor = ShellTheme.MainBg;

        var welcomeStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.None,
        };

        var accentLine = new Panel
        {
            Width = 48,
            Height = 4,
            Margin = new Padding(0, 0, 0, 20),
            BackColor = ShellTheme.Accent,
        };

        var title = new Label
        {
            Text = "Olá",
            AutoSize = true,
            Font = new Font("Segoe UI", 32F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };

        var subtitle = new Label
        {
            Text = "Bem-vindo ao SistecHub — escolhe uma opção no menu à esquerda.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
        };

        welcomeStack.Controls.Add(accentLine);
        welcomeStack.Controls.Add(title);
        welcomeStack.Controls.Add(subtitle);

        var mainCenter = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        mainCenter.Layout += (_, _) =>
        {
            welcomeStack.Left = (mainCenter.ClientSize.Width - welcomeStack.Width) / 2;
            welcomeStack.Top = (mainCenter.ClientSize.Height - welcomeStack.Height) / 2;
        };

        mainCenter.Controls.Add(welcomeStack);
        Controls.Add(mainCenter);
    }
}
