using System.Windows;
using System.Windows.Input;

namespace SistecHub.Modulos.GLPI;

public sealed partial class GlpiRememberLoginConfirmDialog : Window
{
    public GlpiRememberLoginConfirmDialog()
    {
        InitializeComponent();
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                SafeClose(false);
            }
            else if (e.Key == Key.Enter)
            {
                SafeClose(true);
            }
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
                p = System.Windows.Media.VisualTreeHelper.GetParent(p);
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

    private void SafeClose(bool result)
    {
        try
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
            {
                DialogResult = result;
            }
        }
        catch { }
        Close();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        SafeClose(true);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        SafeClose(false);
    }

    public static GlpiRememberLoginConfirmDialog? CurrentActiveInstance { get; private set; }

    public static void CloseActiveInstance()
    {
        try
        {
            if (CurrentActiveInstance != null)
            {
                var inst = CurrentActiveInstance;
                CurrentActiveInstance = null;
                inst.Close();
            }
        }
        catch { }
    }

    public static bool Confirm(Window? owner = null)
    {
        var dlg = new GlpiRememberLoginConfirmDialog();
        CurrentActiveInstance = dlg;
        dlg.Closed += (_, _) =>
        {
            if (CurrentActiveInstance == dlg)
                CurrentActiveInstance = null;
        };

        if (owner != null && owner.IsLoaded && owner.IsVisible)
            dlg.Owner = owner;

        return dlg.ShowDialog() == true;
    }
}

