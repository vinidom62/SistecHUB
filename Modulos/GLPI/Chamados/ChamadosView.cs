using System.Drawing.Drawing2D;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Área do submódulo Chamados (GLPI).
/// </summary>
public sealed class ChamadosView : UserControl
{
    /// <summary>O utilizador pediu a vista de abertura de chamado (navegação no módulo GLPI).</summary>
    public event EventHandler? AberturaChamadoSolicitada;

    static readonly string[] StatusCardTitles =
    {
        "Chamados novos",
        "Chamados pendentes",
        "Em atendimento",
        "Chamados pausados",
        "Chamados fechados",
    };

    readonly Label _entityNameLabel;
    readonly Label _lastUpdateLabel;
    readonly Button _refreshButton;
    readonly Label[] _valueLabels;
    readonly FlowLayoutPanel _stack;
    readonly TableLayoutPanel _cardGrid;
    readonly Panel _belowCardsSection;
    readonly System.Windows.Forms.Timer _cooldownTimer;

    public ChamadosView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0, 8, 0, 0);

        _cooldownTimer = new System.Windows.Forms.Timer { Enabled = false };
        _cooldownTimer.Tick += OnCooldownTimerTick;
        Disposed += (_, _) =>
        {
            _cooldownTimer.Tick -= OnCooldownTimerTick;
            _cooldownTimer.Dispose();
        };

        var title = new Label
        {
            Text = "Chamados",
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };

        _entityNameLabel = new Label
        {
            Text = "A consultar o GLPI…",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 6, 12, 6),
            MaximumSize = new Size(520, 0),
        };

        _lastUpdateLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 10, 0),
            TextAlign = ContentAlignment.MiddleRight,
        };

        _refreshButton = new Button
        {
            Text = "\uE72C",
            Font = new Font("Segoe MDL2 Assets", 12F, FontStyle.Regular, GraphicsUnit.Point),
            Width = 36,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.FromArgb(241, 245, 249),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 0),
            Anchor = AnchorStyles.Right,
        };
        _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _refreshButton.FlatAppearance.BorderSize = 1;
        _refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
        _refreshButton.Click += async (_, _) => await OnRefreshClickedAsync().ConfigureAwait(true);

        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = false,
            MinimumSize = new Size(0, 36),
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 0),
        };
        rightFlow.Controls.Add(_refreshButton);
        rightFlow.Controls.Add(_lastUpdateLabel);

        var entityRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        entityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        // Coluna 0: largura pelo texto da entidade (evita largura 0 com duas Percent no FlowLayout).
        entityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        entityRow.Controls.Add(_entityNameLabel, 0, 0);
        entityRow.Controls.Add(rightFlow, 1, 0);

        _valueLabels = new Label[StatusCardTitles.Length];

        _cardGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = StatusCardTitles.Length,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0),
        };

        for (var i = 0; i < StatusCardTitles.Length; i++)
        {
            _cardGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / StatusCardTitles.Length));
            var (card, valueLbl) = CreateStatusCard(StatusCardTitles[i]);
            _valueLabels[i] = valueLbl;
            card.Margin = new Padding(0, 0, i < StatusCardTitles.Length - 1 ? 10 : 0, 0);
            card.Dock = DockStyle.Fill;
            _cardGrid.Controls.Add(card, i, 0);
        }

        _cardGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));

        const int belowCardsTopGap = 24;
        const int belowCardsDividerGap = 16;
        const int belowCardsRowHeight = 44;
        _belowCardsSection = new Panel
        {
            Dock = DockStyle.Top,
            Height = belowCardsTopGap + 1 + belowCardsDividerGap + belowCardsRowHeight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, belowCardsTopGap, 0, 0),
        };

        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(226, 232, 240),
        };

        var meusChamadosTitle = new Label
        {
            Text = "Meus chamados",
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 12, 0),
        };

        var openNewTicketButton = new Button
        {
            Text = "+ Abrir novo chamado",
            AutoSize = true,
            Height = 38,
            Padding = new Padding(16, 0, 16, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(22, 163, 74),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Right,
        };
        openNewTicketButton.FlatAppearance.BorderSize = 0;
        openNewTicketButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 128, 61);
        openNewTicketButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 83, 45);
        openNewTicketButton.Click += (_, _) => AberturaChamadoSolicitada?.Invoke(this, EventArgs.Empty);

        var meusChamadosRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = belowCardsRowHeight,
            Margin = new Padding(0, belowCardsDividerGap, 0, 0),
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        meusChamadosRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        meusChamadosRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        meusChamadosRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        meusChamadosRow.Controls.Add(meusChamadosTitle, 0, 0);
        meusChamadosRow.Controls.Add(openNewTicketButton, 1, 0);

        _belowCardsSection.Controls.Add(divider);
        _belowCardsSection.Controls.Add(meusChamadosRow);

        _stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };

        _stack.Controls.Add(title);
        _stack.Controls.Add(entityRow);
        _stack.Controls.Add(_cardGrid);
        _stack.Controls.Add(_belowCardsSection);

        void SyncCardGridWidth(object? sender, EventArgs e)
        {
            var inner = Math.Max(1, _stack.ClientSize.Width - _stack.Padding.Horizontal);
            entityRow.Width = inner;
            _cardGrid.Width = inner;
            _belowCardsSection.Width = inner;
        }

        _stack.Resize += SyncCardGridWidth;
        Load += (_, _) =>
        {
            SyncCardGridWidth(null, EventArgs.Empty);
            _ = OnInitialLoadAsync();
        };

        Controls.Add(_stack);
    }

    async Task OnInitialLoadAsync()
    {
        try
        {
            if (!TryReadGlpiSettings(out var settings, out var entityId, out var error))
            {
                ShowConfigError(error);
                return;
            }

            if (ChamadosDataCache.TryGetForEntity(entityId, out var cached) && cached != null)
            {
                ApplySnapshot(cached);
                UpdateRefreshButtonState();
                return;
            }

            await FetchAndCacheAsync(settings, entityId).ConfigureAwait(true);
            UpdateRefreshButtonState();
        }
        catch (Exception ex)
        {
            _entityNameLabel.Text = "GLPI: " + ex.Message;
            _entityNameLabel.ForeColor = Color.FromArgb(220, 38, 38);
            SetValuesDash();
        }
    }

    async Task OnRefreshClickedAsync()
    {
        if (!ChamadosDataCache.IsRefreshAllowed())
            return;

        if (!TryReadGlpiSettings(out var settings, out var entityId, out var error))
        {
            ShowConfigError(error);
            return;
        }

        _refreshButton.Enabled = false;
        try
        {
            await FetchAndCacheAsync(settings, entityId).ConfigureAwait(true);
            ChamadosDataCache.SetRefreshCooldownFromNow();
            ArmCooldownTimer(60_000);
        }
        catch (Exception ex)
        {
            _entityNameLabel.Text = "GLPI: " + ex.Message;
            _entityNameLabel.ForeColor = Color.FromArgb(220, 38, 38);
            _refreshButton.Enabled = ChamadosDataCache.IsRefreshAllowed();
        }
    }

    void OnCooldownTimerTick(object? sender, EventArgs e)
    {
        _cooldownTimer.Stop();
        _refreshButton.Enabled = ChamadosDataCache.IsRefreshAllowed();
    }

    void ArmCooldownTimer(int intervalMs)
    {
        _cooldownTimer.Stop();
        _cooldownTimer.Interval = Math.Max(1, intervalMs);
        _cooldownTimer.Start();
    }

    void UpdateRefreshButtonState()
    {
        if (ChamadosDataCache.IsRefreshAllowed())
        {
            _refreshButton.Enabled = true;
            return;
        }

        _refreshButton.Enabled = false;
        var remain = (int)(ChamadosDataCache.GetNextRefreshAllowedUtc() - DateTime.UtcNow).TotalMilliseconds;
        if (remain <= 0)
        {
            _refreshButton.Enabled = true;
            return;
        }

        ArmCooldownTimer(remain);
    }

    async Task FetchAndCacheAsync(AppUserSettings settings, int entityId)
    {
        var (info, counts) =
            await GlpiApiClient.GetEntityAndTicketCountsAsync(settings, entityId).ConfigureAwait(true);

        var leaf = LeafEntityDisplayName(info);
        var name = string.IsNullOrEmpty(leaf) ? info.Name.Trim() : leaf;
        if (string.IsNullOrWhiteSpace(name))
            name = "Entidade #" + entityId;

        var snap = new ChamadosSnapshot
        {
            EntityId = entityId,
            EntityLeafName = name,
            Counts = counts,
            LoadedAtLocal = DateTime.Now,
        };
        ChamadosDataCache.SetSnapshot(snap);
        ApplySnapshot(snap);
    }

    void ApplySnapshot(ChamadosSnapshot snap)
    {
        var displayName = string.IsNullOrWhiteSpace(snap.EntityLeafName)
            ? "Entidade #" + snap.EntityId
            : snap.EntityLeafName.Trim();
        _entityNameLabel.Text = displayName;
        _entityNameLabel.ForeColor = ShellTheme.TextPrimary;
        _lastUpdateLabel.Text = "Última atualização: " + snap.LoadedAtLocal.ToString("dd/MM/yyyy HH:mm");

        _valueLabels[0].Text = snap.Counts.Novos.ToString();
        _valueLabels[1].Text = snap.Counts.Pendentes.ToString();
        _valueLabels[2].Text = snap.Counts.Atribuidos.ToString();
        _valueLabels[3].Text = snap.Counts.EmAtendimentoPlanejado.ToString();
        _valueLabels[4].Text = snap.Counts.Fechados.ToString();
    }

    void ShowConfigError(string message)
    {
        _entityNameLabel.Text = message;
        _entityNameLabel.ForeColor = ShellTheme.TextMuted;
        _lastUpdateLabel.Text = "";
        SetValuesDash();
        _refreshButton.Enabled = false;
    }

    static bool TryReadGlpiSettings(out AppUserSettings settings, out int entityId, out string error)
    {
        settings = AppSettingsStore.Load();
        entityId = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
        {
            error =
                "Configure o User token do GLPI em Configurações para identificar a entidade.";
            return false;
        }

        if (!int.TryParse((settings.EntityId ?? "").Trim(), out entityId))
        {
            error =
                "Indique um Id da entidade numérico (client id) em Configurações para consultar o GLPI.";
            return false;
        }

        return true;
    }

    void SetValuesDash()
    {
        foreach (var l in _valueLabels)
            l.Text = "—";
    }

    /// <summary>Último segmento do caminho da entidade (ex.: ignora "Sistec Sistemas > ").</summary>
    static string LeafEntityDisplayName(GlpiEntityInfo info)
    {
        var full = info.DisplayName.Trim();
        if (full.Length == 0)
            return info.Name.Trim();

        var idx = full.LastIndexOf('>');
        if (idx < 0 || idx >= full.Length - 1)
            return full;

        return full[(idx + 1)..].Trim();
    }

    static (Panel Card, Label ValueLabel) CreateStatusCard(string cardTitle)
    {
        var card = new Panel
        {
            MinimumSize = new Size(100, 108),
            BackColor = Color.White,
            Padding = new Padding(14, 12, 14, 12),
        };

        var titleLbl = new Label
        {
            Text = cardTitle,
            AutoSize = true,
            MaximumSize = new Size(200, 0),
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopLeft,
        };

        var valueLbl = new Label
        {
            Text = "…",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 26F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),
        };

        card.Controls.Add(titleLbl);
        card.Controls.Add(valueLbl);

        void RefreshChrome(object? sender, EventArgs e)
        {
            if (card.Width <= 0 || card.Height <= 0)
                return;
            ShellTheme.ApplyRoundedRegion(card, 10);
            card.Invalidate();
        }

        card.SizeChanged += RefreshChrome;
        RefreshChrome(null, EventArgs.Empty);

        card.Paint += (_, e) =>
        {
            var rect = card.ClientRectangle;
            rect.Inflate(-1, -1);
            if (rect.Width < 8 || rect.Height < 8)
                return;

            using var path = CreateRoundedRectPath(rect, 10);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawPath(pen, path);
        };

        return (card, valueLbl);
    }

    static GraphicsPath CreateRoundedRectPath(Rectangle r, int radius)
    {
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
