using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.GLPI.Chamados;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI;

/// <summary>
/// Vista principal do módulo GLPI com navegação entre submódulos (WPF).
/// </summary>
public sealed class GLPIView : UserControl
{
    readonly Panel _submoduleHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = ShellTheme.MainBg,
    };

    readonly GlpiHeaderWpfControl _headerWpf;

    public GLPIView(bool openAbertura = false)
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _headerWpf = new GlpiHeaderWpfControl();
        _headerWpf.ChamadosTabClicked += (_, _) => ShowChamados();
        _headerWpf.AberturaTabClicked += (_, _) => ShowAberturaChamado();

        var topHost = new ElementHost
        {
            Dock = DockStyle.Top,
            Height = 132,
            BackColor = ShellTheme.MainBg,
            Child = _headerWpf,
        };

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Padding = new Padding(32, 0, 32, 24),
        };
        body.Controls.Add(_submoduleHost);

        Controls.Add(body);
        Controls.Add(topHost);

        if (openAbertura)
            ShowAberturaChamado();
        else
            ShowChamados();
    }

    void ClearSubmoduleHost()
    {
        while (_submoduleHost.Controls.Count > 0)
        {
            var c = _submoduleHost.Controls[0];
            _submoduleHost.Controls.Remove(c);
            c.Dispose();
        }
    }

    void ShowChamados()
    {
        _headerWpf.SetActiveTab(isChamadosActive: true);
        ClearSubmoduleHost();

        var view = new ChamadosView { Dock = DockStyle.Fill };
        view.AberturaChamadoSolicitada += (_, _) => ShowAberturaChamado();
        _submoduleHost.Controls.Add(view);
    }

    void ShowAberturaChamado()
    {
        _headerWpf.SetActiveTab(isChamadosActive: false);
        ClearSubmoduleHost();

        var view = new AberturaChamadoView { Dock = DockStyle.Fill };
        view.CancelarClicado += (_, _) => ShowChamados();
        view.SolicitarChamadoClicado += (_, _) => OnSolicitarChamadoAsync(view);
        _submoduleHost.Controls.Add(view);
    }

    void OnSolicitarChamadoAsync(AberturaChamadoView view)
    {
        var owner = view.FindForm() as Form
            ?? Form.ActiveForm
            ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);
        if (owner is null)
        {
            MessageBox.Show("Não foi possível abrir a janela de envio.", "SistecHub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        view.Enabled = false;
        using var aguarde = new EnvioChamadoAguardeForm
        {
            Owner = owner,
            StartPosition = FormStartPosition.CenterParent,
        };
        IProgress<string> progresso = new Progress<string>(m => aguarde.DefinirMensagem(m));
        aguarde.DefinirMensagem("A preparar…");

        Exception? erroDepois = null;
        int? ticketIdCriado = null;

        aguarde.Shown += async (_, _) =>
        {
            try
            {
                var settings = AppSettingsStore.Load();
                ticketIdCriado = await ChamadoParaGLPI.EnviarAsync(view, settings, progresso).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                erroDepois = ex;
            }
            finally
            {
                if (!aguarde.IsDisposed)
                    aguarde.Close();
            }
        };

        try
        {
            aguarde.ShowDialog(owner);
            if (erroDepois is not null)
            {
                MessageBox.Show(
                    erroDepois.Message,
                    "Não foi possível enviar o chamado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (ticketIdCriado is { } id)
            {
                MessageBox.Show(
                    $"Chamado criado no GLPI com sucesso (n.º {id}).",
                    "SistecHub",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                ShowChamados();
            }
        }
        finally
        {
            if (!view.IsDisposed)
                view.Enabled = true;
        }
    }
}
