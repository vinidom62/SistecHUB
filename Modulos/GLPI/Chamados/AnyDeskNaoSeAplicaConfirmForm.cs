using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Confirmação ao marcar AnyDesk como "não se aplica".</summary>
internal sealed class AnyDeskNaoSeAplicaConfirmForm : Form
{
    const string Mensagem =
        "Para realizarmos os atendimentos precisamos do número do AnyDesk para acesso remoto, tem certeza que não se aplica para este chamado?";

    const string TrechoNegrito = "precisamos do número do AnyDesk";

    readonly Button _confirmar;
    readonly System.Windows.Forms.Timer _cooldownTimer;
    int _segundosRestantes = 3;

    public AnyDeskNaoSeAplicaConfirmForm()
    {
        Text = "AnyDesk";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = ShellTheme.MainBg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ClientSize = new Size(480, 210);
        MinimumSize = new Size(480, 210);
        MaximumSize = new Size(480, 210);

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon is not null)
            Icon = appIcon;

        Win32Dwm.TryEnableRoundedCorners(this);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(28, 24, 28, 20),
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        var mensagem = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            TabStop = false,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.None,
            BackColor = ShellTheme.MainBg,
            ForeColor = ShellTheme.TextPrimary,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 12),
            Text = Mensagem,
        };
        var boldStart = Mensagem.IndexOf(TrechoNegrito, StringComparison.Ordinal);
        if (boldStart >= 0)
        {
            mensagem.Select(boldStart, TrechoNegrito.Length);
            mensagem.SelectionFont = new Font(mensagem.Font, FontStyle.Bold);
            mensagem.SelectionLength = 0;
            mensagem.SelectionStart = 0;
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };

        _confirmar = new Button
        {
            Text = "Confirmar ação (3)",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(165, 180, 252),
            Cursor = Cursors.Default,
            Enabled = false,
            DialogResult = DialogResult.None,
        };
        _confirmar.FlatAppearance.BorderSize = 0;
        _confirmar.FlatAppearance.MouseOverBackColor = Color.FromArgb(79, 70, 229);

        var cancelar = new Button
        {
            Text = "Cancelar",
            AutoSize = true,
            Height = 34,
            Padding = new Padding(16, 0, 16, 0),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel,
        };
        cancelar.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        cancelar.FlatAppearance.BorderSize = 1;
        cancelar.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);

        buttons.Controls.Add(_confirmar);
        buttons.Controls.Add(cancelar);
        stack.Controls.Add(mensagem, 0, 0);
        stack.Controls.Add(buttons, 0, 1);
        Controls.Add(stack);

        CancelButton = cancelar;

        _cooldownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _cooldownTimer.Tick += OnCooldownTick;
        Shown += (_, _) => _cooldownTimer.Start();
        FormClosed += (_, _) =>
        {
            _cooldownTimer.Stop();
            _cooldownTimer.Dispose();
        };

        _confirmar.Click += (_, _) =>
        {
            if (!_confirmar.Enabled)
                return;
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    void OnCooldownTick(object? sender, EventArgs e)
    {
        _segundosRestantes--;
        if (_segundosRestantes > 0)
        {
            _confirmar.Text = $"Confirmar ação ({_segundosRestantes})";
            return;
        }

        _cooldownTimer.Stop();
        _confirmar.Enabled = true;
        _confirmar.Text = "Confirmar ação";
        _confirmar.BackColor = ShellTheme.Accent;
        _confirmar.Cursor = Cursors.Hand;
    }
}
