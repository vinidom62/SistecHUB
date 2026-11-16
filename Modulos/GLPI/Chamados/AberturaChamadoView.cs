using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.AcessoRemoto;
using SistecHub.Modulos.IA;
using SistecHub.Modulos.Inventario;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Formulário de abertura de chamado renderizado em WPF.
/// </summary>
public sealed class AberturaChamadoView : UserControl
{
    readonly AberturaChamadoWpfControl _wpfView;
    string? _anexoCaminhoCompleto;
    bool _anyDeskNaoSeAplica;
    System.Windows.Forms.Timer? _snipClipboardTimer;
    uint _snipClipboardSeqStart;
    int _snipPollTickCount;
    Form? _snipOwnerForm;

    /// <summary>Utilizador clicou em CANCELAR.</summary>
    public event EventHandler? CancelarClicado;

    /// <summary>Utilizador clicou em SOLICITAR CHAMADO.</summary>
    public event EventHandler? SolicitarChamadoClicado;

    public AberturaChamadoView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _wpfView = new AberturaChamadoWpfControl();
        _wpfView.IaProblemaClicked += async (_, _) => await MelhorarDescricaoComIaAsync().ConfigureAwait(true);
        _wpfView.CapturaAreaClicked += OnAnexoEscolherClicked;
        _wpfView.CapturaTelaClicked += OnCapturaTelaClicked;
        _wpfView.AnexoEscolherClicked += OnAnexoEscolherClicked;
        _wpfView.AnexoLimparClicked += OnAnexoLimparClicked;
        _wpfView.CapturarAcessoRemotoClicked += OnCapturarAcessoRemotoClicked;
        _wpfView.AcessoRemotoNaoSeAplicaClicked += OnAcessoRemotoNaoSeAplicaClicked;
        _wpfView.AcessoRemotoTextChangedByUser += (_, _) => DefinirAcessoRemotoAtivo();
        _wpfView.TermosClicked += (_, _) =>
        {
            using var termos = new TermosDeUsoChamadosForm();
            termos.ShowDialog(FindForm());
        };
        _wpfView.CancelarClicked += (_, _) => CancelarClicado?.Invoke(this, EventArgs.Empty);
        _wpfView.SolicitarChamadoClicked += (_, _) => SolicitarChamadoClicado?.Invoke(this, EventArgs.Empty);

        PreencherRemotoIdPadrao();

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Disposed += (_, _) =>
        {
            if (_snipClipboardTimer is not null)
            {
                _snipClipboardTimer.Stop();
                _snipClipboardTimer.Tick -= OnSnipClipboardPollTick;
                _snipClipboardTimer.Dispose();
            }
        };

