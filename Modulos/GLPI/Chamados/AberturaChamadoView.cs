using System.IO;
using System.Text;
using System.Threading.Tasks;
using SistecHub.Core;
using SistecHub.Modulos.IA;
using SistecHub.Modulos.Inventario;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Formulário de abertura de chamado (UI alinhada ao mockup).
/// </summary>
public sealed class AberturaChamadoView : UserControl
{
    /// <summary>Tom de borda opaco partilhado por campos e pela área de captura.</summary>
    static readonly Color FieldBorderColor = Color.FromArgb(203, 213, 225);

    static readonly Color PastelGreenBg = Color.FromArgb(220, 252, 231);
    static readonly Color PastelGreenHover = Color.FromArgb(187, 247, 208);
    static readonly Color PastelGreenBorder = Color.FromArgb(134, 239, 172);
    static readonly Color PastelGreenText = Color.FromArgb(22, 101, 52);

    static readonly Color PastelRedBg = Color.FromArgb(254, 226, 226);
    static readonly Color PastelRedHover = Color.FromArgb(254, 202, 202);
    static readonly Color PastelRedBorder = Color.FromArgb(252, 165, 165);
    static readonly Color PastelRedText = Color.FromArgb(153, 27, 27);
    static readonly Color PastelRedActiveBg = Color.FromArgb(254, 202, 202);

