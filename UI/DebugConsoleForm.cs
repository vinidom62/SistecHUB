using SistecHub.Core;

namespace SistecHub.UI;

/// <summary>Consola técnica com logs em tempo real para testes e diagnóstico.</summary>
internal sealed class DebugConsoleForm : Form
{
    static readonly Color Bg = Color.FromArgb(12, 12, 12);
    static readonly Color ToolbarBg = Color.FromArgb(24, 24, 24);
    static readonly Color Border = Color.FromArgb(55, 55, 55);

    readonly RichTextBox _logBox;
    bool _autoScroll = true;

    public DebugConsoleForm()
    {
        Text = "SistecHub — Modo Debug";
        ClientSize = new Size(920, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 360);
        BackColor = Bg;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ShowInTaskbar = true;

        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null)
            Icon = appIcon;

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = ToolbarBg,
            Padding = new Padding(12, 8, 12, 8),
        };

        var clearButton = MakeToolbarButton("Limpar");
        clearButton.Click += (_, _) => ClearLog();

        var copyButton = MakeToolbarButton("Copiar tudo");
        copyButton.Click += (_, _) => CopyAll();

        var autoScrollCheck = new CheckBox
        {
            Text = "Auto-scroll",
            Checked = true,
            AutoSize = true,
            ForeColor = Color.FromArgb(203, 213, 225),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(16, 6, 0, 0),
        };
        autoScrollCheck.CheckedChanged += (_, _) => _autoScroll = autoScrollCheck.Checked;

        var hint = new Label
        {
            Text = "Logs técnicos em tempo real",
            AutoSize = true,
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
        };

        toolbar.Controls.Add(clearButton);
        toolbar.Controls.Add(copyButton);
        toolbar.Controls.Add(autoScrollCheck);
        toolbar.Controls.Add(hint);
        toolbar.Resize += (_, _) =>
        {
            hint.Left = Math.Max(clearButton.Right + 12, toolbar.ClientSize.Width - hint.Width - 8);
            hint.Top = (toolbar.ClientSize.Height - hint.Height) / 2;
        };

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Bg,
            ForeColor = Color.FromArgb(226, 232, 240),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9.75F, FontStyle.Regular, GraphicsUnit.Point),
            DetectUrls = false,
            HideSelection = false,
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
        };
        _logBox.VScroll += (_, _) =>
        {
            if (!_logBox.IsHandleCreated)
                return;

            var visibleBottom = _logBox.GetCharIndexFromPosition(new Point(1, _logBox.ClientSize.Height - 4));
            _autoScroll = visibleBottom >= _logBox.TextLength - 4;
        };

        var logHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 12),
            BackColor = Bg,
        };
        logHost.Controls.Add(_logBox);

        var topBorder = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Border,
        };

        Controls.Add(logHost);
        Controls.Add(topBorder);
        Controls.Add(toolbar);

        Load += OnFormLoad;
        FormClosed += OnFormClosed;
    }

    static Button MakeToolbarButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = Color.FromArgb(38, 38, 38),
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    void OnFormLoad(object? sender, EventArgs e)
    {
        Load -= OnFormLoad;
        foreach (var entry in AppDebugLog.GetHistory())
            AppendEntry(entry, scrollToEnd: false);

        if (_logBox.TextLength > 0)
            _logBox.SelectionStart = _logBox.TextLength;

        AppDebugLog.EntryAdded += OnLogEntryAdded;
        AppDebugLog.Info("Debug", "Consola de debug aberta.");

        AppendUpdateLogSection();
    }

    void AppendUpdateLogSection()
    {
        var tail = UpdateActivityLog.ReadTail(30);
        if (tail.StartsWith("(sem entradas", StringComparison.Ordinal))
            return;

        AppendRawBlock("--- update.log ---", Color.FromArgb(148, 163, 184));
        AppendRawBlock(tail + Environment.NewLine, Color.FromArgb(203, 213, 225));
    }

    void AppendRawBlock(string text, Color color)
    {
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color;
        _logBox.AppendText(text);
        if (_autoScroll)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    void OnFormClosed(object? sender, FormClosedEventArgs e) =>
        AppDebugLog.EntryAdded -= OnLogEntryAdded;

    void OnLogEntryAdded(AppDebugLogEntry entry)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendEntry(entry));
            return;
        }

        AppendEntry(entry);
    }

    void AppendEntry(AppDebugLogEntry entry, bool scrollToEnd = true)
    {
        var line = AppDebugLog.FormatLine(entry) + Environment.NewLine;
        var color = LevelColor(entry.Level);

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color;
        _logBox.AppendText(line);

        if (scrollToEnd && _autoScroll)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }

    static Color LevelColor(AppDebugLogLevel level) => level switch
    {
        AppDebugLogLevel.Debug => Color.FromArgb(148, 163, 184),
        AppDebugLogLevel.Info => Color.FromArgb(226, 232, 240),
        AppDebugLogLevel.Warn => Color.FromArgb(251, 191, 36),
        AppDebugLogLevel.Error => Color.FromArgb(248, 113, 113),
        _ => Color.White,
    };

    void ClearLog()
    {
        _logBox.Clear();
        AppDebugLog.Info("Debug", "Consola limpa (histórico em memória mantido).");
    }

    void CopyAll()
    {
        if (_logBox.TextLength == 0)
            return;

        try
        {
            Clipboard.SetText(_logBox.Text);
            AppDebugLog.Info("Debug", "Logs copiados para a área de transferência.");
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("Debug", ex, "Falha ao copiar logs.");
        }
    }
}

internal static class DebugConsoleWindow
{
    static DebugConsoleForm? _instance;

    public static void ShowOrActivate(IWin32Window? owner)
    {
        if (_instance is { IsDisposed: false })
        {
            if (_instance.WindowState == FormWindowState.Minimized)
                _instance.WindowState = FormWindowState.Normal;

            _instance.Show(owner);
            _instance.BringToFront();
            _instance.Activate();
            return;
        }

        _instance = new DebugConsoleForm();
        _instance.FormClosed += (_, _) => _instance = null;
        _instance.Show(owner);
    }
}
