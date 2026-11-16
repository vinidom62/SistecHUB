using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.Inventario;

public sealed partial class InventarioWpfControl : System.Windows.Controls.UserControl
{
    const double DiskCardWidth = 260;
    const double DiskCardHeight = 224;

    public event EventHandler? RefreshClicked;
    public event EventHandler? KeyRevealOrHideClicked;
    public event EventHandler? CopyKeyClicked;

    static readonly MediaBrush PrimaryTextBrush = CreateFrozenBrush(15, 23, 42);
    static readonly MediaBrush MutedTextBrush = CreateFrozenBrush(100, 116, 139);
    static readonly MediaBrush AccentBrush = CreateFrozenBrush(99, 102, 241);
    static readonly MediaBrush WhiteBrush = CreateFrozenBrush(255, 255, 255);
    static readonly MediaBrush CardBorderBrush = CreateFrozenBrush(226, 232, 240);

    static readonly MediaBrush ActivatedBadgeBgBrush = CreateFrozenBrush(220, 252, 231);
    static readonly MediaBrush ActivatedBadgeFgBrush = CreateFrozenBrush(22, 101, 52);
    static readonly MediaBrush UnactivatedBadgeBgBrush = CreateFrozenBrush(254, 226, 226);
    static readonly MediaBrush UnactivatedBadgeFgBrush = CreateFrozenBrush(153, 27, 27);
    static readonly MediaBrush NeutralBadgeBgBrush = CreateFrozenBrush(241, 245, 249);

    static readonly MediaBrush HealthHealthyBrush = CreateFrozenBrush(22, 163, 74);
    static readonly MediaBrush HealthHealthyBgBrush = CreateFrozenBrush(220, 252, 231);
    static readonly MediaBrush HealthWarningBrush = CreateFrozenBrush(217, 119, 6);
    static readonly MediaBrush HealthWarningBgBrush = CreateFrozenBrush(254, 243, 199);
    static readonly MediaBrush HealthFailureBrush = CreateFrozenBrush(220, 38, 38);
    static readonly MediaBrush HealthFailureBgBrush = CreateFrozenBrush(254, 226, 226);

    static readonly MediaBrush ProgressTrackBrush = CreateFrozenBrush(241, 245, 249);
    static readonly MediaBrush ProgressFillNormalBrush = CreateFrozenBrush(79, 70, 229);
    static readonly MediaBrush ProgressFillWarningBrush = CreateFrozenBrush(217, 119, 6);
    static readonly MediaBrush ProgressFillDangerBrush = CreateFrozenBrush(220, 38, 38);

    readonly DispatcherTimer _hintHideTimer;
    DispatcherTimer? _copyHostnameResetTimer;

