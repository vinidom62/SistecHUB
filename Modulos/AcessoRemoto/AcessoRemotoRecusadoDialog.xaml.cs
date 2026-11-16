using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SistecHub.Modulos.AcessoRemoto;

public sealed partial class AcessoRemotoRecusadoDialog : Window
{
    public static AcessoRemotoRecusadoDialog? CurrentActiveInstance { get; private set; }

    public AcessoRemotoRecusadoDialog(string? customMessage = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            TxtMainMessage.Text = customMessage.Trim();
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                e.Handled = true;
                Close();
            }
        };

        Closed += (_, _) =>
        {
            if (CurrentActiveInstance == this)
                CurrentActiveInstance = null;
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            var p = d;
            while (p != null && p != this)
            {
                if (p is System.Windows.Controls.Button)
                    return;
                p = VisualTreeHelper.GetParent(p);
            }
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public static void ShowDialog(string? message = null, Window? owner = null)
    {
        if (CurrentActiveInstance != null && CurrentActiveInstance.IsLoaded)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                CurrentActiveInstance.TxtMainMessage.Text = message.Trim();
            }
            CurrentActiveInstance.Activate();
            CurrentActiveInstance.Focus();
            return;
        }

        var dlg = new AcessoRemotoRecusadoDialog(message);
        CurrentActiveInstance = dlg;

        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            try
            {
                dlg.Left = owner.Left + (owner.Width - dlg.Width) / 2;
                dlg.Top = owner.Top + Math.Max(40, (owner.Height - 340) / 2);
            }
            catch { }
        }

        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(dlg);
        dlg.Show();
        dlg.Activate();
        dlg.Focus();
    }
}

