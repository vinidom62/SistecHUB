using System.IO;
using System.Threading.Tasks;
using SistecHub.Core;
using SistecHub.Modulos.IA;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Formulário de abertura de chamado (UI alinhada ao mockup).
/// </summary>
public sealed class AberturaChamadoView : UserControl
{
    readonly TextBox _problemaTextBox;
    readonly TextBox _whatsappTextBox;
    readonly TextBox _nomeContatoTextBox;
    readonly TextBox _observacoesTextBox;
    readonly Panel _capturaHost;
    readonly Label _hintCaptura;
    readonly PictureBox _picCaptura;
    readonly Button _btnCapturaTela;
    readonly Button _anexoEscolherButton;
    readonly Button _anexoLimparButton;
    readonly Button _btnCancelar;
    readonly Button _btnSolicitarChamado;
    readonly Button _btnIaProblema;
    readonly ToolTip _anexoToolTip = new();
    string? _anexoCaminhoCompleto;
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
        Padding = new Padding(0, 8, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));

        var headerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };
        var accentBar = new Panel
        {
            Width = 4,
            Height = 28,
            Margin = new Padding(0, 6, 12, 0),
            BackColor = ShellTheme.Accent,
        };
        var titleLbl = new Label
        {
            Text = "Abertura de chamado",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0),
        };
        headerFlow.Controls.Add(accentBar);
        headerFlow.Controls.Add(titleLbl);
        grid.Controls.Add(headerFlow, 0, 0);
        grid.SetColumnSpan(headerFlow, 2);

        var leftTop = BuildLabeledMultiline(
            "Relate o problema técnico",
            out _problemaTextBox,
            marginRight: 10,
            placeholderText:
            "Exemplo: Após queda de energia, a impressora não liga." 
            + Environment.NewLine
            + Environment.NewLine
            + "Seja rico em detalhes, forneça informações relevantes"
            + Environment.NewLine
            + "Exemplo: Modelo da impressora",
            incluirBotaoIa: true,
            out var btnIaTmp);
        _btnIaProblema = btnIaTmp
            ?? throw new InvalidOperationException("O botão IA deveria ter sido criado com incluirBotaoIa: true.");
        _btnIaProblema.Click += async (_, _) => await MelhorarDescricaoComIaAsync().ConfigureAwait(true);
        grid.Controls.Add(leftTop, 0, 1);

        var rightTop = BuildCapturaColumn(
            out _capturaHost,
            out _hintCaptura,
            out _picCaptura,
            out _btnCapturaTela,
            out _anexoEscolherButton,
            out _anexoLimparButton);
        grid.Controls.Add(rightTop, 1, 1);

        var contacts = BuildContactsRow(
            out _whatsappTextBox,
            out _nomeContatoTextBox,
            marginRight: 10,
            whatsappPlaceholder: "(DDD) + Telefone",
            nomePlaceholder: "Seu nome");
        grid.Controls.Add(contacts, 0, 2);

        var rightMidSpacer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 0, 0),
        };
        grid.Controls.Add(rightMidSpacer, 1, 2);

        var leftBottom = BuildLabeledMultiline(
            "Observações importantes",
            out _observacoesTextBox,
            marginRight: 10,
            placeholderText: "Exemplo: estarei disponível somente até 18:00.",
            incluirBotaoIa: false,
            out _);
        grid.Controls.Add(leftBottom, 0, 3);

        var rightBottomActions = BuildBottomActionBar(out _btnCancelar, out _btnSolicitarChamado);
        grid.Controls.Add(rightBottomActions, 1, 3);

        void OnCapturaAreaClick(object? sender, EventArgs e) => OnAnexoEscolherClicked(sender, e);
        _capturaHost.Click += OnCapturaAreaClick;
        _hintCaptura.Click += OnCapturaAreaClick;
        _picCaptura.Click += OnCapturaAreaClick;

        _btnCapturaTela.Click += OnCapturaTelaClicked;
        _anexoEscolherButton.Click += OnAnexoEscolherClicked;
        _anexoLimparButton.Click += OnAnexoLimparClicked;
        _btnCancelar.Click += (_, _) => CancelarClicado?.Invoke(this, EventArgs.Empty);
        _btnSolicitarChamado.Click += (_, _) => SolicitarChamadoClicado?.Invoke(this, EventArgs.Empty);

        Disposed += (_, _) =>
        {
            if (_snipClipboardTimer is not null)
            {
                _snipClipboardTimer.Stop();
                _snipClipboardTimer.Tick -= OnSnipClipboardPollTick;
                _snipClipboardTimer.Dispose();
            }

            _anexoToolTip.Dispose();
            _picCaptura.Image?.Dispose();
        };

        Controls.Add(grid);
    }

    static TableLayoutPanel BuildLabeledMultiline(
        string titulo,
        out TextBox box,
        int marginRight,
        string placeholderText,
        bool incluirBotaoIa,
        out Button? botaoIa)
    {
        botaoIa = null;
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, marginRight, 12),
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        if (incluirBotaoIa)
        {
            // FlowLayoutPanel: o botão fica logo a seguir ao título (evita coluna Percent que empurra o botão para a direita).
            var labelRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 0),
                Padding = new Padding(0, 0, 0, 0),
            };

            var lbl = MakeFieldLabelBold(titulo);
            lbl.Margin = new Padding(0, 0, 0, 0);
            lbl.Anchor = AnchorStyles.Left;
            labelRow.Controls.Add(lbl);

            botaoIa = new Button
            {
                Text = "IA",
                AutoSize = true,
                Height = 2,
                Padding = new Padding(0, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(35, 142, 35),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0),
            };
            botaoIa.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            botaoIa.FlatAppearance.BorderSize = 1;
            botaoIa.FlatAppearance.MouseOverBackColor = Color.FromArgb(136, 231, 136);
            labelRow.Controls.Add(botaoIa);
            wrap.Controls.Add(labelRow, 0, 0);
        }
        else
            wrap.Controls.Add(MakeFieldLabelBold(titulo), 0, 0);

        box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.None,
            AcceptsReturn = true,
            WordWrap = true,
            MaxLength = 512,
            PlaceholderText = placeholderText,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
        };

        var shell = WrapTextBoxWithBorder(box, minHeight: 120);
        wrap.Controls.Add(shell, 0, 1);
        return wrap;
    }

    /// <summary>
    /// Com <see cref="TextBox.PlaceholderText"/>, o WinForms por vezes deixa de desenhar a borda do TextBox;
    /// o painel exterior garante o contorno visível.
    /// </summary>
    static Panel WrapTextBoxWithBorder(TextBox inner, int minHeight = 0)
    {
        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(2),
        };
        if (minHeight > 0)
            shell.MinimumSize = new Size(0, minHeight);

        inner.Dock = DockStyle.Fill;
        shell.Controls.Add(inner);
        shell.Click += (_, _) => inner.Focus();
        return shell;
    }

    static Panel WrapSingleLineTextBox(TextBox inner)
    {
        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Padding = new Padding(2, 3, 2, 3),
        };
        inner.Dock = DockStyle.Fill;
        inner.BorderStyle = BorderStyle.None;
        inner.BackColor = Color.White;
        shell.Controls.Add(inner);
        shell.Click += (_, _) => inner.Focus();
        return shell;
    }

    static TableLayoutPanel BuildContactsRow(
        out TextBox whatsapp,
        out TextBox nome,
        int marginRight,
        string whatsappPlaceholder,
        string nomePlaceholder)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, marginRight, 12),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

        t.Controls.Add(MakeFieldLabelBold("WhatsApp *"), 0, 0);
        t.Controls.Add(MakeFieldLabelBold("Nome de contato *"), 1, 0);

        whatsapp = MakeSingleLineTextBox(whatsappPlaceholder);
        nome = MakeSingleLineTextBox(nomePlaceholder);

        var shellWhatsapp = WrapSingleLineTextBox(whatsapp);
        shellWhatsapp.Margin = new Padding(0, 0, 8, 0);
        var shellNome = WrapSingleLineTextBox(nome);
        shellNome.Margin = new Padding(8, 0, 0, 0);

        t.Controls.Add(shellWhatsapp, 0, 1);
        t.Controls.Add(shellNome, 1, 1);
        return t;
    }

    static TableLayoutPanel BuildCapturaColumn(
        out Panel host,
        out Label hint,
        out PictureBox pic,
        out Button btnCapturaTela,
        out Button btnEscolher,
        out Button btnLimpar)
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 0, 12),
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        wrap.Controls.Add(MakeFieldLabelBold("Captura de tela"), 0, 0);

        host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            MinimumSize = new Size(100, 160),
            Cursor = Cursors.Hand,
        };

        var bottomStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = ShellTheme.MainBg,
        };

        btnCapturaTela = new Button
        {
            Text = "Captura de tela",
            AutoSize = true,
            Height = 28,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0),
        };
        btnCapturaTela.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btnCapturaTela.FlatAppearance.BorderSize = 1;
        btnCapturaTela.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);

        btnEscolher = new Button
        {
            Text = "Anexar arquivo",
            AutoSize = true,
            Height = 28,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0),
        };
        btnEscolher.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btnEscolher.FlatAppearance.BorderSize = 1;
        btnEscolher.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);

        btnLimpar = new Button
        {
            Text = "Limpar",
            AutoSize = true,
            Height = 28,
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
        };
        btnLimpar.FlatAppearance.BorderSize = 0;
        btnLimpar.FlatAppearance.MouseOverBackColor = Color.FromArgb(241, 245, 249);

        bottomStrip.Controls.Add(btnCapturaTela);
        bottomStrip.Controls.Add(btnEscolher);
        bottomStrip.Controls.Add(btnLimpar);

        pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Visible = false,
            Cursor = Cursors.Hand,
        };

        hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Clique para escolher\nimagem ou ficheiro",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
        };

        host.Controls.Add(bottomStrip);
        host.Controls.Add(pic);
        host.Controls.Add(hint);

        wrap.Controls.Add(host, 0, 1);
        return wrap;
    }

    static Panel BuildBottomActionBar(out Button cancelar, out Button solicitar)
    {
        const int btnHeight = 40;

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 0, 0),
        };

        const string disclaimerText =
            "Ao abrir este chamado, declaro estar ciente e de acordo com os termos de uso.";
        const string linkPhrase = "termos de uso";
        var disclaimer = new LinkLabel
        {
            Text = disclaimerText,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            LinkColor = ShellTheme.Accent,
            ActiveLinkColor = Color.FromArgb(67, 70, 200),
            VisitedLinkColor = ShellTheme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopRight,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0, 0, 0, 8),
            Dock = DockStyle.Fill,
        };
        var linkStart = disclaimerText.IndexOf(linkPhrase, StringComparison.OrdinalIgnoreCase);
        if (linkStart >= 0)
            disclaimer.LinkArea = new LinkArea(linkStart, linkPhrase.Length);

        disclaimer.LinkClicked += (_, _) =>
        {
            using var termos = new TermosDeUsoChamadosForm();
            termos.ShowDialog(disclaimer.FindForm());
        };

        void SyncDisclaimerWrapWidth()
        {
            if (host.ClientSize.Width > 0)
                disclaimer.MaximumSize = new Size(host.ClientSize.Width, 0);
        }

        host.Resize += (_, _) => SyncDisclaimerWrapWidth();

        var actionBar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionBar.RowStyles.Add(new RowStyle(SizeType.Absolute, btnHeight));

        solicitar = new Button
        {
            Text = "SOLICITAR CHAMADO",
            AutoSize = true,
            Padding = new Padding(18, 0, 18, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = ShellTheme.Accent,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        solicitar.FlatAppearance.BorderSize = 0;
        solicitar.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 82, 221);

        cancelar = new Button
        {
            Text = "CANCELAR",
            AutoSize = true,
            Padding = new Padding(16, 0, 16, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
        };
        cancelar.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        cancelar.FlatAppearance.BorderSize = 1;
        cancelar.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);

        actionBar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 0);
        actionBar.Controls.Add(cancelar, 1, 0);
        actionBar.Controls.Add(solicitar, 2, 0);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 12, 0, 0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        stack.Controls.Add(disclaimer, 0, 0);
        stack.Controls.Add(actionBar, 0, 1);

        host.Controls.Add(stack);
        SyncDisclaimerWrapWidth();
        return host;
    }

    static Label MakeFieldLabelBold(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6),
        };

    static TextBox MakeSingleLineTextBox(string placeholderText) =>
        new()
        {
            PlaceholderText = placeholderText,
            MaxLength = 30,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
        };

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

        _btnCapturaTela.Enabled = false;
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

        _btnCapturaTela.Enabled = true;
        _snipOwnerForm = null;
    }

    void OnAnexoLimparClicked(object? sender, EventArgs e)
    {
        _anexoCaminhoCompleto = null;
        _picCaptura.Image?.Dispose();
        _picCaptura.Image = null;
        _picCaptura.Visible = false;
        _hintCaptura.Visible = true;
        _hintCaptura.Text = "Clique para escolher\nimagem ou ficheiro";
        _anexoLimparButton.Enabled = false;
        _anexoToolTip.SetToolTip(_capturaHost, "");
    }

    void DefinirAnexo(string caminhoCompleto)
    {
        _anexoCaminhoCompleto = caminhoCompleto;
        _picCaptura.Image?.Dispose();
        _picCaptura.Image = null;

        if (LooksLikeImage(caminhoCompleto))
        {
            try
            {
                using var temp = new Bitmap(caminhoCompleto);
                _picCaptura.Image = new Bitmap(temp);
                _picCaptura.Visible = true;
                _hintCaptura.Visible = false;
            }
            catch
            {
                _picCaptura.Visible = false;
                _hintCaptura.Visible = true;
                _hintCaptura.Text = "Ficheiro:\n" + Path.GetFileName(caminhoCompleto);
            }
        }
        else
        {
            _picCaptura.Visible = false;
            _hintCaptura.Visible = true;
            _hintCaptura.Text = "Ficheiro anexado:\n" + Path.GetFileName(caminhoCompleto);
        }

        _anexoLimparButton.Enabled = true;
        _anexoToolTip.SetToolTip(_capturaHost, caminhoCompleto);
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
        var descricao = _problemaTextBox.Text.Trim();
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
        _btnIaProblema.Enabled = false;
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

            if (melhorado.Length > _problemaTextBox.MaxLength)
                melhorado = melhorado[.._problemaTextBox.MaxLength];

            _problemaTextBox.Text = melhorado;
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
            _btnIaProblema.Enabled = true;
            if (owner is not null && !owner.IsDisposed)
                owner.UseWaitCursor = false;
        }
    }

    /// <summary>Caminho local do anexo escolhido, ou <c>null</c> se não houver.</summary>
    public string? CaminhoAnexo => _anexoCaminhoCompleto;

    public string TextoProblema => _problemaTextBox.Text.Trim();

    public string Whatsapp => _whatsappTextBox.Text.Trim();

    public string NomeContato => _nomeContatoTextBox.Text.Trim();

    public string Observacoes => _observacoesTextBox.Text.Trim();
}
