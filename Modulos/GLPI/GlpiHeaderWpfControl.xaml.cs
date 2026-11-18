using System;
using System.Windows;
using SistecHub.UI;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.GLPI;

public sealed partial class GlpiHeaderWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? ChamadosTabClicked;
    public event EventHandler? AberturaTabClicked;

    static MediaBrush ActiveBgBrush => AppThemeManager.IndigoSoftBg;
    static MediaBrush ActiveFgBrush => AppThemeManager.IndigoText;
    static readonly MediaBrush ActiveBorderBrush = CreateFrozenBrush(99, 102, 241);

    static MediaBrush InactiveBgBrush => AppThemeManager.SubtleBg;
    static MediaBrush InactiveFgBrush => AppThemeManager.TextPrimary;
    static MediaBrush InactiveBorderBrush => AppThemeManager.BorderStrong;

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
        btn.SetResourceReference(BackgroundProperty, active ? "Theme.IndigoSoftBg" : "Theme.SubtleBg");
        btn.SetResourceReference(ForegroundProperty, active ? "Theme.IndigoText" : "Theme.TextPrimary");
        btn.BorderBrush = active ? ActiveBorderBrush : InactiveBorderBrush;
        if (!active)
            btn.SetResourceReference(BorderBrushProperty, "Theme.BorderStrong");
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

