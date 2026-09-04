using System.Drawing.Drawing2D;
using SistecHub.Core;
using SistecHub.UI;

namespace SistecHub.Modulos.Inventario;

public sealed class InventarioView : UserControl
{
    const int CompactCardHeight = 168;
    const int DiskCardWidth = 236;
    const int DiskCardHeight = 214;

    readonly Label _cpuValue;
    readonly Label _cpuSecondary;
    readonly Label _ramValue;
    readonly Label _ramSecondary;
    readonly Label _gpuValue;
    readonly Label _gpuSecondary;
    readonly Label _motherboardValue;
    readonly Label _motherboardSecondary;
    readonly FlowLayoutPanel _disksFlow;
    readonly Button _refreshButton;
    readonly Label _lastUpdateLabel;
    readonly ToolTip _toolTip = new();

    readonly Label _osNameLabel;
    readonly Label _osVersionLabel;
    readonly Label _osStatusBadge;
    readonly Label _osKeyLabel;
    readonly Button _revealKeyButton;
    readonly Button _copyKeyButton;
    string? _currentOsKey;
    bool _isKeyRevealed;

    public InventarioView()
    {
        BackColor = ShellTheme.MainBg;

        _cpuValue = CreateValueLabel();
        _cpuSecondary = CreateSecondaryLabel();
        _ramValue = CreateValueLabel();
        _ramSecondary = CreateSecondaryLabel();
        _gpuValue = CreateValueLabel();
        _gpuSecondary = CreateSecondaryLabel();
        _motherboardValue = CreateValueLabel();
        _motherboardSecondary = CreateSecondaryLabel();
        _disksFlow = CreateDisksFlow();

        _osNameLabel = new Label();
        _osVersionLabel = new Label();
        _osStatusBadge = new Label();
        _osKeyLabel = new Label();
        _revealKeyButton = new Button();
        _copyKeyButton = new Button();

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
            Margin = new Padding(0),
            Anchor = AnchorStyles.Right,
        };
        _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _refreshButton.FlatAppearance.BorderSize = 1;
        _refreshButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
        _refreshButton.Click += async (_, _) => await OnRefreshClickedAsync().ConfigureAwait(true);
        _toolTip.SetToolTip(_refreshButton, "Atualizar inventário");

