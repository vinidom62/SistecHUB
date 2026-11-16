namespace SistecHub.UI;

public sealed partial class ServiceStartupWaitWpfControl : System.Windows.Controls.UserControl
{
    public ServiceStartupWaitWpfControl()
    {
        InitializeComponent();
    }

    public void SetMessage(string message)
    {
        TxtMessage.Text = message ?? "";
    }

    public void SetCountdown(string text)
    {
        TxtCountdown.Text = text ?? "";
    }
}

