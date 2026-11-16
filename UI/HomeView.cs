using System.Windows.Forms.Integration;

namespace SistecHub.UI;

internal sealed class HomeView : UserControl
{
    public event Action<string>? NavigateRequested;

    public HomeView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        var control = new HomeWpfControl();
        control.NavigateRequested += pageId => NavigateRequested?.Invoke(pageId);

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = control,
        };

        Controls.Add(host);
    }
}
