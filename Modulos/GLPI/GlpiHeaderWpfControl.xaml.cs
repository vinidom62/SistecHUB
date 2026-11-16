using System;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.GLPI;

public sealed partial class GlpiHeaderWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? ChamadosTabClicked;
    public event EventHandler? AberturaTabClicked;

    static readonly MediaBrush ActiveBgBrush = CreateFrozenBrush(238, 242, 255);
    static readonly MediaBrush ActiveFgBrush = CreateFrozenBrush(99, 102, 241);
    static readonly MediaBrush ActiveBorderBrush = CreateFrozenBrush(99, 102, 241);

    static readonly MediaBrush InactiveBgBrush = CreateFrozenBrush(241, 245, 249);
    static readonly MediaBrush InactiveFgBrush = CreateFrozenBrush(15, 23, 42);
    static readonly MediaBrush InactiveBorderBrush = CreateFrozenBrush(203, 213, 225);

    public GlpiHeaderWpfControl()
    {
        InitializeComponent();
        SetActiveTab(isChamadosActive: true);
    }

    public void SetActiveTab(bool isChamadosActive)
    {
        ApplyButtonState(BtnNavChamados, isChamadosActive);
        ApplyButtonState(BtnNavAbertura, !isChamadosActive);
    }

    static void ApplyButtonState(System.Windows.Controls.Button btn, bool active)
    {
        btn.Background = active ? ActiveBgBrush : InactiveBgBrush;
        btn.Foreground = active ? ActiveFgBrush : InactiveFgBrush;
        btn.BorderBrush = active ? ActiveBorderBrush : InactiveBorderBrush;
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void BtnNavChamados_Click(object sender, RoutedEventArgs e)
        => ChamadosTabClicked?.Invoke(this, EventArgs.Empty);

    private void BtnNavAbertura_Click(object sender, RoutedEventArgs e)
        => AberturaTabClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

