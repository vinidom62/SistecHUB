using System;
using System.Windows;
using System.Windows.Input;

namespace SistecHub.UI;

public sealed partial class SettingsPasswordWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? ConfirmRequested;
    public event EventHandler? CancelRequested;

    public SettingsPasswordWpfControl(string titleText, string subtitleText)
    {
        InitializeComponent();
        TxtTitle.Text = titleText ?? "Senha necessária";
        TxtSubtitle.Text = subtitleText ?? "";
    }

    public string Password => PwdBox.Password ?? "";

    public void FocusPassword()
    {
        PwdBox.Focus();
    }

    public void ShowIncorrectPasswordError()
    {
        TxtError.Visibility = Visibility.Visible;
        PwdBox.SelectAll();
        PwdBox.Focus();
    }

    private void PwdBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        => ConfirmRequested?.Invoke(this, EventArgs.Empty);

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => CancelRequested?.Invoke(this, EventArgs.Empty);
}

