namespace SistecHub.Modulos.GLPI.Chamados;

public sealed partial class EnvioChamadoAguardeWpfControl : System.Windows.Controls.UserControl
{
    public EnvioChamadoAguardeWpfControl()
    {
        InitializeComponent();
    }

    public void SetStatusText(string text)
    {
        TxtStatus.Text = text ?? "";
    }
}

