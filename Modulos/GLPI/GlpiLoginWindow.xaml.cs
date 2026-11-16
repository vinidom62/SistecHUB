using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfPasswordBox = System.Windows.Controls.PasswordBox;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace SistecHub.Modulos.GLPI;

public sealed partial class GlpiLoginWindow : Window
{
    bool _isPasswordRevealed;
    bool _syncingPassword;
    bool _isAuthenticating;

    public GlpiLoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtLogin.Focus();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            var p = d;
            while (p != null && p != this)
            {
                if (p is WpfTextBox || p is WpfPasswordBox || p is WpfButton || p is WpfCheckBox)
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

    public string CurrentPassword => _isPasswordRevealed
        ? (TxtPasswordRevealed.Text ?? "").Trim()
        : (PwdPassword.Password ?? "").Trim();

    private void PwdPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncingPassword)
        {
            _syncingPassword = true;
            TxtPasswordRevealed.Text = PwdPassword.Password;
            _syncingPassword = false;
        }
    }

    private void TxtPasswordRevealed_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingPassword)
        {
            _syncingPassword = true;
            PwdPassword.Password = TxtPasswordRevealed.Text;
            _syncingPassword = false;
        }
    }

    private void BtnToggleEye_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            TxtPasswordRevealed.Text = PwdPassword.Password;
            PwdPassword.Visibility = Visibility.Collapsed;
            TxtPasswordRevealed.Visibility = Visibility.Visible;
            TxtPasswordRevealed.CaretIndex = TxtPasswordRevealed.Text.Length;
            TxtPasswordRevealed.Focus();
        }
        else
        {
            PwdPassword.Password = TxtPasswordRevealed.Text;
            TxtPasswordRevealed.Visibility = Visibility.Collapsed;
            PwdPassword.Visibility = Visibility.Visible;
            PwdPassword.Focus();
        }
    }

    private void ChkRememberPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ChkRememberPassword.IsChecked == true)
        {
            var confirmed = GlpiRememberLoginConfirmDialog.Confirm(this);
            if (!confirmed)
            {
                ChkRememberPassword.IsChecked = false;
            }
        }
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isAuthenticating)
        {
            e.Handled = true;
            BtnSubmit_Click(sender, e);
        }
        else if (e.Key == Key.Escape && !_isAuthenticating)
        {
            e.Handled = true;
            SafeClose(false);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAuthenticating)
        {
            SafeClose(false);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAuthenticating)
        {
            SafeClose(false);
        }
    }

    private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (_isAuthenticating)
            return;

        var login = (TxtLogin.Text ?? "").Trim();
        var password = CurrentPassword;

        if (string.IsNullOrEmpty(login))
        {
            ShowError("Informe seu usuário Angelus.");
            TxtLogin.Focus();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowError("Informe sua senha Angelus.");
            if (_isPasswordRevealed)
                TxtPasswordRevealed.Focus();
            else
                PwdPassword.Focus();
            return;
        }

        SetLoadingState(true);

        try
        {
            var profile = await GlpiTechnicianAuthService.AuthenticateAsync(login, password)
                .ConfigureAwait(true);

            var remember = ChkRememberPassword.IsChecked == true;
            GlpiTechnicianSession.SetAuthenticated(profile, password, remember);

            SafeClose(true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    void ShowError(string message)
    {
        TxtErrorMessage.Text = message ?? "Erro ao autenticar no Angelus.";
        ErrorBorder.Visibility = Visibility.Visible;
    }

    void SetLoadingState(bool isLoading)
    {
        _isAuthenticating = isLoading;
        TxtLogin.IsEnabled = !isLoading;
        PwdPassword.IsEnabled = !isLoading;
        TxtPasswordRevealed.IsEnabled = !isLoading;
        BtnToggleEye.IsEnabled = !isLoading;
        ChkRememberPassword.IsEnabled = !isLoading;
        BtnSubmit.IsEnabled = !isLoading;
        BtnCancel.IsEnabled = !isLoading;

        if (isLoading)
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
            ProgressBorder.Visibility = Visibility.Visible;
        }
        else
        {
            ProgressBorder.Visibility = Visibility.Collapsed;
        }
    }

    public static GlpiLoginWindow? CurrentActiveInstance { get; private set; }

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

    public static bool PromptLogin(Window? owner = null)
    {
        if (CurrentActiveInstance != null && CurrentActiveInstance.IsLoaded)
        {
            CurrentActiveInstance.Activate();
            CurrentActiveInstance.Focus();
            return false;
        }

        var win = new GlpiLoginWindow();
        CurrentActiveInstance = win;
        win.Closed += (_, _) =>
        {
            if (CurrentActiveInstance == win)
                CurrentActiveInstance = null;
        };

        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            try
            {
                win.Left = owner.Left + (owner.Width - win.Width) / 2;
                win.Top = owner.Top + Math.Max(40, (owner.Height - 440) / 2);
            }
            catch { }
        }

        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(win);
        win.Show();
        win.Activate();
        win.Focus();
        win.TxtLogin.Focus();
        return false;
    }
}
