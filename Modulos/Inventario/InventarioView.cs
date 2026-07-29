using SistecHub.UI;

namespace SistecHub.Modulos.Inventario;

public sealed class InventarioView : UserControl
{
    const int CompactCardHeight = 168;

    readonly Label _cpuValue;
    readonly Label _cpuSecondary;
    readonly Label _ramValue;
    readonly Label _ramSecondary;
    readonly Label _gpuValue;
    readonly Label _gpuSecondary;
    readonly Label _motherboardValue;
    readonly Label _motherboardSecondary;

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

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(32, 24, 32, 28),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);

        var cardsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 20, 0, 0),
        };
        for (var i = 0; i < 4; i++)
            cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        cardsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        cardsRow.Controls.Add(CreateMetricCard("\uE950", "Processador", _cpuValue, _cpuSecondary), 0, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE9F5", "Memória RAM", _ramValue, _ramSecondary), 1, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE714", "Placa de vídeo", _gpuValue, _gpuSecondary), 2, 0);
        cardsRow.Controls.Add(CreateMetricCard("\uE770", "Placa mãe", _motherboardValue, _motherboardSecondary), 3, 0);

        root.Controls.Add(cardsRow, 0, 1);

        Controls.Add(root);
        InventarioSnapshotCoordinator.SnapshotUpdated += OnInventarioSnapshotUpdated;
        HandleDestroyed += OnInventarioHandleDestroyed;
        Load += OnInventarioLoad;
    }

    static FlowLayoutPanel BuildHeader()
    {
        var top = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
        };

        var accentRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };
        accentRow.Controls.Add(new Panel
        {
            Width = 56,
            Height = 4,
            BackColor = ShellTheme.Accent,
        });

        var title = new Label
        {
            Text = "Inventário",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = ShellTheme.TextPrimary,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
        };

        top.Controls.Add(title);
        top.Controls.Add(accentRow);
        return top;
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

    void OnInventarioHandleDestroyed(object? sender, EventArgs e)
    {
        HandleDestroyed -= OnInventarioHandleDestroyed;
        InventarioSnapshotCoordinator.SnapshotUpdated -= OnInventarioSnapshotUpdated;
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
                ApplyInventoryLoadErrorUi(
                    _cpuValue,
                    _ramValue,
                    _gpuValue,
                    _motherboardValue,
                    _cpuSecondary,
                    _ramSecondary,
                    _gpuSecondary,
                    _motherboardSecondary);
        });
    }

    void OnInventarioLoad(object? sender, EventArgs e)
    {
        Load -= OnInventarioLoad;
        if (InventarioSnapshotCoordinator.TryGetLatest() is { } snap)
            ApplySnapshotToUi(snap);
    }

    void ApplySnapshotToUi(in InventarioHardwareSnapshot snapshot)
    {
        _cpuValue.Text = snapshot.Cpu;
        _ramValue.Text = snapshot.Ram;
        _gpuValue.Text = snapshot.Gpu;
        _motherboardValue.Text = snapshot.Motherboard;
        _cpuSecondary.Text = snapshot.CpuTemperatureLine;
        _ramSecondary.Text = snapshot.RamUsageLine;
        _gpuSecondary.Text = snapshot.GpuTemperatureLine;
        _motherboardSecondary.Text = snapshot.MotherboardSerialLine;
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
}
