using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.Modulos.AcessoRemoto;

public sealed partial class AcessoRemotoWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? ReconnectClicked;
    public event EventHandler? CopyBothClicked;
    public event EventHandler? OpenChatClicked;
    public event EventHandler? HostDisconnectClicked;
    public event EventHandler? ActiveEndSessionClicked;
    public event EventHandler? ConnectToRemoteClicked;

    static readonly MediaBrush StatusEmeraldBrush = CreateFrozenBrush(16, 185, 129);
    static readonly MediaBrush StatusAmberBrush = CreateFrozenBrush(245, 158, 11);
    static readonly MediaBrush StatusRedBrush = CreateFrozenBrush(239, 68, 68);
    static readonly MediaBrush StatusSlateBrush = CreateFrozenBrush(148, 163, 184);

    static readonly MediaBrush TextDarkBrush = CreateFrozenBrush(30, 41, 59);
    static readonly MediaBrush TextActiveBrush = CreateFrozenBrush(22, 101, 52);
    static readonly MediaBrush TextAmberBrush = CreateFrozenBrush(180, 83, 9);
    static readonly MediaBrush TextRedBrush = CreateFrozenBrush(185, 28, 28);
    static readonly MediaBrush TextMutedBrush = CreateFrozenBrush(100, 116, 139);
    static readonly MediaBrush AccentIndigoBrush = CreateFrozenBrush(79, 70, 229);
    static readonly MediaBrush DefaultIconTextBrush = CreateFrozenBrush(51, 65, 85);

    static readonly MediaBrush PhaseDefaultFgBrush = TextMutedBrush;

    static readonly MediaBrush CopySuccessBrush = CreateFrozenBrush(22, 163, 74);
    static readonly MediaBrush CopyErrorBrush = CreateFrozenBrush(185, 28, 28);

    bool _isPasswordRevealed;
    bool _syncingPasswordText;

    public AcessoRemotoWpfControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            GlpiTechnicianSession.Initialize();
            UpdateTechnicianUiState();
        };
        GlpiTechnicianSession.StateChanged += (_, _) => Dispatcher.Invoke(UpdateTechnicianUiState);
    }

    public string TargetIdInput => (TxtConnectTargetId.Text ?? "").Trim().Replace(" ", "");

    public string PasswordInput => _isPasswordRevealed
        ? (TxtConnectPasswordRevealed.Text ?? "").Trim()
        : (PwdConnectPassword.Password ?? "").Trim();

    public bool IsActiveSessionCardVisible => ActiveSessionCard.Visibility == Visibility.Visible;

    public void FocusTargetIdInput() => TxtConnectTargetId.Focus();

    public void FocusPasswordInput()
    {
        if (!GlpiTechnicianSession.IsAuthenticated)
        {
            PromptGlpiLogin();
            return;
        }

        if (_isPasswordRevealed)
            TxtConnectPasswordRevealed.Focus();
        else
            PwdConnectPassword.Focus();
    }

    public void UpdateTechnicianUiState()
    {
        var isAuth = GlpiTechnicianSession.IsAuthenticated;
        if (isAuth)
        {
            BtnHeaderGlpiLogin.Visibility = Visibility.Collapsed;
            HeaderTechnicianCard.Visibility = Visibility.Visible;
            TxtHeaderTechName.Text = GlpiTechnicianSession.TechnicianName;

            if (!string.IsNullOrWhiteSpace(GlpiTechnicianSession.TechnicianAvatarBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(GlpiTechnicianSession.TechnicianAvatarBase64);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    HeaderAvatarImageBrush.ImageSource = bmp;
                    HeaderAvatarImageCircle.Visibility = Visibility.Visible;
                    HeaderAvatarFallback.Visibility = Visibility.Collapsed;
                    HeaderAvatarInitials.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    SetHeaderAvatarFallback(GlpiTechnicianSession.TechnicianName);
                }
            }
            else
            {
                SetHeaderAvatarFallback(GlpiTechnicianSession.TechnicianName);
            }

            BorderConnectPassword.Opacity = 1.0;
            BorderConnectPassword.Background = System.Windows.Media.Brushes.White;
            BorderConnectPassword.ToolTip = null;
            PwdConnectPassword.ToolTip = null;
            TxtConnectPasswordRevealed.ToolTip = null;
            PwdConnectPassword.IsEnabled = true;
            TxtConnectPasswordRevealed.IsEnabled = true;
        }
        else
        {
            BtnHeaderGlpiLogin.Visibility = Visibility.Visible;
            HeaderTechnicianCard.Visibility = Visibility.Collapsed;

            PwdConnectPassword.Password = "";
            TxtConnectPasswordRevealed.Text = "";
            PwdConnectPassword.IsEnabled = false;
            TxtConnectPasswordRevealed.IsEnabled = false;
            BorderConnectPassword.Opacity = 0.7;
            BorderConnectPassword.Background = new MediaSolidColorBrush(MediaColor.FromRgb(248, 250, 252));
            BorderConnectPassword.ToolTip = "Faça login para desbloquear";
            PwdConnectPassword.ToolTip = "Faça login para desbloquear";
            TxtConnectPasswordRevealed.ToolTip = "Faça login para desbloquear";
        }
    }

    void SetHeaderAvatarFallback(string techName)
    {
        HeaderAvatarImageBrush.ImageSource = null;
        HeaderAvatarImageCircle.Visibility = Visibility.Collapsed;
        HeaderAvatarFallback.Visibility = Visibility.Visible;
        HeaderAvatarInitials.Visibility = Visibility.Visible;
        HeaderAvatarInitials.Text = GetInitials(techName);
    }

    public bool PromptGlpiLogin()
    {
        var parentWindow = Window.GetWindow(this);
        return GlpiLoginWindow.PromptLogin(parentWindow);
    }

    private void BtnHeaderGlpiLogin_Click(object sender, RoutedEventArgs e)
    {
        PromptGlpiLogin();
    }

    private void BtnHeaderGlpiLogout_Click(object sender, RoutedEventArgs e)
    {
        var res = System.Windows.MessageBox.Show(
            "Deseja realmente desconectar sua conta de técnico Angelus deste computador?",
            "Desconectar Angelus",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            GlpiTechnicianSession.Logout();
        }
    }

    public void SetDurationText(string text)
    {
        TxtActiveDuration.Text = text ?? "Duração: 00:00:00";
    }

    public void ShowCopyFeedback(string message, bool isError = false)
    {
        TxtCopyFeedback.Text = message ?? "";
        TxtCopyFeedback.Foreground = isError ? CopyErrorBrush : CopySuccessBrush;
        TxtCopyFeedbackBorder.Visibility = Visibility.Visible;
    }

    public void HideCopyFeedback()
    {
        TxtCopyFeedbackBorder.Visibility = Visibility.Collapsed;
    }

    public void ShowConnectError(string message)
    {
        TxtConnectError.Text = message ?? "";
        TxtConnectError.Visibility = Visibility.Visible;
    }

    public void HideConnectError()
    {
        TxtConnectError.Visibility = Visibility.Collapsed;
    }

    internal void ApplyState(AcessoRemotoStateSnapshot snap, string durationText)
    {
        var isBloqueado = AppSettingsStore.Load().RemotoBloqueado;
        if (isBloqueado)
        {
            TxtHostId.Text = "BLOQUEADO";
            TxtHostId.Foreground = StatusRedBrush;
            TxtUnattendedStatus.Text = "● Acesso remoto desativado nas configurações";
            TxtUnattendedStatus.Foreground = StatusRedBrush;
        }
        else
        {
            TxtHostId.Text = snap.FormattedId;
            TxtHostId.Foreground = AccentIndigoBrush;

            if (snap.HasUnattendedPassword)
            {
                TxtUnattendedStatus.Text = "● Acesso não supervisionado ativo";
                TxtUnattendedStatus.Foreground = CopySuccessBrush;
            }
            else
            {
                TxtUnattendedStatus.Text = "● Acesso supervisionado";
                TxtUnattendedStatus.Foreground = PhaseDefaultFgBrush;
            }
        }

        var activeVis = snap.IsSessionActive ? Visibility.Visible : Visibility.Collapsed;
        BtnHostOpenChat.Visibility = activeVis;
        BtnHostDisconnect.Visibility = activeVis;
        ActiveSessionCard.Visibility = activeVis;

        if (snap.IsSessionActive)
        {
            var techName = !string.IsNullOrWhiteSpace(snap.ConnectedTechnicianName)
                ? snap.ConnectedTechnicianName
                : "Técnico de Suporte";
            TxtActiveTechName.Text = techName;
            TxtActiveDuration.Text = durationText;

            if (!string.IsNullOrWhiteSpace(snap.ConnectedTechnicianAvatar))
            {
                try
                {
                    var bytes = Convert.FromBase64String(snap.ConnectedTechnicianAvatar);
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    AvatarImageBrush.ImageSource = bmp;
                    AvatarImageCircle.Visibility = Visibility.Visible;
                    AvatarFallbackCircle.Visibility = Visibility.Collapsed;
                    TxtAvatarInitials.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    SetAvatarFallback(techName);
                }
            }
            else
            {
                SetAvatarFallback(techName);
            }
        }

        if (isBloqueado)
        {
            TxtStatusCategory.Text = "ACESSO REMOTO";
            TxtStatusBadge.Text = "Bloqueado";
            TxtStatusBadge.Foreground = TextRedBrush;
            StatusDotContainer.Background = StatusRedBrush;
            HeaderStatusCard.ToolTip = "O sistema de acesso remoto está totalmente bloqueado nesta máquina pelas configurações.";
        }
        else
        {
            switch (snap.Phase)
            {
                case AcessoRemotoConnectionPhase.SessionActive:
                    TxtStatusCategory.Text = "SESSÃO ATIVA";
                    TxtStatusBadge.Text = "Em atendimento";
                    TxtStatusBadge.Foreground = TextActiveBrush;
                    StatusDotContainer.Background = StatusEmeraldBrush;
                    HeaderStatusCard.ToolTip = "Sessão de suporte remoto ativa no momento com técnico.";
                    break;

                case AcessoRemotoConnectionPhase.ConnectedWaiting:
                    TxtStatusCategory.Text = "SERVIDOR RELAY";
                    TxtStatusBadge.Text = "Conectado";
                    TxtStatusBadge.Foreground = TextDarkBrush;
                    StatusDotContainer.Background = StatusEmeraldBrush;
                    HeaderStatusCard.ToolTip = "Conectado ao servidor relay com sucesso. Pronto para receber suporte.";
                    break;

                case AcessoRemotoConnectionPhase.Connecting:
                case AcessoRemotoConnectionPhase.Reconnecting:
                    TxtStatusCategory.Text = "STATUS";
                    TxtStatusBadge.Text = "Conectando…";
                    TxtStatusBadge.Foreground = TextAmberBrush;
                    StatusDotContainer.Background = StatusAmberBrush;
                    HeaderStatusCard.ToolTip = !string.IsNullOrWhiteSpace(snap.StatusText)
                        ? snap.StatusText
                        : "A estabelecer conexão com o servidor de suporte...";
                    break;

                case AcessoRemotoConnectionPhase.Error:
                    TxtStatusCategory.Text = "CONEXÃO";
                    TxtStatusBadge.Text = "Erro de conexão";
                    TxtStatusBadge.Foreground = TextRedBrush;
                    StatusDotContainer.Background = StatusRedBrush;
                    HeaderStatusCard.ToolTip = !string.IsNullOrWhiteSpace(snap.LastError)
                        ? snap.LastError
                        : (!string.IsNullOrWhiteSpace(snap.StatusText) ? snap.StatusText : "Não foi possível conectar ao servidor relay.");
                    break;

                default:
                    TxtStatusCategory.Text = "SERVIDOR RELAY";
                    TxtStatusBadge.Text = "Desconectado";
                    TxtStatusBadge.Foreground = TextMutedBrush;
                    StatusDotContainer.Background = StatusSlateBrush;
                    HeaderStatusCard.ToolTip = "Serviço de acesso remoto desconectado.";
                    break;
            }
        }

        if (snap.Phase == AcessoRemotoConnectionPhase.Connecting || snap.Phase == AcessoRemotoConnectionPhase.Reconnecting)
            StartSpinningReconnect();
        else
            StopSpinningReconnect();
    }

    bool _isReconnectingSpinning;

    void StartSpinningReconnect()
    {
        if (_isReconnectingSpinning) return;
        _isReconnectingSpinning = true;

        TxtRefreshBtnLabel.Text = "Conectando…";
        TxtRefreshBtnLabel.Foreground = AccentIndigoBrush;
        IconReconnect.Foreground = AccentIndigoBrush;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.75),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        ReconnectRotateTransform.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    void StopSpinningReconnect()
    {
        if (!_isReconnectingSpinning) return;
        _isReconnectingSpinning = false;

        ReconnectRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        ReconnectRotateTransform.Angle = 0;
        TxtRefreshBtnLabel.Text = "Reconectar";
        TxtRefreshBtnLabel.Foreground = DefaultIconTextBrush;
        IconReconnect.Foreground = DefaultIconTextBrush;
    }

    void SetAvatarFallback(string techName)
    {
        AvatarImageBrush.ImageSource = null;
        AvatarImageCircle.Visibility = Visibility.Collapsed;
        AvatarFallbackCircle.Visibility = Visibility.Visible;
        TxtAvatarInitials.Visibility = Visibility.Visible;
        TxtAvatarInitials.Text = GetInitials(techName);
    }

    static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "TS";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    private void TxtConnectTargetId_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HintConnectTargetId.Visibility = string.IsNullOrEmpty(TxtConnectTargetId.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PwdConnectPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_syncingPasswordText)
        {
            _syncingPasswordText = true;
            TxtConnectPasswordRevealed.Text = PwdConnectPassword.Password;
            _syncingPasswordText = false;
        }
        UpdatePasswordHintVisibility();
    }

    private void TxtConnectPasswordRevealed_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_syncingPasswordText)
        {
            _syncingPasswordText = true;
            PwdConnectPassword.Password = TxtConnectPasswordRevealed.Text;
            _syncingPasswordText = false;
        }
        UpdatePasswordHintVisibility();
    }

    void UpdatePasswordHintVisibility()
    {
        var hasText = !string.IsNullOrEmpty(PwdConnectPassword.Password)
                      || !string.IsNullOrEmpty(TxtConnectPasswordRevealed.Text);
        HintConnectPassword.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnToggleEye_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            TxtConnectPasswordRevealed.Text = PwdConnectPassword.Password;
            PwdConnectPassword.Visibility = Visibility.Collapsed;
            TxtConnectPasswordRevealed.Visibility = Visibility.Visible;
            TxtConnectPasswordRevealed.CaretIndex = TxtConnectPasswordRevealed.Text.Length;
            TxtConnectPasswordRevealed.Focus();
        }
        else
        {
            PwdConnectPassword.Password = TxtConnectPasswordRevealed.Text;
            TxtConnectPasswordRevealed.Visibility = Visibility.Collapsed;
            PwdConnectPassword.Visibility = Visibility.Visible;
            PwdConnectPassword.Focus();
        }
        UpdatePasswordHintVisibility();
    }

    private void TxtConnectTargetId_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (AppSettingsStore.Load().RemotoBloqueado)
            {
                ShowConnectError("O acesso remoto está desabilitado nas configurações desta máquina.");
                return;
            }
            ConnectToRemoteClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ConnectPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (AppSettingsStore.Load().RemotoBloqueado)
            {
                ShowConnectError("O acesso remoto está desabilitado nas configurações desta máquina.");
                return;
            }
            ConnectToRemoteClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BtnReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsStore.Load().RemotoBloqueado)
        {
            ShowCopyFeedback("Acesso remoto bloqueado nesta máquina nas configurações.", isError: true);
            return;
        }
        ReconnectClicked?.Invoke(this, EventArgs.Empty);
    }

    private void BtnCopyBoth_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsStore.Load().RemotoBloqueado)
        {
            ShowCopyFeedback("Acesso remoto bloqueado nesta máquina nas configurações.", isError: true);
            return;
        }
        CopyBothClicked?.Invoke(this, EventArgs.Empty);
    }

    private void BtnOpenChat_Click(object sender, RoutedEventArgs e)
        => OpenChatClicked?.Invoke(this, EventArgs.Empty);

    private void BtnHostDisconnect_Click(object sender, RoutedEventArgs e)
        => HostDisconnectClicked?.Invoke(this, EventArgs.Empty);

    private void BtnActiveEndSession_Click(object sender, RoutedEventArgs e)
        => ActiveEndSessionClicked?.Invoke(this, EventArgs.Empty);

    private void BtnConnectToRemote_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsStore.Load().RemotoBloqueado)
        {
            ShowConnectError("O acesso remoto está desabilitado nas configurações desta máquina.");
            return;
        }
        ConnectToRemoteClicked?.Invoke(this, EventArgs.Empty);
    }

    private void BorderConnectPassword_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!GlpiTechnicianSession.IsAuthenticated)
        {
            PromptGlpiLogin();
        }
    }

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

