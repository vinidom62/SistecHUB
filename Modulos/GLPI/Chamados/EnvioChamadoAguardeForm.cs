using System.Windows.Forms.Integration;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Diálogo para exibir o passo actual do envio do chamado (WPF).</summary>
internal sealed class EnvioChamadoAguardeForm : Form
{
    readonly EnvioChamadoAguardeWpfControl _wpfControl;

    internal EnvioChamadoAguardeForm()
    {
        Text = "A enviar chamado";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = false;
        BackColor = ShellTheme.MainBg;
        ClientSize = new Size(440, 110);
        Padding = new Padding(0);

        Load += (_, _) =>
        {
            if (Owner is not null)
                CenterToParent();
            else
                CenterToScreen();
        };

        _wpfControl = new EnvioChamadoAguardeWpfControl();

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfControl,
        };

        Controls.Add(host);
    }

    internal void DefinirMensagem(string mensagem)
    {
        if (IsDisposed)
            return;
        _wpfControl.SetStatusText(mensagem);
    }
}
