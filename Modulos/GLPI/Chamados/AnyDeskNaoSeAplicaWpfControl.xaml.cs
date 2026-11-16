using System;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.GLPI.Chamados;

public sealed partial class AnyDeskNaoSeAplicaWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? CancelRequested;
    public event EventHandler? ConfirmRequested;

    static readonly MediaBrush ActiveBrush = CreateFrozenBrush(99, 102, 241);

    public AnyDeskNaoSeAplicaWpfControl()
    {
        InitializeComponent();
    }

    public void UpdateCountdown(int secondsRemaining)
    {
        if (secondsRemaining > 0)
        {
            BtnConfirm.Content = $"Confirmar ação ({secondsRemaining})";
            BtnConfirm.IsEnabled = false;
        }
        else
        {
            BtnConfirm.Content = "Confirmar ação";
            BtnConfirm.IsEnabled = true;
            BtnConfirm.Background = ActiveBrush;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        => ConfirmRequested?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