    readonly TextBox _problemaTextBox;
    readonly TextBox _whatsappTextBox;
    readonly TextBox _nomeContatoTextBox;
    readonly TextBox _observacoesTextBox;
    readonly TextBox _anyDeskTextBox;
    readonly Panel _anyDeskShell;
    readonly Label _anyDeskLabel;
    readonly Panel _capturaHost;
    readonly Label _hintCaptura;
    readonly PictureBox _picCaptura;
    readonly Button _btnCapturaTela;
    readonly Button _anexoEscolherButton;
    readonly Button _anexoLimparButton;
    readonly Button _btnCapturarAnyDesk;
    readonly Button _btnAnyDeskNaoSeAplica;
    readonly Button _btnCancelar;
    readonly Button _btnSolicitarChamado;
    readonly Button _btnIaProblema;
    readonly ToolTip _anexoToolTip = new();
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
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
            whatsappPlaceholder: "(DDD) + Telefone",
            nomePlaceholder: "Seu nome");
        contacts.Margin = new Padding(0, 0, 10, 12);
        grid.Controls.Add(contacts, 0, 2);
        AttachSomenteDigitos(_whatsappTextBox);
        AttachSomenteLetras(_nomeContatoTextBox);

        var anyDeskPanel = BuildAnyDeskColumn(
            out _anyDeskLabel,
            out _anyDeskTextBox,
            out _anyDeskShell,
            out _btnCapturarAnyDesk,
            out _btnAnyDeskNaoSeAplica);
        anyDeskPanel.Margin = new Padding(10, 0, 0, 12);
        grid.Controls.Add(anyDeskPanel, 1, 2);
        AttachSomenteDigitos(_anyDeskTextBox);
        _anyDeskTextBox.TextChanged += (_, _) =>
        {
            if (_anyDeskTextBox.TextLength > 0)
                DefinirAnyDeskObrigatorio();
        };

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
        _btnCapturarAnyDesk.Click += OnCapturarAnyDeskClicked;
        _btnAnyDeskNaoSeAplica.Click += OnAnyDeskNaoSeAplicaClicked;
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
            Margin = new Padding(0, 0, marginRight, 8),
        };
        // Altura fixa igual à coluna da captura — alinha as caixas.
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        if (incluirBotaoIa)
        {
            // FlowLayoutPanel: o botão fica logo a seguir ao título (evita coluna Percent que empurra o botão para a direita).
            var labelRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            var lbl = MakeFieldLabelBold(titulo);
            lbl.Margin = new Padding(0, 4, 6, 0);
            lbl.Anchor = AnchorStyles.Left;
            labelRow.Controls.Add(lbl);

            botaoIa = new Button
            {
                Text = "IA",
                AutoSize = false,
                Size = new Size(32, 22),
                Padding = new Padding(0),
                Margin = new Padding(0, 2, 0, 3),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = PastelGreenText,
                BackColor = PastelGreenBg,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            botaoIa.FlatAppearance.BorderColor = PastelGreenBorder;
            botaoIa.FlatAppearance.BorderSize = 1;
            botaoIa.FlatAppearance.MouseOverBackColor = PastelGreenHover;
            labelRow.Controls.Add(botaoIa);
            wrap.Controls.Add(labelRow, 0, 0);
        }
        else
        {
            var lbl = MakeFieldLabelBold(titulo);
            lbl.Margin = new Padding(0, 4, 0, 0);
            wrap.Controls.Add(lbl, 0, 0);
        }

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
    /// o painel exterior garante o contorno visível no tom opaco do formulário.
    /// </summary>
    static Panel WrapTextBoxWithBorder(TextBox inner, int minHeight = 0)
    {
        var frame = CreateOpaqueBorderFrame(out var body, minHeight);
        body.Padding = new Padding(2);
        inner.Dock = DockStyle.Fill;
        body.Controls.Add(inner);
        body.Click += (_, _) => inner.Focus();
        frame.Click += (_, _) => inner.Focus();
        return frame;
    }

    static Panel WrapSingleLineTextBox(TextBox inner)
    {
        inner.BorderStyle = BorderStyle.None;
        inner.BackColor = Color.White;
        var frame = CreateOpaqueBorderFrame(out var body, minHeight: 0);
        body.Padding = new Padding(2, 3, 2, 3);
        inner.Dock = DockStyle.Fill;
        body.Controls.Add(inner);
        body.Click += (_, _) => inner.Focus();
        frame.Click += (_, _) => inner.Focus();
        return frame;
    }

    /// <summary>
    /// Borda 1px com painéis Dock (não usa Padding — TableLayout interno cobria a base).
    /// <paramref name="body"/> é a área interior branca; o frame mantém a cor da borda nas arestas.
    /// </summary>
    static Panel CreateOpaqueBorderFrame(out Panel body, int minHeight)
    {
        var frame = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        if (minHeight > 0)
            frame.MinimumSize = new Size(0, minHeight);

        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = FieldBorderColor,
            Margin = new Padding(0),
        };
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = FieldBorderColor,
            Margin = new Padding(0),
        };
        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 1,
            BackColor = FieldBorderColor,
            Margin = new Padding(0),
        };
        var right = new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = FieldBorderColor,
            Margin = new Padding(0),
        };

        // Ordem: Fill primeiro; Top/Bottom por último para a base nunca ser coberta.
        frame.Controls.Add(body);
        frame.Controls.Add(left);
        frame.Controls.Add(right);
        frame.Controls.Add(top);
        frame.Controls.Add(bottom);
        return frame;
    }

    static TableLayoutPanel BuildContactsRow(
        out TextBox whatsapp,
        out TextBox nome,
        string whatsappPlaceholder,
        string nomePlaceholder)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

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

    static TableLayoutPanel BuildAnyDeskColumn(
        out Label lbl,
        out TextBox anyDesk,
        out Panel shell,
        out Button btnCapturar,
        out Button btnNaoSeAplica)
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lbl = MakeFieldLabelBold("AnyDesk *");
        wrap.Controls.Add(lbl, 0, 0);

        anyDesk = MakeSingleLineTextBox("Somente números");
        anyDesk.MaxLength = 15;
        shell = WrapSingleLineTextBox(anyDesk);
        wrap.Controls.Add(shell, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(0),
        };

        btnCapturar = MakeSecondaryActionButton(
            "Capturar meu anydesk",
            marginRight: 8,
            PastelGreenBg,
            PastelGreenHover,
            PastelGreenBorder,
            PastelGreenText);
        btnNaoSeAplica = MakeSecondaryActionButton(
            "Não se aplica",
            marginRight: 0,
            PastelRedBg,
            PastelRedHover,
            PastelRedBorder,
            PastelRedText);

        buttons.Controls.Add(btnCapturar);
        buttons.Controls.Add(btnNaoSeAplica);
        wrap.Controls.Add(buttons, 0, 2);
        return wrap;
    }

    static Button MakeSecondaryActionButton(
        string text,
        int marginRight,
        Color backColor,
        Color hoverColor,
        Color borderColor,
        Color foreColor)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = foreColor,
            BackColor = backColor,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, marginRight, 0),
        };
        btn.FlatAppearance.BorderColor = borderColor;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = hoverColor;
        return btn;
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
            Margin = new Padding(10, 0, 0, 8),
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var capturaLbl = MakeFieldLabelBold("Captura de tela");
        capturaLbl.Margin = new Padding(0, 4, 0, 0);
        wrap.Controls.Add(capturaLbl, 0, 0);

        var frame = CreateOpaqueBorderFrame(out var body, minHeight: 0);
        body.Padding = new Padding(0);

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
        };

        var bottomStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
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
        btnCapturaTela.FlatAppearance.BorderColor = FieldBorderColor;
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
        btnEscolher.FlatAppearance.BorderColor = FieldBorderColor;
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

        host.Controls.Add(pic);
        host.Controls.Add(hint);
        inner.Controls.Add(host, 0, 0);
        inner.Controls.Add(bottomStrip, 0, 1);
        body.Controls.Add(inner);

        wrap.Controls.Add(frame, 0, 1);
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
            Margin = new Padding(0, 0, 0, 0),
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

    static void AttachSomenteLetras(TextBox box)
    {
        box.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar))
                return;
            if (!char.IsLetter(e.KeyChar) && e.KeyChar is not ' ' and not '-' and not '\'')
                e.Handled = true;
        };
        box.TextChanged += (_, _) =>
        {
            var filtered = FiltrarSomenteLetras(box.Text);
            if (filtered == box.Text)
                return;
            var sel = Math.Min(box.SelectionStart, filtered.Length);
            box.Text = filtered;
            box.SelectionStart = sel;
        };
    }

    static void AttachSomenteDigitos(TextBox box)
    {
        box.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar))
                return;
            if (!char.IsDigit(e.KeyChar))
                e.Handled = true;
        };
        box.TextChanged += (_, _) =>
        {
            var filtered = FiltrarSomenteDigitos(box.Text);
            if (filtered == box.Text)
                return;
            var sel = Math.Min(box.SelectionStart, filtered.Length);
            box.Text = filtered;
            box.SelectionStart = sel;
        };
    }

    static string FiltrarSomenteLetras(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) || ch is ' ' or '-' or '\'')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    static string FiltrarSomenteDigitos(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    void OnCapturarAnyDeskClicked(object? sender, EventArgs e)
    {
        var id = InventarioAcessoRemotoReader.ReadAcessoRemoto().AnyDeskId;
        if (string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show(
                "Não foi possível obter o número do AnyDesk nesta máquina. Verifique se o AnyDesk está instalado ou digite o número manualmente.",
                "SistecHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DefinirAnyDeskObrigatorio();
        _anyDeskTextBox.Text = id;
        _anyDeskTextBox.SelectionStart = _anyDeskTextBox.Text.Length;
        _anyDeskTextBox.Focus();
    }

    void OnAnyDeskNaoSeAplicaClicked(object? sender, EventArgs e)
    {
        using var dlg = new AnyDeskNaoSeAplicaConfirmForm();
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        _anyDeskNaoSeAplica = true;
        _anyDeskLabel.Text = "AnyDesk";
        _anyDeskTextBox.Clear();
        AplicarEstadoVisualAnyDesk(ativo: false);
    }

    void DefinirAnyDeskObrigatorio()
    {
        if (!_anyDeskNaoSeAplica)
            return;

        _anyDeskNaoSeAplica = false;
        _anyDeskLabel.Text = "AnyDesk *";
        AplicarEstadoVisualAnyDesk(ativo: true);
    }

    void AplicarEstadoVisualAnyDesk(bool ativo)
    {
        var bg = ativo ? Color.White : Color.FromArgb(226, 232, 240);
        // Corpo interior = primeiro controlo Dock.Fill do frame com bordas Dock.
        foreach (Control child in _anyDeskShell.Controls)
        {
            if (child.Dock == DockStyle.Fill)
            {
                child.BackColor = bg;
                break;
            }
        }
        _anyDeskTextBox.BackColor = bg;
        _anyDeskTextBox.ReadOnly = !ativo;
        _anyDeskTextBox.ForeColor = ativo ? ShellTheme.TextPrimary : ShellTheme.TextMuted;
        _anyDeskTextBox.Cursor = ativo ? Cursors.IBeam : Cursors.Default;
        _btnAnyDeskNaoSeAplica.BackColor = ativo ? PastelRedBg : PastelRedActiveBg;
    }

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

    /// <summary>Número AnyDesk informado, ou vazio.</summary>
    public string AnyDesk => _anyDeskTextBox.Text.Trim();

    /// <summary>Indica se o utilizador confirmou que AnyDesk não se aplica a este chamado.</summary>
    public bool AnyDeskNaoSeAplica => _anyDeskNaoSeAplica;
}
