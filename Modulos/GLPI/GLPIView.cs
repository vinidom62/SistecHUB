using SistecHub.Core;
using SistecHub.Modulos.GLPI.Chamados;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI;

/// <summary>
/// Vista principal do módulo GLPI com navegação entre submódulos.
/// </summary>
public sealed class GLPIView : UserControl
{
    readonly Panel _submoduleHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = ShellTheme.MainBg,
    };

    Button? _activeNavButton;
    readonly Button _navChamados;
    readonly Button _navAbertura;

    public GLPIView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = ShellTheme.MainBg,
            Padding = new Padding(32, 24, 32, 0),
        };

        var header = new Label
        {
            Text = "Sistema de Tickets",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4),
        };

        var accentRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };
        accentRow.Controls.Add(new Panel
        {
            Width = 56,
            Height = 4,
            BackColor = ShellTheme.Accent,
        });

        var navRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
        };

        _navChamados = CreateNavButton("Chamados");
        _navAbertura = CreateNavButton("Abertura de chamado");
        _navChamados.Click += (_, _) => ShowChamados(_navChamados, _navAbertura);
        _navAbertura.Click += (_, _) => ShowAberturaChamado(_navAbertura);
        navRow.Controls.Add(_navChamados);
        navRow.Controls.Add(_navAbertura);

        top.Controls.Add(header);
        top.Controls.Add(accentRow);
        top.Controls.Add(navRow);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Padding = new Padding(32, 0, 32, 28),
        };
        body.Controls.Add(_submoduleHost);

        Controls.Add(body);
        Controls.Add(top);

        ShowChamados(_navChamados, _navAbertura);
    }

    static Button CreateNavButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            Height = 36,
            AutoSize = true,
            Padding = new Padding(16, 0, 16, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
        return btn;
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

    void ShowChamados(Button navButton, Button btnAbertura)
    {
        SetActiveNav(navButton);
        ClearSubmoduleHost();

        var view = new ChamadosView { Dock = DockStyle.Fill };
        view.AberturaChamadoSolicitada += (_, _) => ShowAberturaChamado(btnAbertura);
        _submoduleHost.Controls.Add(view);
    }

    void ShowAberturaChamado(Button navButton)
    {
        SetActiveNav(navButton);
        ClearSubmoduleHost();

        var view = new AberturaChamadoView { Dock = DockStyle.Fill };
        view.CancelarClicado += (_, _) => ShowChamados(_navChamados, _navAbertura);
        view.SolicitarChamadoClicado += async (_, _) => await OnSolicitarChamadoAsync(view).ConfigureAwait(true);
        _submoduleHost.Controls.Add(view);
    }

    async Task OnSolicitarChamadoAsync(AberturaChamadoView view)
    {
        view.Enabled = false;
        try
        {
            var settings = AppSettingsStore.Load();
            var ticketId = await ChamadoParaGLPI.EnviarAsync(view, settings).ConfigureAwait(true);
            MessageBox.Show(
                $"Chamado criado no GLPI com sucesso (n.º {ticketId}).",
                "SistecHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            ShowChamados(_navChamados, _navAbertura);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Não foi possível enviar o chamado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            if (!view.IsDisposed)
                view.Enabled = true;
        }
    }

    void SetActiveNav(Button navButton)
    {
        if (_activeNavButton != null)
        {
            _activeNavButton.BackColor = Color.FromArgb(241, 245, 249);
            _activeNavButton.ForeColor = ShellTheme.TextPrimary;
            _activeNavButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        }

        _activeNavButton = navButton;
        _activeNavButton.BackColor = Color.FromArgb(238, 242, 255);
        _activeNavButton.ForeColor = ShellTheme.Accent;
        _activeNavButton.FlatAppearance.BorderColor = ShellTheme.Accent;
    }
}