        Controls.Add(host);
    }

    void OnCapturarAcessoRemotoClicked(object? sender, EventArgs e)
    {
        DefinirAcessoRemotoAtivo();

        // 1. Capturar ID SistecHUB
        PreencherRemotoIdPadrao();

        // 2. Capturar AnyDesk
        try
        {
            var id = InventarioAcessoRemotoReader.ReadAcessoRemoto().AnyDeskId;
            if (!string.IsNullOrWhiteSpace(id))
            {
                _wpfView.AnyDesk = id;
            }
        }
        catch (Exception ex)
        {
            AppDebugLog.Warn("AberturaChamado", "Erro ao capturar AnyDesk: " + ex.Message);
        }
    }

    void OnAcessoRemotoNaoSeAplicaClicked(object? sender, EventArgs e)
    {
        using var dlg = new AnyDeskNaoSeAplicaConfirmForm();
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        _anyDeskNaoSeAplica = true;
        _wpfView.SetAcessoRemotoVisualState(ativo: false);
    }

    void DefinirAcessoRemotoAtivo()
    {
        if (!_anyDeskNaoSeAplica)
            return;

        _anyDeskNaoSeAplica = false;
        _wpfView.SetAcessoRemotoVisualState(ativo: true);
    }

    void OnCapturarAnyDeskClicked(object? sender, EventArgs e) => OnCapturarAcessoRemotoClicked(sender, e);
    void OnAnyDeskNaoSeAplicaClicked(object? sender, EventArgs e) => OnAcessoRemotoNaoSeAplicaClicked(sender, e);
    void DefinirAnyDeskObrigatorio() => DefinirAcessoRemotoAtivo();

    void OnAnexoEscolherClicked(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Anexar captura de ecrã ou ficheiro",
            Filter =
                "Imagens (PNG, JPEG, GIF, BMP)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|"
                + "Todos os ficheiros (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        DefinirAnexo(dlg.FileName);
    }

    async void OnCapturaTelaClicked(object? sender, EventArgs e)
    {
        if (_snipClipboardTimer is { Enabled: true })
            return;

        var form = FindForm();
        if (form is null)
            return;

        _wpfView.SetCapturaTelaButtonEnabled(false);
        _snipOwnerForm = form;
        _snipClipboardSeqStart = WindowsSnipHelper.GetClipboardSequenceNumber();
        _snipPollTickCount = 0;

        form.WindowState = FormWindowState.Minimized;
        await Task.Delay(450).ConfigureAwait(true);

        WindowsSnipHelper.TryLaunchSnippingUi();

        _snipClipboardTimer ??= new System.Windows.Forms.Timer { Interval = 250 };
        _snipClipboardTimer.Tick -= OnSnipClipboardPollTick;
        _snipClipboardTimer.Tick += OnSnipClipboardPollTick;
        _snipClipboardTimer.Start();
    }

    void OnSnipClipboardPollTick(object? sender, EventArgs e)
    {
        const int maxTicks = 480;

        _snipPollTickCount++;
        var seq = WindowsSnipHelper.GetClipboardSequenceNumber();
        if (seq != _snipClipboardSeqStart && Clipboard.ContainsImage())
        {
            var path = WindowsSnipHelper.TrySaveClipboardImageToTempPng();
            if (path is not null)
            {
                DefinirAnexo(path);
                FinishSnipSession(restoreWindow: true);
                return;
            }
        }

        if (_snipPollTickCount >= maxTicks)
            FinishSnipSession(restoreWindow: true);
    }

    void FinishSnipSession(bool restoreWindow)
    {
        if (_snipClipboardTimer is not null)
        {
            _snipClipboardTimer.Stop();
            _snipClipboardTimer.Tick -= OnSnipClipboardPollTick;
        }

        if (restoreWindow)
        {
            var f = _snipOwnerForm;
            if (f is not null && !f.IsDisposed)
            {
                f.WindowState = FormWindowState.Normal;
                f.Show();
                f.Activate();
            }
        }

        _wpfView.SetCapturaTelaButtonEnabled(true);
        _snipOwnerForm = null;
    }

    void OnAnexoLimparClicked(object? sender, EventArgs e)
    {
        _anexoCaminhoCompleto = null;
        _wpfView.ClearAttachmentPreview();
    }

    void DefinirAnexo(string caminhoCompleto)
    {
        _anexoCaminhoCompleto = caminhoCompleto;
        _wpfView.ShowAttachmentPreview(caminhoCompleto, LooksLikeImage(caminhoCompleto));
    }

    static bool LooksLikeImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp";
    }

    const string PromptMelhorarChamado =
        "Não adicione ou remova informações existentes, apenas torne o texto mais compreensível para um chamado técnico. Me retorno apenas o texto melhorado, sem guias.";

    async Task MelhorarDescricaoComIaAsync()
    {
        var descricao = _wpfView.TextoProblema;
        if (descricao.Length == 0)
        {
            MessageBox.Show(
                "Escreva a descrição do problema antes de usar a IA.",
                "SistecHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var owner = FindForm();
        _wpfView.SetIaButtonEnabled(false);
        if (owner is not null)
            owner.UseWaitCursor = true;

        try
        {
            var settings = AppSettingsStore.Load();
            var userPrompt = PromptMelhorarChamado + Environment.NewLine + Environment.NewLine + descricao;
            var completion = await GroqClient.CompleteUserPromptAsync(settings, userPrompt).ConfigureAwait(true);
            var melhorado = completion.Content.Trim();
            if (melhorado.Length == 0)
            {
                MessageBox.Show(
                    "A API não devolveu texto. Tente novamente.",
                    "SistecHub",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _wpfView.TextoProblema = melhorado;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "IA (Groq)",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _wpfView.SetIaButtonEnabled(true);
            if (owner is not null && !owner.IsDisposed)
                owner.UseWaitCursor = false;
        }
    }

    void PreencherRemotoIdPadrao()
    {
        try
        {
            var snap = AcessoRemotoAgentService.GetSnapshot();
            var id = !string.IsNullOrWhiteSpace(snap.AssignedId)
                ? snap.AssignedId
                : AppSettingsStore.Load().RemotoId;

            if (string.IsNullOrWhiteSpace(id))
            {
                var settings = AppSettingsStore.Load();
                id = AppSettingsStore.EnsureRemotoId(settings);
            }

            var formatted = !string.IsNullOrWhiteSpace(snap.FormattedId) && !snap.FormattedId.Contains("---")
                ? snap.FormattedId
                : AcessoRemotoAgentService.FormatId(id);

            if (!string.IsNullOrWhiteSpace(formatted) && !formatted.Contains("---"))
                _wpfView.RemotoId = formatted;
        }
        catch (Exception ex)
        {
            AppDebugLog.Warn("AberturaChamado", "Não foi possível carregar ID de acesso remoto: " + ex.Message);
        }
    }

    /// <summary>Caminho local do anexo escolhido, ou <c>null</c> se não houver.</summary>
    public string? CaminhoAnexo => _anexoCaminhoCompleto;

    public string TextoProblema => _wpfView.TextoProblema;

    public string Whatsapp => _wpfView.Whatsapp;

    public string NomeContato => _wpfView.NomeContato;

    public string Observacoes => _wpfView.Observacoes;

    /// <summary>ID do Acesso Remoto SistecHUB informado, ou vazio.</summary>
    public string RemotoId => _wpfView.RemotoId;

    /// <summary>Número AnyDesk informado, ou vazio.</summary>
    public string AnyDesk => _wpfView.AnyDesk;

    /// <summary>Indica se o utilizador confirmou que AnyDesk não se aplica a este chamado.</summary>
    public bool AnyDeskNaoSeAplica => _anyDeskNaoSeAplica;
}
