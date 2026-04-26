namespace SistecHub.UI;

internal sealed class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string message)
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(40, 36, 40, 36);

        var lbl = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
        };

        var sub = new Label
        {
            Text = message,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0),
        };

        Controls.Add(sub);
        Controls.Add(lbl);
    }
}
