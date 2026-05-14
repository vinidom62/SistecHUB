using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Diálogo para exibir o passo actual do envio do chamado (evita a sensação de interface bloqueada).</summary>
internal sealed class EnvioChamadoAguardeForm : Form
{
    readonly Label _status = new()
    {
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
        ForeColor = ShellTheme.TextPrimary,
        BackColor = Color.Transparent,
    };

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
        ClientSize = new Size(440, 100);
        Padding = new Padding(20, 18, 20, 18);
        // Com Show, CenterParent ignora às vezes; após carga garante o centro do parent.
        Load += (_, _) =>
        {
            if (Owner is not null)
                CenterToParent();
            else
                CenterToScreen();
        };
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Padding = new Padding(0, 0, 0, 0),
        };
        container.Controls.Add(_status);
        Controls.Add(container);
    }

    internal void DefinirMensagem(string mensagem)
    {
        if (IsDisposed)
            return;
        _status.Text = mensagem;
        if (IsHandleCreated)
            _status.Refresh();
    }
}