    public InventarioWpfControl()
    {
        InitializeComponent();

        TxtMachineHostname.Text = Environment.MachineName;
        CardMachineHostname.ToolTip = $"Clique para copiar o nome desta estação: {Environment.MachineName}";

        _hintHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220),
        };
        _hintHideTimer.Tick += (_, _) =>
        {
            _hintHideTimer.Stop();
            if (!DiskInfoTrigger.IsMouseOver && !DiskHealthHintCard.IsMouseOver)
            {
                DiskHealthHintCard.Visibility = Visibility.Collapsed;
                TxtDiskInfoIcon.Foreground = MutedTextBrush;
            }
        };

        ShowDiskPlaceholder("A carregar…");
    }

    public bool IsRefreshEnabled => BtnRefresh.IsEnabled;

    public void SetRefreshState(bool enabled, string? tooltip = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetRefreshState(enabled, tooltip));
            return;
        }

        BtnRefresh.IsEnabled = enabled;
        if (tooltip is not null)
            BtnRefresh.ToolTip = tooltip;

        if (!enabled)
        {
            TxtRefreshBtnLabel.Text = "Atualizando…";
            TxtRefreshBtnLabel.Foreground = AccentBrush;
            StartSpinning();
        }
        else
        {
            TxtRefreshBtnLabel.Text = "Atualizar";
            TxtRefreshBtnLabel.Foreground = CreateFrozenBrush(51, 65, 85);
            StopSpinning();
        }
    }

    void StartSpinning()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(StartSpinning);
            return;
        }

        IconRefresh.Foreground = AccentBrush;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.75),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        RefreshRotateTransform.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    void StopSpinning()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(StopSpinning);
            return;
        }

        RefreshRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        RefreshRotateTransform.Angle = 0;
        IconRefresh.Foreground = CreateFrozenBrush(51, 65, 85);
    }

    public void SetLastUpdateText(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetLastUpdateText(text));
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            TxtStatusCategory.Text = "SINCRONIZAÇÃO";
            TxtLastUpdate.Text = "Aguardando…";
            TxtLastUpdate.Foreground = MutedTextBrush;
            StatusDotContainer.Background = CreateFrozenBrush(148, 163, 184);
            return;
        }

        if (text.StartsWith("A atualizar", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Atualizando", StringComparison.OrdinalIgnoreCase))
        {
            TxtStatusCategory.Text = "STATUS";
            TxtLastUpdate.Text = "Sincronizando…";
            TxtLastUpdate.Foreground = AccentBrush;
            StatusDotContainer.Background = AccentBrush;
            return;
        }

        const string prefix = "Última atualização: ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var timePart = text.Substring(prefix.Length).Trim();
            TxtStatusCategory.Text = "ÚLTIMA SINCRONIZAÇÃO";
            TxtLastUpdate.Text = $"Às {timePart}";
            TxtLastUpdate.Foreground = CreateFrozenBrush(30, 41, 59);
            StatusDotContainer.Background = CreateFrozenBrush(16, 185, 129);
            return;
        }

        TxtStatusCategory.Text = "ÚLTIMA SINCRONIZAÇÃO";
        TxtLastUpdate.Text = text;
        TxtLastUpdate.Foreground = CreateFrozenBrush(30, 41, 59);
        StatusDotContainer.Background = CreateFrozenBrush(16, 185, 129);
    }

    public void SetCopyKeyTooltip(string text)
    {
        BtnCopyKey.ToolTip = text;
    }

    internal void ApplySnapshot(in InventarioHardwareSnapshot snapshot)
    {
        TxtCpuValue.Text = snapshot.Cpu;
        TxtRamValue.Text = snapshot.Ram;
        TxtGpuValue.Text = snapshot.Gpu;
        TxtMotherboardValue.Text = snapshot.Motherboard;

        TxtCpuSecondary.Text = snapshot.CpuTemperatureLine;
        TxtCpuSecondary.ToolTip = !string.IsNullOrWhiteSpace(snapshot.ProcessadorInfo.SensorTemperatura)
            ? $"Sensor utilizado: {snapshot.ProcessadorInfo.SensorTemperatura}"
            : null;

        TxtRamSecondary.Text = snapshot.RamUsageLine;
        TxtGpuSecondary.Text = snapshot.GpuTemperatureLine;

        var gpuSensors = string.Join(", ", snapshot.PlacasVideo
            .Where(g => !string.IsNullOrWhiteSpace(g.SensorTemperatura))
            .Select(g => $"{g.Nome}: {g.SensorTemperatura}"));
        TxtGpuSecondary.ToolTip = !string.IsNullOrWhiteSpace(gpuSensors)
            ? $"Sensor utilizado: {gpuSensors}"
            : null;

        TxtMotherboardSecondary.Text = snapshot.MotherboardSerialLine;

        RebuildDiskCards(snapshot.DiscosRigidos);

        var so = snapshot.SistemaOperacional;
        TxtOsName.Text = string.IsNullOrWhiteSpace(so.NomeProduto) || so.NomeProduto == "—"
            ? "Windows"
            : $"{so.NomeProduto} ({so.Arquitetura})";

        TxtOsVersion.Text = string.IsNullOrWhiteSpace(so.VersaoAtual) || so.VersaoAtual == "—"
            ? ""
            : $"•  {so.VersaoAtual}";

        var isAtivado = string.Equals(so.StatusAtivacao, "Ativado", StringComparison.OrdinalIgnoreCase);
        if (isAtivado)
        {
            var channelInfo = !string.IsNullOrWhiteSpace(so.CanalLicenca) ? $" ({so.CanalLicenca})" : "";
            TxtOsStatus.Text = $"✔ Ativado{channelInfo}";
            BdOsStatus.Background = ActivatedBadgeBgBrush;
            TxtOsStatus.Foreground = ActivatedBadgeFgBrush;
        }
        else
        {
            TxtOsStatus.Text = $"✖ {so.StatusAtivacao}";
            BdOsStatus.Background = UnactivatedBadgeBgBrush;
            TxtOsStatus.Foreground = UnactivatedBadgeFgBrush;
        }
    }

    public void ApplyErrorState()
    {
        const string err = "—";
        if (TxtCpuValue.Text == "A carregar…") TxtCpuValue.Text = err;
        if (TxtRamValue.Text == "A carregar…") TxtRamValue.Text = err;
        if (TxtGpuValue.Text == "A carregar…") TxtGpuValue.Text = err;
        if (TxtMotherboardValue.Text == "A carregar…") TxtMotherboardValue.Text = err;
        if (TxtCpuSecondary.Text == "A carregar…") TxtCpuSecondary.Text = "Temperatura: —";
        if (TxtRamSecondary.Text == "A carregar…") TxtRamSecondary.Text = "Uso: —";
        if (TxtGpuSecondary.Text == "A carregar…") TxtGpuSecondary.Text = "Temperatura: —";
        if (TxtMotherboardSecondary.Text == "A carregar…") TxtMotherboardSecondary.Text = "N.º de série: —";

        RebuildDiskCards(null);

        TxtOsName.Text = "Windows";
        TxtOsVersion.Text = "";
        TxtOsStatus.Text = "—";
        BdOsStatus.Background = NeutralBadgeBgBrush;
        TxtOsStatus.Foreground = MutedTextBrush;
    }

    public void UpdateKeyDisplay(string? currentOsKey, bool isKeyRevealed)
    {
        if (string.IsNullOrWhiteSpace(currentOsKey))
        {
            TxtOsKey.Visibility = Visibility.Collapsed;
            BtnRevealKey.Visibility = Visibility.Collapsed;
            BtnCopyKey.Visibility = Visibility.Collapsed;
            return;
        }

        TxtOsKey.Visibility = Visibility.Visible;
        BtnRevealKey.Visibility = Visibility.Visible;
        BtnCopyKey.Visibility = Visibility.Visible;

        if (isKeyRevealed)
        {
            TxtOsKey.Text = $"Chave: {currentOsKey}";
            TxtRevealKeyGlyph.Text = "\uE785"; // cadeado aberto
            TxtRevealKeyGlyph.Foreground = HealthFailureBrush;
            BtnRevealKey.ToolTip = "Ocultar chave de ativação";
            TxtOsKey.ToolTip = "Chave de ativação do Windows (clique para ocultar)";
        }
        else
        {
            TxtOsKey.Text = "Chave: *****-*****-*****-*****-*****";
            TxtRevealKeyGlyph.Text = "\uE72E"; // cadeado fechado
            TxtRevealKeyGlyph.Foreground = PrimaryTextBrush;
            BtnRevealKey.ToolTip = "Exibir chave (requer senha de administrador)";
            TxtOsKey.ToolTip = "Clique para exibir a chave (requer senha de administrador)";
        }
    }

    void RebuildDiskCards(IReadOnlyList<DiscoRigidoInventario>? disks)
    {
        DisksStackPanel.Children.Clear();

        if (disks is null || disks.Count == 0)
        {
            ShowDiskPlaceholder(disks is null ? "—" : "Nenhum disco detectado");
            return;
        }

        foreach (var disk in disks)
            DisksStackPanel.Children.Add(CreateDiskCard(disk));
    }

    void ShowDiskPlaceholder(string message)
    {
        DisksStackPanel.Children.Clear();

        var card = new Border
        {
            Width = DiskCardWidth,
            Height = DiskCardHeight,
            Margin = new Thickness(0, 0, 14, 0),
            Padding = new Thickness(16, 14, 16, 14),
            Background = WhiteBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect
            {
                Color = MediaColor.FromRgb(15, 23, 42),
                BlurRadius = 10,
                ShadowDepth = 1.5,
                Direction = 270,
                Opacity = 0.04,
            },
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var icon = new TextBlock
        {
            Text = "\uEDA2",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 32,
            Foreground = AccentBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var text = new TextBlock
        {
            Text = message,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedTextBrush,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };

        stack.Children.Add(icon);
        stack.Children.Add(text);
        card.Child = stack;

        DisksStackPanel.Children.Add(card);
    }

    static UIElement CreateDiskCard(DiscoRigidoInventario disk)
    {
        var card = new Border
        {
            Width = DiskCardWidth,
            Height = DiskCardHeight,
            Margin = new Thickness(0, 0, 14, 0),
            Padding = new Thickness(16, 14, 16, 14),
            Background = WhiteBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect
            {
                Color = MediaColor.FromRgb(15, 23, 42),
                BlurRadius = 10,
                ShadowDepth = 1.5,
                Direction = 270,
                Opacity = 0.04,
            },
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Topo (ícone + tipo)
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Nome do disco
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Barra de espaço
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Espaço flexível
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Saúde + Vida útil
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Motivo do alerta
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: Serial

        // 0: Header (Ícone + Tipo)
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBox = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = CreateFrozenBrush(238, 242, 255),
            Child = new TextBlock
            {
                Text = "\uEDA2",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = AccentBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBox, 0);

        var tipoBadge = new Border
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = NeutralBadgeBgBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2.5, 7, 2.5),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(disk.Tipo) ? "DISCO" : disk.Tipo.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = MutedTextBrush,
            },
        };
        Grid.SetColumn(tipoBadge, 1);

        header.Children.Add(iconBox);
        header.Children.Add(tipoBadge);
        Grid.SetRow(header, 0);

        // 1: Nome do Modelo
        var nameBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(disk.Nome) ? "Disco" : disk.Nome,
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = PrimaryTextBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = disk.Nome,
        };
        Grid.SetRow(nameBlock, 1);

        // 2: Barra de Espaço
        var spaceContainer = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var spaceText = new TextBlock
        {
            Text = FormatDiskSpace(disk.ArmazenamentoUsadoGb, disk.ArmazenamentoTotalGb),
            FontSize = 11,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 4),
        };
        spaceContainer.Children.Add(spaceText);

        if (disk.ArmazenamentoTotalGb is { } total && total > 0 && disk.ArmazenamentoUsadoGb is { } used)
        {
            var ratio = Math.Clamp(used / total, 0f, 1f);
            var fillBrush = ratio >= 0.9f ? ProgressFillDangerBrush : (ratio >= 0.8f ? ProgressFillWarningBrush : ProgressFillNormalBrush);

            var track = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = ProgressTrackBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            };

            var bar = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = (DiskCardWidth - 32) * ratio,
            };

            var trackGrid = new Grid();
            trackGrid.Children.Add(track);
            trackGrid.Children.Add(bar);
            spaceContainer.Children.Add(trackGrid);
        }
        Grid.SetRow(spaceContainer, 2);

        // 4: Saúde + Vida Útil
        var healthRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        healthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        healthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var healthBadge = new Border
        {
            Background = DiskHealthBgBrush(disk.Saude),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = FormatDiskHealth(disk.Saude),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DiskHealthBrush(disk.Saude),
            },
        };
        Grid.SetColumn(healthBadge, 0);

        if (!string.IsNullOrWhiteSpace(disk.AlertaMotivo))
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Motivo do alerta: ").AppendLine(disk.AlertaMotivo);
            if (disk.AlertaDetalhes is { Count: > 0 } details)
            {
                sb.AppendLine();
                sb.AppendLine("Detalhes técnicos (S.M.A.R.T.):");
                foreach (var det in details)
                    sb.Append("• ").AppendLine(det);
            }
            healthBadge.ToolTip = sb.ToString().TrimEnd();
        }

        var lifeText = new TextBlock
        {
            Text = disk.VidaPercent is { } pct ? $"Vida: {pct:0.#}%" : "Vida: —",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedTextBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lifeText, 1);

        healthRow.Children.Add(healthBadge);
        healthRow.Children.Add(lifeText);
        Grid.SetRow(healthRow, 4);

        // 5: Alerta em destaque no card (se houver motivo de alerta)
        Border? alertBadge = null;
        if (!string.IsNullOrWhiteSpace(disk.AlertaMotivo))
        {
            var isFail = disk.Saude == "falha";
            alertBadge = new Border
            {
                Background = isFail ? CreateFrozenBrush(254, 242, 242) : CreateFrozenBrush(254, 249, 195),
                BorderBrush = isFail ? CreateFrozenBrush(254, 202, 202) : CreateFrozenBrush(253, 224, 71),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2.5, 6, 2.5),
                Margin = new Thickness(0, 2, 0, 0),
                ToolTip = healthBadge.ToolTip,
                Child = new TextBlock
                {
                    Text = $"⚠ {disk.AlertaMotivo}",
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isFail ? CreateFrozenBrush(185, 28, 28) : CreateFrozenBrush(133, 77, 14),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Grid.SetRow(alertBadge, 5);
        }

        // 6: Serial Number (+ Temperatura se houver)
        var serialText = string.IsNullOrWhiteSpace(disk.NumeroSerie) ? "S/N: —" : $"S/N: {disk.NumeroSerie}";
        if (disk.TemperaturaC is { } t)
            serialText += $" • {t}°C";

        var serialBlock = new TextBlock
        {
            Text = serialText,
            FontSize = 10,
            Foreground = CreateFrozenBrush(148, 163, 184),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetRow(serialBlock, 6);

        grid.Children.Add(header);
        grid.Children.Add(nameBlock);
        grid.Children.Add(spaceContainer);
        grid.Children.Add(healthRow);
        if (alertBadge != null)
            grid.Children.Add(alertBadge);
        grid.Children.Add(serialBlock);

        card.Child = grid;
        return card;
    }

    static string FormatDiskHealth(string saude) =>
        (saude ?? "").Trim().ToLowerInvariant() switch
        {
            "saudavel" => "● Sem alertas",
            "atencao" => "⚠ Atenção",
            "falha" => "✖ Falha",
            _ => "Desconhecida",
        };

    static MediaBrush DiskHealthBrush(string saude) =>
        (saude ?? "").Trim().ToLowerInvariant() switch
        {
            "saudavel" => HealthHealthyBrush,
            "atencao" => HealthWarningBrush,
            "falha" => HealthFailureBrush,
            _ => MutedTextBrush,
        };

    static MediaBrush DiskHealthBgBrush(string saude) =>
        (saude ?? "").Trim().ToLowerInvariant() switch
        {
            "saudavel" => HealthHealthyBgBrush,
            "atencao" => HealthWarningBgBrush,
            "falha" => HealthFailureBgBrush,
            _ => NeutralBadgeBgBrush,
        };

    static string FormatDiskSpace(float? usedGb, float? totalGb)
    {
        if (usedGb is null && totalGb is null)
            return "Espaço: —";
        if (totalGb is null)
            return $"Usado: {usedGb:0.#} GB";
        if (usedGb is null)
            return $"Total: {totalGb:0.#} GB";

        var freeGb = Math.Max(0, (totalGb.Value - usedGb.Value));
        return $"{usedGb:0.#} GB de {totalGb:0.#} GB ({freeGb:0.#} GB livres)";
    }

    private void DisksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DisksScrollViewer.ScrollableWidth <= 0)
            return;
        DisksScrollViewer.ScrollToHorizontalOffset(DisksScrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void DiskInfoTrigger_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hintHideTimer.Stop();
        TxtDiskInfoIcon.Foreground = AccentBrush;
        DiskHealthHintCard.Visibility = Visibility.Visible;
    }

    private void DiskInfoTrigger_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hintHideTimer.Stop();
        _hintHideTimer.Start();
    }

    private void DiskHealthHintCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hintHideTimer.Stop();
    }

    private void DiskHealthHintCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hintHideTimer.Stop();
        _hintHideTimer.Start();
    }

    private void CardMachineHostname_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            System.Windows.Clipboard.SetText(Environment.MachineName);
            IconCopyHostname.Text = "\uE73E"; // Checkmark icon
            IconCopyHostname.Foreground = CreateFrozenBrush(22, 163, 74);
            CardMachineHostname.ToolTip = "✓ Nome da máquina copiado para a área de transferência!";

            _copyHostnameResetTimer?.Stop();
            _copyHostnameResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyHostnameResetTimer.Tick += (_, _) =>
            {
                _copyHostnameResetTimer.Stop();
                IconCopyHostname.Text = "\uE8C8"; // Copy icon
                IconCopyHostname.Foreground = CreateFrozenBrush(148, 163, 184);
                CardMachineHostname.ToolTip = $"Clique para copiar o nome desta estação: {Environment.MachineName}";
            };
            _copyHostnameResetTimer.Start();
        }
        catch
        {
            // Área de transferência pode estar bloqueada temporariamente
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => RefreshClicked?.Invoke(this, EventArgs.Empty);

    private void TxtOsKey_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => KeyRevealOrHideClicked?.Invoke(this, EventArgs.Empty);

    private void BtnRevealKey_Click(object sender, RoutedEventArgs e)
        => KeyRevealOrHideClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCopyKey_Click(object sender, RoutedEventArgs e)
        => CopyKeyClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
