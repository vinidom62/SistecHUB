using System.Windows.Forms.Integration;

namespace SistecHub.UI;

internal sealed class HomeView : UserControl
{
    public event Action<string>? NavigateRequested;
    public event Action<bool>? DarkModeChanged;

    public HomeView()
    {
        var control = new HomeWpfControl();
        var initialBg = control.IsDarkMode ? Color.FromArgb(20, 20, 22) : ShellTheme.MainBg;
        BackColor = initialBg;
        Padding = new Padding(0);

        control.NavigateRequested += pageId => NavigateRequested?.Invoke(pageId);

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = initialBg,
            Child = control,
        };

        control.DarkModeChanged += isDark =>
        {
            var bg = isDark ? Color.FromArgb(20, 20, 22) : ShellTheme.MainBg;
            BackColor = bg;
            host.BackColor = bg;
            DarkModeChanged?.Invoke(isDark);
        };

        Controls.Add(host);
    }
}
