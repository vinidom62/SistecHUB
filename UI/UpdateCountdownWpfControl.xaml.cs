namespace SistecHub.UI;

public sealed partial class UpdateCountdownWpfControl : System.Windows.Controls.UserControl
{
    public UpdateCountdownWpfControl()
    {
        InitializeComponent();
    }

    public void SetTitle(string version)
    {
        TxtTitle.Text = $"Versão {version} pronta para instalar";
    }

    public void SetCountdown(int secondsRemaining)
    {
        TxtCountdown.Text = $"Reiniciando em {secondsRemaining} segundo{(secondsRemaining == 1 ? "" : "s")}...";
    }
}