        _lastUpdateLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 10, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Right,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(32, 20, 32, 24),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactCardHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildOsBanner(), 0, 1);

        var cardsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            MinimumSize = new Size(0, CompactCardHeight),
        };
        for (var i = 0; i < 4; i++)
            cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        cardsRow.RowStyles.Add(new RowStyle(SizeType.Absolute, CompactCardHeight));

        cardsRow.Controls.Add(CreateMetricCard("\uE950", "Processador", _cpuValue, _cpuSecondary), 0, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE9F5", "Memória RAM", _ramValue, _ramSecondary), 1, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE714", "Placa de vídeo", _gpuValue, _gpuSecondary), 2, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE770", "Placa mãe", _motherboardValue, _motherboardSecondary), 3, 0);

        root.Controls.Add(cardsRow, 0, 2);
        root.Controls.Add(BuildDisksSection(_disksFlow), 0, 3);

        Controls.Add(root);
        InventarioSnapshotCoordinator.SnapshotUpdated += OnInventarioSnapshotUpdated;
        HandleDestroyed += OnInventarioHandleDestroyed;
        Load += OnInventarioLoad;
    }

    Control BuildHeader()
    {
        var headerTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
            Padding = Padding.Empty,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var leftStack = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };

        var title = new Label
        {
            Text = "Inventário",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
        };

        var accentBar = new Panel
        {
            Width = 56,
            Height = 4,
            BackColor = ShellTheme.Accent,
            Margin = Padding.Empty,
        };

        leftStack.Controls.Add(title);
        leftStack.Controls.Add(accentBar);

        var rightStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 10, 0),
        };
        rightStack.Controls.Add(_refreshButton);
        rightStack.Controls.Add(_lastUpdateLabel);

        headerTable.Controls.Add(leftStack, 0, 0);
        headerTable.Controls.Add(rightStack, 1, 0);

        return headerTable;
    }

    /// <summary>Cartão: título, ícone, valor principal e linha extra (temperatura, uso, série).</summary>
    static ElevatedCardPanel CreateMetricCard(string mdl2Glyph, string title, Label valueLabel, Label secondaryLabel)
    {
        var card = new ElevatedCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(10, 10, 12, 10),
            Height = CompactCardHeight,
            MinimumSize = new Size(48, CompactCardHeight),
            MaximumSize = new Size(4096, CompactCardHeight),
            AutoSize = false,
        };

        var titleLbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        var iconLbl = new Label
        {
            Text = mdl2Glyph,
            Font = new Font("Segoe MDL2 Assets", 24F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
            BackColor = Color.Transparent,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        valueLabel.Text = "A carregar…";
        valueLabel.AutoSize = false;
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
        valueLabel.TextAlign = ContentAlignment.MiddleCenter;
        valueLabel.AutoEllipsis = true;
        valueLabel.UseCompatibleTextRendering = false;

        secondaryLabel.Text = "A carregar…";
        secondaryLabel.AutoSize = false;
        secondaryLabel.Dock = DockStyle.Fill;
        secondaryLabel.TextAlign = ContentAlignment.MiddleCenter;
        secondaryLabel.AutoEllipsis = true;

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        inner.Controls.Add(titleLbl, 0, 0);
        inner.Controls.Add(iconLbl, 0, 1);
        inner.Controls.Add(valueLabel, 0, 2);
        inner.Controls.Add(secondaryLabel, 0, 3);

        card.Controls.Add(inner);
        return card;
    }

    static Label CreateValueLabel() =>
        new()
        {
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            UseMnemonic = false,
        };

    static Label CreateSecondaryLabel() =>
        new()
        {
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            UseMnemonic = false,
        };

    static FlowLayoutPanel CreateDisksFlow()
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 8, 4),
            Height = DiskCardHeight + 8,
        };
        flow.Controls.Add(CreateDiskPlaceholder("A carregar…"));
        return flow;
    }

    static Control BuildDisksSection(FlowLayoutPanel disksFlow)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 20, 0, 0),
            BackColor = Color.Transparent,
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 0, 10),
            Margin = Padding.Empty,
        };

        var title = new Label
        {
            Text = "Discos",
            AutoSize = true,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 4, 0),
            UseMnemonic = false,
        };

        var infoIcon = new Label
        {
            Text = "\uE946",
            AutoSize = true,
            Font = new Font("Segoe MDL2 Assets", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Cursor = Cursors.Help,
            Margin = new Padding(0, 3, 0, 0),
            UseMnemonic = false,
        };

        header.Controls.Add(title);
        header.Controls.Add(infoIcon);

        var scroller = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };
        disksFlow.Location = Point.Empty;
        scroller.Controls.Add(disksFlow);

        void ScrollHorizontally(object? _, MouseEventArgs e)
        {
            if (!scroller.HorizontalScroll.Visible)
                return;
            var next = scroller.HorizontalScroll.Value - e.Delta;
            next = Math.Clamp(next, scroller.HorizontalScroll.Minimum, scroller.HorizontalScroll.Maximum);
            scroller.AutoScrollPosition = new Point(next, 0);
        }

        scroller.MouseWheel += ScrollHorizontally;
        disksFlow.MouseWheel += ScrollHorizontally;

        var hint = new DiskHealthHintPopup();
        WireDiskHealthHint(host, infoIcon, hint);

        host.Controls.Add(scroller);
        host.Controls.Add(header);
        host.Controls.Add(hint);
        hint.BringToFront();
        return host;
    }

    static void WireDiskHealthHint(Control host, Label infoIcon, DiskHealthHintPopup hint)
    {
        var hideTimer = new System.Windows.Forms.Timer { Interval = 220 };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            if (!infoIcon.ClientRectangle.Contains(infoIcon.PointToClient(Cursor.Position))
                && !hint.ClientRectangle.Contains(hint.PointToClient(Cursor.Position)))
            {
                hint.Visible = false;
                infoIcon.ForeColor = ShellTheme.TextMuted;
            }
        };

        void ShowHint()
        {
            hideTimer.Stop();
            infoIcon.ForeColor = ShellTheme.Accent;
            var below = host.PointToClient(infoIcon.PointToScreen(new Point(0, infoIcon.Height + 6)));
            var x = Math.Clamp(below.X - 8, 0, Math.Max(0, host.Width - hint.Width));
            var y = Math.Max(0, below.Y);
            hint.Location = new Point(x, y);
            hint.Visible = true;
            hint.BringToFront();
        }

        void ScheduleHide()
        {
            hideTimer.Stop();
            hideTimer.Start();
        }

        infoIcon.MouseEnter += (_, _) => ShowHint();
        infoIcon.MouseLeave += (_, _) => ScheduleHide();
        hint.MouseEnter += (_, _) => hideTimer.Stop();
        hint.MouseLeave += (_, _) => ScheduleHide();
        host.Disposed += (_, _) => hideTimer.Dispose();
    }

    static ElevatedCardPanel CreateDiskPlaceholder(string message)
    {
        var value = CreateValueLabel();
        var secondary = CreateSecondaryLabel();
        var card = CreateMetricCard("\uEDA2", "Disco", value, secondary);
        card.Dock = DockStyle.None;
        card.Width = DiskCardWidth;
        card.Height = DiskCardHeight;
        card.MinimumSize = new Size(DiskCardWidth, DiskCardHeight);
        card.MaximumSize = new Size(DiskCardWidth, DiskCardHeight);
        card.Margin = new Padding(0, 0, 10, 0);
        value.Text = message;
        secondary.Text = " ";
        return card;
    }

    static Control CreateDiskCard(DiscoRigidoInventario disk)
    {
        var card = new ElevatedCardPanel
        {
            Width = DiskCardWidth,
            Height = DiskCardHeight,
            MinimumSize = new Size(DiskCardWidth, DiskCardHeight),
            MaximumSize = new Size(DiskCardWidth, DiskCardHeight),
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(12, 10, 12, 10),
            AutoSize = false,
        };

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent,
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var tipoLbl = new Label
        {
            Text = string.IsNullOrWhiteSpace(disk.Tipo) ? "Disco" : disk.Tipo,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Margin = Padding.Empty,
        };
        var iconLbl = new Label
        {
            Text = "\uEDA2",
            Font = new Font("Segoe MDL2 Assets", 16F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.Accent,
            BackColor = Color.Transparent,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = Padding.Empty,
        };
        header.Controls.Add(tipoLbl, 0, 0);
        header.Controls.Add(iconLbl, 1, 0);

        var nameLbl = new Label
        {
            Text = string.IsNullOrWhiteSpace(disk.Nome) ? "Disco" : disk.Nome,
            Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        var healthLbl = new Label
        {
            Text = FormatDiskHealth(disk.Saude),
            Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = DiskHealthColor(disk.Saude),
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        var lifeLbl = new Label
        {
            Text = disk.VidaPercent is { } pct ? $"Vida: {pct:0.#}%" : "Vida: —",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
        };

        var spaceLbl = new Label
        {
            Text = FormatDiskSpace(disk.ArmazenamentoUsadoGb, disk.ArmazenamentoTotalGb),
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        var serialLbl = new Label
        {
            Text = string.IsNullOrWhiteSpace(disk.NumeroSerie) ? "S/N: —" : $"S/N: {disk.NumeroSerie}",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextMuted,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        inner.Controls.Add(header, 0, 0);
        inner.Controls.Add(nameLbl, 0, 1);
        inner.Controls.Add(healthLbl, 0, 2);
        inner.Controls.Add(lifeLbl, 0, 3);
        inner.Controls.Add(spaceLbl, 0, 4);
        inner.Controls.Add(serialLbl, 0, 5);
        card.Controls.Add(inner);
        return card;
    }

    static string FormatDiskHealth(string saude) =>
        saude.Trim().ToLowerInvariant() switch
        {
            "saudavel" => "Sem alertas",
            "atencao" => "Atenção",
            "falha" => "Falha",
            _ => "Desconhecida",
        };

    static Color DiskHealthColor(string saude) =>
        saude.Trim().ToLowerInvariant() switch
        {
            "saudavel" => Color.FromArgb(22, 163, 74),
            "atencao" => Color.FromArgb(217, 119, 6),
            "falha" => Color.FromArgb(220, 38, 38),
            _ => ShellTheme.TextMuted,
        };

    static string FormatDiskSpace(float? usedGb, float? totalGb)
    {
        if (usedGb is null && totalGb is null)
            return "Espaço: —";
        if (totalGb is null)
            return $"Usado: {usedGb:0.#} GB";
        if (usedGb is null)
            return $"Total: {totalGb:0.#} GB";
        return $"{usedGb:0.#} / {totalGb:0.#} GB";
    }

    void RebuildDiskCards(IReadOnlyList<DiscoRigidoInventario>? disks)
    {
        _disksFlow.SuspendLayout();
        var old = _disksFlow.Controls.Cast<Control>().ToArray();
        _disksFlow.Controls.Clear();
        foreach (var c in old)
            c.Dispose();

        if (disks is null || disks.Count == 0)
        {
            _disksFlow.Controls.Add(
                CreateDiskPlaceholder(disks is null ? "—" : "Nenhum disco detectado"));
        }
        else
        {
            foreach (var disk in disks)
                _disksFlow.Controls.Add(CreateDiskCard(disk));
        }

        _disksFlow.ResumeLayout(true);
    }

    void OnInventarioHandleDestroyed(object? sender, EventArgs e)
    {
        HandleDestroyed -= OnInventarioHandleDestroyed;
        InventarioSnapshotCoordinator.SnapshotUpdated -= OnInventarioSnapshotUpdated;
        _toolTip.Dispose();
    }

    void OnInventarioSnapshotUpdated(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            if (IsDisposed)
                return;
            if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
                ApplySnapshotToUi(snap);
            else
            {
                ApplyInventoryLoadErrorUi(
                    _cpuValue,
                    _ramValue,
                    _gpuValue,
                    _motherboardValue,
                    _cpuSecondary,
                    _ramSecondary,
                    _gpuSecondary,
                    _motherboardSecondary);
                RebuildDiskCards(null);
                UpdateLastCollectedLabel();
                _osNameLabel.Text = "Windows";
                _osVersionLabel.Text = "";
                _osStatusBadge.Text = "—";
                _osStatusBadge.BackColor = Color.FromArgb(241, 245, 249);
                _osStatusBadge.ForeColor = ShellTheme.TextMuted;
                _currentOsKey = null;
                _isKeyRevealed = false;
                UpdateKeyDisplay();
            }
        });
    }

    void OnInventarioLoad(object? sender, EventArgs e)
    {
        Load -= OnInventarioLoad;
        if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
            ApplySnapshotToUi(snap);
        else
            UpdateLastCollectedLabel();
    }

    Control BuildOsBanner()
    {
        var panel = new ElevatedCardPanel
        {
            Dock = DockStyle.Fill,
            Height = 48,
            MinimumSize = new Size(0, 48),
            Margin = new Padding(0, 0, 10, 14),
            Padding = new Padding(14, 0, 14, 0),
            BackColor = Color.White,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        var winIcon = new WindowsLogoControl();

        _osNameLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        _osNameLabel.ForeColor = ShellTheme.TextPrimary;
        _osNameLabel.AutoSize = true;
        _osNameLabel.Margin = new Padding(0, 14, 8, 0);

        _osVersionLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _osVersionLabel.ForeColor = ShellTheme.TextMuted;
        _osVersionLabel.AutoSize = true;
        _osVersionLabel.Margin = new Padding(0, 15, 0, 0);

        left.Controls.Add(winIcon);
        left.Controls.Add(_osNameLabel);
        left.Controls.Add(_osVersionLabel);

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        _copyKeyButton.Text = "\uE8C8";
        _copyKeyButton.Font = new Font("Segoe MDL2 Assets", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _copyKeyButton.Size = new Size(28, 26);
        _copyKeyButton.FlatStyle = FlatStyle.Flat;
        _copyKeyButton.FlatAppearance.BorderSize = 0;
        _copyKeyButton.BackColor = Color.FromArgb(241, 245, 249);
        _copyKeyButton.ForeColor = ShellTheme.TextPrimary;
        _copyKeyButton.Cursor = Cursors.Hand;
        _copyKeyButton.Margin = new Padding(0, 11, 0, 0);
        _toolTip.SetToolTip(_copyKeyButton, "Copiar chave de ativação");
        _copyKeyButton.Click += OnCopyKeyClicked;
        _copyKeyButton.Visible = false;

        _revealKeyButton.Text = "\uE72E";
        _revealKeyButton.Font = new Font("Segoe MDL2 Assets", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _revealKeyButton.Size = new Size(28, 26);
        _revealKeyButton.FlatStyle = FlatStyle.Flat;
        _revealKeyButton.FlatAppearance.BorderSize = 0;
        _revealKeyButton.BackColor = Color.FromArgb(241, 245, 249);
        _revealKeyButton.ForeColor = ShellTheme.TextPrimary;
        _revealKeyButton.Cursor = Cursors.Hand;
        _revealKeyButton.Margin = new Padding(0, 11, 4, 0);
        _toolTip.SetToolTip(_revealKeyButton, "Clique para exibir a chave (requer senha de administrador)");
        _revealKeyButton.Click += (_, _) => OnKeyRevealOrHideClicked();
        _revealKeyButton.Visible = false;

        _osKeyLabel.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _osKeyLabel.ForeColor = ShellTheme.TextPrimary;
        _osKeyLabel.AutoSize = true;
        _osKeyLabel.Margin = new Padding(0, 15, 6, 0);
        _osKeyLabel.Cursor = Cursors.Hand;
        _osKeyLabel.Click += (_, _) => OnKeyRevealOrHideClicked();
        _osKeyLabel.Visible = false;

        _osStatusBadge.AutoSize = true;
        _osStatusBadge.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        _osStatusBadge.Padding = new Padding(8, 4, 8, 4);
        _osStatusBadge.Margin = new Padding(0, 11, 12, 0);

        right.Controls.Add(_copyKeyButton);
        right.Controls.Add(_revealKeyButton);
        right.Controls.Add(_osKeyLabel);
        right.Controls.Add(_osStatusBadge);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        panel.Controls.Add(layout);

        return panel;
    }

    void OnKeyRevealOrHideClicked()
    {
        if (string.IsNullOrWhiteSpace(_currentOsKey))
            return;

        if (_isKeyRevealed)
        {
            _isKeyRevealed = false;
            UpdateKeyDisplay();
            return;
        }

        using var prompt = new SettingsPasswordForm(
            "Exibir Chave de Ativação",
            "Senha de administrador",
            "Digite a senha de administrador para visualizar a chave do Windows.");

        if (prompt.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _isKeyRevealed = true;
            UpdateKeyDisplay();
        }
    }

    void OnCopyKeyClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentOsKey))
            return;

        if (!_isKeyRevealed)
        {
            using var prompt = new SettingsPasswordForm(
                "Copiar Chave de Ativação",
                "Senha de administrador",
                "Digite a senha de administrador para copiar a chave do Windows.");

            if (prompt.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _isKeyRevealed = true;
            UpdateKeyDisplay();
        }

        try
        {
            Clipboard.SetText(_currentOsKey);
            _toolTip.SetToolTip(_copyKeyButton, "Chave copiada!");
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => _toolTip.SetToolTip(_copyKeyButton, "Copiar chave de ativação"));
            });
        }
        catch
        {
            // falha de acesso à área de transferência
        }
    }

    void UpdateKeyDisplay()
    {
        if (string.IsNullOrWhiteSpace(_currentOsKey))
        {
            _osKeyLabel.Visible = false;
            _revealKeyButton.Visible = false;
            _copyKeyButton.Visible = false;
            return;
        }

        _osKeyLabel.Visible = true;
        _revealKeyButton.Visible = true;
        _copyKeyButton.Visible = true;

        if (_isKeyRevealed)
        {
            _osKeyLabel.Text = $"Chave: {_currentOsKey}";
            _revealKeyButton.Text = "\uE785"; // cadeado aberto
            _revealKeyButton.ForeColor = Color.FromArgb(220, 38, 38);
            _toolTip.SetToolTip(_revealKeyButton, "Ocultar chave de ativação");
            _toolTip.SetToolTip(_osKeyLabel, "Chave de ativação do Windows (clique para ocultar)");
        }
        else
        {
            _osKeyLabel.Text = "Chave: *****-*****-*****-*****-*****";
            _revealKeyButton.Text = "\uE72E"; // cadeado fechado
            _revealKeyButton.ForeColor = ShellTheme.TextPrimary;
            _toolTip.SetToolTip(_revealKeyButton, "Exibir chave (requer senha de administrador)");
            _toolTip.SetToolTip(_osKeyLabel, "Clique para exibir a chave (requer senha de administrador)");
        }
    }

    void ApplySnapshotToUi(in InventarioHardwareSnapshot snapshot)
    {
        _cpuValue.Text = snapshot.Cpu;
        _ramValue.Text = snapshot.Ram;
        _gpuValue.Text = snapshot.Gpu;
        _motherboardValue.Text = snapshot.Motherboard;
        _cpuSecondary.Text = snapshot.CpuTemperatureLine;
        if (!string.IsNullOrWhiteSpace(snapshot.ProcessadorInfo.SensorTemperatura))
            _toolTip.SetToolTip(_cpuSecondary, $"Sensor utilizado: {snapshot.ProcessadorInfo.SensorTemperatura}");

        _ramSecondary.Text = snapshot.RamUsageLine;
        _gpuSecondary.Text = snapshot.GpuTemperatureLine;
        var gpuSensors = string.Join(", ", snapshot.PlacasVideo
            .Where(g => !string.IsNullOrWhiteSpace(g.SensorTemperatura))
            .Select(g => $"{g.Nome}: {g.SensorTemperatura}"));
        if (!string.IsNullOrWhiteSpace(gpuSensors))
            _toolTip.SetToolTip(_gpuSecondary, $"Sensor utilizado: {gpuSensors}");

        _motherboardSecondary.Text = snapshot.MotherboardSerialLine;
        RebuildDiskCards(snapshot.DiscosRigidos);
        UpdateLastCollectedLabel();

        var so = snapshot.SistemaOperacional;
        _osNameLabel.Text = string.IsNullOrWhiteSpace(so.NomeProduto) || so.NomeProduto == "—"
            ? "Windows"
            : $"{so.NomeProduto} ({so.Arquitetura})";

        _osVersionLabel.Text = string.IsNullOrWhiteSpace(so.VersaoAtual) || so.VersaoAtual == "—"
            ? ""
            : $"•  {so.VersaoAtual}";

        var isAtivado = string.Equals(so.StatusAtivacao, "Ativado", StringComparison.OrdinalIgnoreCase);
        if (isAtivado)
        {
            var channelInfo = !string.IsNullOrWhiteSpace(so.CanalLicenca) ? $" ({so.CanalLicenca})" : "";
            _osStatusBadge.Text = $"✔ Ativado{channelInfo}";
            _osStatusBadge.BackColor = Color.FromArgb(220, 252, 231);
            _osStatusBadge.ForeColor = Color.FromArgb(22, 101, 52);
        }
        else
        {
            _osStatusBadge.Text = $"✖ {so.StatusAtivacao}";
            _osStatusBadge.BackColor = Color.FromArgb(254, 226, 226);
            _osStatusBadge.ForeColor = Color.FromArgb(153, 27, 27);
        }
        ShellTheme.ApplyRoundedRegion(_osStatusBadge, 6);

        _currentOsKey = so.ChaveAtivacao;
        UpdateKeyDisplay();
    }

    void UpdateLastCollectedLabel()
    {
        if (InventarioSnapshotCoordinator.LastCollectedAt is { } stamp)
        {
            var local = stamp.ToLocalTime();
            _lastUpdateLabel.Text = $"Última atualização: {local:HH:mm:ss}";
        }
        else
        {
            _lastUpdateLabel.Text = "";
        }
    }

    async Task OnRefreshClickedAsync()
    {
        if (!_refreshButton.Enabled)
            return;

        _refreshButton.Enabled = false;
        _lastUpdateLabel.Text = "A atualizar inventário…";
        _toolTip.SetToolTip(_refreshButton, "A atualizar…");

        try
        {
            var previousStamp = InventarioSnapshotCoordinator.LastCollectedAt;
            InventarioSnapshotCoordinator.RequestRefreshNow();

            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(400).ConfigureAwait(true);
                if (IsDisposed)
                    return;

                if (InventarioSnapshotCoordinator.PollFromService()
                    || InventarioSnapshotCoordinator.LastCollectedAt != previousStamp)
                {
                    break;
                }
            }

            if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
                ApplySnapshotToUi(snap);
        }
        catch (Exception ex)
        {
            AppDebugLog.LogException("Inventario", ex, "Falha ao atualizar inventário");
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
            {
                UpdateLastCollectedLabel();
                await Task.Delay(3000).ConfigureAwait(true);
                if (!IsDisposed)
                {
                    _refreshButton.Enabled = true;
                    _toolTip.SetToolTip(_refreshButton, "Atualizar inventário");
                }
            }
        }
    }

    static void ApplyInventoryLoadErrorUi(
        Label cpuValue,
        Label ramValue,
        Label gpuValue,
        Label mbValue,
        Label cpuSecondary,
        Label ramSecondary,
        Label gpuSecondary,
        Label mbSecondary)
    {
        const string err = "—";
        if (cpuValue.Text == "A carregar…") cpuValue.Text = err;
        if (ramValue.Text == "A carregar…") ramValue.Text = err;
        if (gpuValue.Text == "A carregar…") gpuValue.Text = err;
        if (mbValue.Text == "A carregar…") mbValue.Text = err;
        if (cpuSecondary.Text == "A carregar…") cpuSecondary.Text = "Temperatura: —";
        if (ramSecondary.Text == "A carregar…") ramSecondary.Text = "Uso: —";
        if (gpuSecondary.Text == "A carregar…") gpuSecondary.Text = "Temperatura: —";
        if (mbSecondary.Text == "A carregar…") mbSecondary.Text = "N.º de série: —";
    }

    sealed class DiskHealthHintPopup : Panel
    {
        public DiskHealthHintPopup()
        {
            Visible = false;
            Width = 312;
            AutoSize = false;
            BackColor = Color.White;
            Padding = new Padding(16, 12, 14, 14);

            var heading = new Label
            {
                Text = "Sobre estes valores",
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = ShellTheme.TextPrimary,
                BackColor = Color.Transparent,
                Location = new Point(16, 12),
                UseMnemonic = false,
            };
            var body = new Label
            {
                Text = "A saúde e a vida útil vêm de auto testes SMART e do firmware do fabricante. Podem variar entre equipamentos e programas, e não substituem um diagnóstico técnico.",
                AutoSize = true,
                MaximumSize = new Size(276, 0),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = ShellTheme.TextMuted,
                BackColor = Color.Transparent,
                Location = new Point(16, 34),
                UseMnemonic = false,
            };

            Controls.Add(heading);
            Controls.Add(body);
            Height = 34 + TextRenderer.MeasureText(
                body.Text,
                body.Font,
                new Size(276, 0),
                TextFormatFlags.WordBreak).Height + 20;

            SizeChanged += (_, _) => ShellTheme.ApplyRoundedRegion(this, 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = ClientRectangle;
            if (bounds.Width <= 2 || bounds.Height <= 2)
                return;

            bounds.Width -= 1;
            bounds.Height -= 1;
            using var path = ShellTheme.CreateRoundedRectanglePath(bounds.Width, bounds.Height, 10);
            using var border = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawPath(border, path);
            using var accent = new SolidBrush(ShellTheme.Accent);
            e.Graphics.FillRectangle(accent, 0, 10, 3, Math.Max(8, bounds.Height - 20));
        }
    }

    sealed class WindowsLogoControl : Panel
    {
        public WindowsLogoControl()
        {
            Size = new Size(16, 16);
            MinimumSize = new Size(16, 16);
            Margin = new Padding(0, 15, 10, 0);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(Color.FromArgb(0, 120, 215)); // Windows Blue #0078D7
            // 4 quadrados simétricos com espaçamento de 2px
            e.Graphics.FillRectangle(brush, 0, 0, 7, 7);
            e.Graphics.FillRectangle(brush, 9, 0, 7, 7);
            e.Graphics.FillRectangle(brush, 0, 9, 7, 7);
            e.Graphics.FillRectangle(brush, 9, 9, 7, 7);
        }
    }
}
