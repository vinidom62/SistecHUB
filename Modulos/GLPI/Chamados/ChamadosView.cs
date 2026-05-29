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
    readonly TableLayoutPanel _entityRow;
    readonly FlowLayoutPanel _topStack;
    readonly FlowLayoutPanel _recentTicketsList;
    readonly Label _recentTicketsEmptyLabel;
    readonly Panel _paginationPanel;
    readonly Button _prevPageButton;
    readonly Button _nextPageButton;
    readonly Label _pageInfoLabel;
    readonly TableLayoutPanel _cardGrid;
    readonly Panel _belowCardsSection;
    readonly Panel _contentRoot;
    readonly System.Windows.Forms.Timer _cooldownTimer;

    const int TicketsPageSize = GlpiTicketPagination.DefaultPageSize;
    int _currentTicketsPage = 1;
    int _ticketsTotalCount;
    bool _loadingTicketsPage;

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

        _entityRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
        };
        _entityRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        // Coluna 0: largura pelo texto da entidade (evita largura 0 com duas Percent no FlowLayout).
        _entityRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _entityRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _entityRow.Controls.Add(_entityNameLabel, 0, 0);
        _entityRow.Controls.Add(rightFlow, 1, 0);

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

        _recentTicketsEmptyLabel = new Label
        {
            Text = "Nenhum chamado encontrado para esta entidade.",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 12, 0, 0),
            Visible = false,
        };

        _recentTicketsList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 12, 0, 8),
        };

        _prevPageButton = CreatePaginationButton("Anterior");
        _nextPageButton = CreatePaginationButton("Próxima");
        _pageInfoLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(12, 0, 12, 0),
        };

        _prevPageButton.Click += async (_, _) => await GoToTicketsPageAsync(_currentTicketsPage - 1).ConfigureAwait(true);
        _nextPageButton.Click += async (_, _) => await GoToTicketsPageAsync(_currentTicketsPage + 1).ConfigureAwait(true);

        var paginationRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0),
        };
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        paginationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paginationRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        paginationRow.Controls.Add(_prevPageButton, 0, 0);
        paginationRow.Controls.Add(_pageInfoLabel, 1, 0);
        paginationRow.Controls.Add(_nextPageButton, 2, 0);
        _pageInfoLabel.Anchor = AnchorStyles.None;
        _pageInfoLabel.Dock = DockStyle.Fill;
        _pageInfoLabel.TextAlign = ContentAlignment.MiddleCenter;

        _paginationPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.Transparent,
            Visible = false,
        };
        _paginationPanel.Controls.Add(paginationRow);

        _topStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent,
        };

        _topStack.Controls.Add(title);
        _topStack.Controls.Add(_entityRow);
        _topStack.Controls.Add(_cardGrid);
        _topStack.Controls.Add(_belowCardsSection);
        _topStack.Controls.Add(_recentTicketsEmptyLabel);

        var contentRoot = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        _contentRoot = contentRoot;
        contentRoot.Controls.Add(_recentTicketsList);
        contentRoot.Controls.Add(_paginationPanel);
        contentRoot.Controls.Add(_topStack);

        void SyncContentWidth(object? sender, EventArgs e) => SyncLayoutWidths();

        contentRoot.Resize += SyncContentWidth;
        _recentTicketsList.Resize += SyncContentWidth;
        Load += (_, _) =>
        {
            SyncContentWidth(null, EventArgs.Empty);
            _ = OnInitialLoadAsync();
        };

        Controls.Add(contentRoot);
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
                _currentTicketsPage = 1;
                ApplySnapshot(cached);
                UpdateRefreshButtonState();
                return;
            }

            await FetchAndCacheAsync(settings, entityId).ConfigureAwait(true);
            UpdateRefreshButtonState();
        }
        catch (Exception ex)
        {
            _entityNameLabel.Text = "GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex);
            _entityNameLabel.ForeColor = Color.FromArgb(220, 38, 38);
            SetValuesDash();
            ClearRecentTickets();
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
            _entityNameLabel.Text = "GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex);
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
        var (info, counts, ticketsPage) =
            await GlpiApiClient.GetEntityAndTicketCountsAsync(settings, entityId).ConfigureAwait(true);

        var leaf = info.LeafDisplayName;
        var name = string.IsNullOrEmpty(leaf) ? info.Name.Trim() : leaf;
        if (string.IsNullOrWhiteSpace(name))
            name = "Entidade #" + entityId;

        var snap = new ChamadosSnapshot
        {
            EntityId = entityId,
            EntityLeafName = name,
            Counts = counts,
            TicketsTotalCount = ticketsPage.TotalCount,
            RecentTickets = ticketsPage.Tickets,
            LoadedAtLocal = DateTime.Now,
        };
        ChamadosDataCache.SetSnapshot(snap);
        _currentTicketsPage = 1;
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

        _ticketsTotalCount = snap.TicketsTotalCount;
        RenderRecentTickets(snap.RecentTickets);
        UpdatePaginationControls();
    }

    async Task GoToTicketsPageAsync(int page)
    {
        if (_loadingTicketsPage)
            return;

        var totalPages = GetTicketsTotalPages();
        if (page < 1 || page > totalPages)
            return;

        if (!TryReadGlpiSettings(out var settings, out var entityId, out _))
            return;

        _loadingTicketsPage = true;
        SetPaginationEnabled(false);
        try
        {
            var ticketsPage = await GlpiApiClient
                .GetTicketsPageAsync(settings, entityId, page - 1, TicketsPageSize)
                .ConfigureAwait(true);

            _currentTicketsPage = page;
            _ticketsTotalCount = ticketsPage.TotalCount;
            RenderRecentTickets(ticketsPage.Tickets);
            UpdatePaginationControls();
            _recentTicketsList.AutoScrollOffset = Point.Empty;
        }
        catch (Exception ex)
        {
            _entityNameLabel.Text = "GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex);
            _entityNameLabel.ForeColor = Color.FromArgb(220, 38, 38);
        }
        finally
        {
            _loadingTicketsPage = false;
            UpdatePaginationControls();
        }
    }

    int GetTicketsTotalPages()
    {
        if (_ticketsTotalCount <= 0)
            return 1;
        return (int)Math.Ceiling(_ticketsTotalCount / (double)TicketsPageSize);
    }

    void UpdatePaginationControls()
    {
        var totalPages = GetTicketsTotalPages();
        var showPagination = _ticketsTotalCount > TicketsPageSize;
        _paginationPanel.Visible = showPagination;

        if (!showPagination)
            return;

        var from = (_currentTicketsPage - 1) * TicketsPageSize + 1;
        var to = Math.Min(_currentTicketsPage * TicketsPageSize, _ticketsTotalCount);
        _pageInfoLabel.Text = $"Página {_currentTicketsPage} de {totalPages}  ·  {from}–{to} de {_ticketsTotalCount}";

        var canNavigate = !_loadingTicketsPage;
        _prevPageButton.Enabled = canNavigate && _currentTicketsPage > 1;
        _nextPageButton.Enabled = canNavigate && _currentTicketsPage < totalPages;
    }

    void SetPaginationEnabled(bool enabled)
    {
        _prevPageButton.Enabled = enabled && _currentTicketsPage > 1;
        _nextPageButton.Enabled = enabled && _currentTicketsPage < GetTicketsTotalPages();
    }

    static Button CreatePaginationButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Padding = new Padding(14, 0, 14, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
        return btn;
    }

    void RenderRecentTickets(IReadOnlyList<GlpiTicketSummary> tickets)
    {
        _recentTicketsList.SuspendLayout();
        _recentTicketsList.Controls.Clear();

        if (tickets.Count == 0)
        {
            _recentTicketsEmptyLabel.Visible = true;
            _recentTicketsList.ResumeLayout(true);
            return;
        }

        _recentTicketsEmptyLabel.Visible = false;
        foreach (var ticket in tickets)
            _recentTicketsList.Controls.Add(CreateTicketRow(ticket));

        _recentTicketsList.ResumeLayout(true);
        SyncLayoutWidths();
    }

    void SyncLayoutWidths()
    {
        var inner = Math.Max(1, _contentRoot.ClientSize.Width);
        _entityRow.Width = inner;
        _cardGrid.Width = inner;
        _belowCardsSection.Width = inner;
        _paginationPanel.Width = inner;

        var listInner = Math.Max(1, _recentTicketsList.ClientSize.Width - _recentTicketsList.Padding.Horizontal);
        if (_recentTicketsList.VerticalScroll.Visible)
            listInner = Math.Max(1, listInner - SystemInformation.VerticalScrollBarWidth);

        foreach (Control child in _recentTicketsList.Controls)
            child.Width = listInner;
    }

    void ClearRecentTickets()
    {
        _recentTicketsList.Controls.Clear();
        _recentTicketsEmptyLabel.Visible = false;
        _ticketsTotalCount = 0;
        _currentTicketsPage = 1;
        _paginationPanel.Visible = false;
    }

    void ShowConfigError(string message)
    {
        _entityNameLabel.Text = message;
        _entityNameLabel.ForeColor = ShellTheme.TextMuted;
        _lastUpdateLabel.Text = "";
        SetValuesDash();
        ClearRecentTickets();
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

    static Panel CreateTicketRow(GlpiTicketSummary ticket)
    {
        const int rowHeight = 56;
        var row = new Panel
        {
            Height = rowHeight,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.White,
            Padding = new Padding(14, 0, 14, 0),
        };

        var idLabel = new Label
        {
            Text = "#" + ticket.Id,
            AutoSize = false,
            Width = 56,
            Dock = DockStyle.Left,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var titleLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(ticket.Title) ? "(sem título)" : ticket.Title.Trim(),
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var statusBadge = CreateStatusBadge(ticket.StatusLabel, ticket.Status);

        row.Controls.Add(titleLabel);
        row.Controls.Add(statusBadge);
        row.Controls.Add(idLabel);

        void RefreshChrome(object? sender, EventArgs e)
        {
            if (row.Width <= 0 || row.Height <= 0)
                return;
            ShellTheme.ApplyRoundedRegion(row, 8);
            row.Invalidate();
        }

        row.SizeChanged += RefreshChrome;
        RefreshChrome(null, EventArgs.Empty);

        row.Paint += (_, e) =>
        {
            var rect = row.ClientRectangle;
            rect.Inflate(-1, -1);
            if (rect.Width < 8 || rect.Height < 8)
                return;

            using var path = CreateRoundedRectPath(rect, 8);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawPath(pen, path);
        };

        return row;
    }

    static Control CreateStatusBadge(string label, int status)
    {
        var (bg, fg) = GetStatusBadgeColors(status);
        var badge = new Label
        {
            Text = label,
            AutoSize = true,
            MinimumSize = new Size(88, 26),
            Padding = new Padding(12, 5, 12, 5),
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0),
        };

        void RefreshBadgeShape(object? sender, EventArgs e)
        {
            if (badge.Width <= 0 || badge.Height <= 0)
                return;
            ShellTheme.ApplyRoundedRegion(badge, 6);
        }

        badge.SizeChanged += RefreshBadgeShape;
        RefreshBadgeShape(null, EventArgs.Empty);

        var host = new Panel
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 0, 0, 0),
        };
        host.Controls.Add(badge);
        return host;
    }

    static (Color Background, Color Foreground) GetStatusBadgeColors(int status) =>
        status switch
        {
            1 => (Color.FromArgb(22, 163, 74), Color.White),    // Novo — verde
            2 => (Color.FromArgb(37, 99, 235), Color.White),    // Em atendimento — azul
            3 => (Color.FromArgb(234, 88, 12), Color.White),    // Pausado — laranja
            4 => (Color.FromArgb(217, 119, 6), Color.White),    // Pendente — âmbar
            5 => (Color.FromArgb(124, 58, 237), Color.White),   // Resolvido — roxo
            6 => (Color.FromArgb(100, 116, 139), Color.White),  // Fechado — cinza
            _ => (Color.FromArgb(148, 163, 184), Color.White),
        };

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
