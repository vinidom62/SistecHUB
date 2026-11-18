using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SistecHub.UI;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using GradientStop = System.Windows.Media.GradientStop;
using ImageBrush = System.Windows.Media.ImageBrush;
using Stretch = System.Windows.Media.Stretch;

namespace SistecHub.Modulos.AcessoRemoto;

public sealed partial class AcessoRemotoChatWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? SendRequested;
    public event EventHandler? DisconnectRequested;

    readonly ObservableCollection<ChatEntryViewModel> _messages = [];

    public AcessoRemotoChatWpfControl()
    {
        InitializeComponent();
        ChatItemsControl.ItemsSource = _messages;
    }

    public string InputText
    {
        get => (TxtChatInput.Text ?? "").Trim();
        set
        {
            TxtChatInput.Text = value ?? "";
            UpdateInputHint();
        }
    }

    public void FocusInput() => TxtChatInput.Focus();

    public void SetSendEnabled(bool enabled) => BtnSend.IsEnabled = enabled;

    public void SetSessionEnded()
    {
        StatusDot.Fill = CreateFrozenBrush(239, 68, 68); // Red
        BtnDisconnect.IsEnabled = false;
        TxtChatInput.IsEnabled = false;
        BtnSend.IsEnabled = false;
        HintChatInput.Text = "A sessão foi encerrada.";
        PingDot.Fill = CreateFrozenBrush(239, 68, 68);
        PingBadge.Background = AppThemeManager.DangerSoftBg;
        PingBadge.BorderBrush = AppThemeManager.DangerSoftBorder;
        TxtPingValue.Text = "Encerrado";
        TxtPingValue.Foreground = AppThemeManager.DangerText;
        PingBadge.ToolTip = "Atendimento remoto encerrado";
        UpdateInputHint();
    }

    public void SetTechnician(string? name, string? avatarBase64OrPath)
    {
        var display = string.IsNullOrWhiteSpace(name) ? "Técnico de Suporte" : name.Trim();
        TxtTechName.Text = display;
        TxtAvatarInitials.Text = ExtractInitials(display);

        var image = LoadAvatar(avatarBase64OrPath);
        if (image != null)
        {
            AvatarImage.Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            AvatarImage.Visibility = Visibility.Visible;
            TxtAvatarInitials.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvatarImage.Visibility = Visibility.Collapsed;
            TxtAvatarInitials.Visibility = Visibility.Visible;
        }
    }

    public void SetPing(long ms)
    {
        TxtPingValue.Text = $"{ms} ms";
        if (ms < 60)
        {
            PingDot.Fill = CreateFrozenBrush(16, 185, 129); // #10B981 Emerald
            PingBadge.Background = AppThemeManager.SuccessSoftBg;
            PingBadge.BorderBrush = AppThemeManager.SuccessSoftBorder;
            TxtPingValue.Foreground = AppThemeManager.SuccessText;
            PingBadge.ToolTip = $"Conexão Excelente: {ms} ms de latência";
        }
        else if (ms < 150)
        {
            PingDot.Fill = CreateFrozenBrush(245, 158, 11); // #F59E0B Amber
            PingBadge.Background = AppThemeManager.WarningSoftBg;
            PingBadge.BorderBrush = AppThemeManager.WarningSoftBorder;
            TxtPingValue.Foreground = AppThemeManager.WarningText;
            PingBadge.ToolTip = $"Conexão Boa: {ms} ms de latência";
        }
        else
        {
            PingDot.Fill = CreateFrozenBrush(239, 68, 68); // #EF4444 Red
            PingBadge.Background = AppThemeManager.DangerSoftBg;
            PingBadge.BorderBrush = AppThemeManager.DangerSoftBorder;
            TxtPingValue.Foreground = AppThemeManager.DangerText;
            PingBadge.ToolTip = $"Latência Alta: {ms} ms de latência";
        }
    }

    public void ClearMessages()
    {
        _messages.Clear();
    }

    internal void AddMessage(AcessoRemotoChatEntry entry)
    {
        if (AcessoRemotoClipboard.IsAutomatedFileTransferChatMessage(entry.Text))
            return;

        _messages.Add(new ChatEntryViewModel(entry));
        ChatScrollViewer.ScrollToEnd();
    }

    private void TxtChatInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateInputHint();
    }

    void UpdateInputHint()
    {
        HintChatInput.Visibility = string.IsNullOrEmpty(TxtChatInput.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)
        => SendRequested?.Invoke(this, EventArgs.Empty);

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        => DisconnectRequested?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    static BitmapImage? LoadAvatar(string? avatarBase64OrPath)
    {
        if (string.IsNullOrWhiteSpace(avatarBase64OrPath))
            return null;

        try
        {
            var raw = avatarBase64OrPath.Trim();
            byte[] bytes;

            if (File.Exists(raw))
            {
                bytes = File.ReadAllBytes(raw);
            }
            else
            {
                int comma = raw.IndexOf(',', StringComparison.Ordinal);
                if (comma >= 0 && raw.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    raw = raw[(comma + 1)..];
                bytes = Convert.FromBase64String(raw);
            }

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    static string ExtractInitials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "TC";
        if (parts.Length == 1)
            return parts[0].Length <= 2 ? parts[0].ToUpperInvariant() : parts[0][..2].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    public sealed class ChatEntryViewModel
    {
        public string Text { get; }
        public string Time { get; }
        public string HeaderText { get; }
        public string FormattedText { get; }
        public Visibility SystemEventVisibility { get; }
        public Visibility MessageVisibility { get; }
        public System.Windows.HorizontalAlignment BubbleAlignment { get; }
        public System.Windows.CornerRadius BubbleCornerRadius { get; }
        public Thickness BubbleBorderThickness { get; }
        public MediaBrush BubbleBackground { get; }
        public MediaBrush BubbleBorderBrush { get; }
        public MediaBrush SenderForeground { get; }
        public MediaBrush TextForeground { get; }
        public MediaBrush TimeForeground { get; }
        public double ShadowOpacity { get; }

        internal ChatEntryViewModel(AcessoRemotoChatEntry entry)
        {
            Text = entry.Text ?? "";
            Time = entry.Time ?? "";
            HeaderText = entry.IsFromTechnician ? (string.IsNullOrWhiteSpace(entry.Sender) ? "Técnico" : entry.Sender) : "Você";
            FormattedText = $"[{entry.Time}] {entry.Text}";

            if (entry.IsSystemEvent)
            {
                SystemEventVisibility = Visibility.Visible;
                MessageVisibility = Visibility.Collapsed;
                BubbleAlignment = System.Windows.HorizontalAlignment.Center;
                BubbleCornerRadius = new System.Windows.CornerRadius(10);
                BubbleBorderThickness = new Thickness(0);
                BubbleBackground = AppThemeManager.SubtleBg;
                BubbleBorderBrush = AppThemeManager.BorderSubtle;
                SenderForeground = AppThemeManager.TextMuted;
                TextForeground = AppThemeManager.TextMuted;
                TimeForeground = AppThemeManager.TextSubtle;
                ShadowOpacity = 0;
            }
            else
            {
                SystemEventVisibility = Visibility.Collapsed;
                MessageVisibility = Visibility.Visible;

                if (entry.IsFromTechnician)
                {
                    BubbleAlignment = System.Windows.HorizontalAlignment.Left;
                    BubbleCornerRadius = new System.Windows.CornerRadius(14, 14, 14, 2);
                    BubbleBorderThickness = new Thickness(1);
                    BubbleBackground = AppThemeManager.CardBg;
                    BubbleBorderBrush = AppThemeManager.BorderSubtle;
                    SenderForeground = AppThemeManager.IndigoText;
                    TextForeground = AppThemeManager.TextPrimary;
                    TimeForeground = AppThemeManager.TextSubtle;
                    ShadowOpacity = 0.05;
                }
                else
                {
                    BubbleAlignment = System.Windows.HorizontalAlignment.Right;
                    BubbleCornerRadius = new System.Windows.CornerRadius(14, 14, 2, 14);
                    BubbleBorderThickness = new Thickness(0);

                    var grad = new MediaLinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    grad.GradientStops.Add(new GradientStop(MediaColor.FromRgb(99, 102, 241), 0));
                    grad.GradientStops.Add(new GradientStop(MediaColor.FromRgb(67, 56, 202), 1));
                    grad.Freeze();

                    BubbleBackground = grad;
                    BubbleBorderBrush = CreateFrozenBrush(99, 102, 241);
                    SenderForeground = CreateFrozenBrush(224, 231, 255); // #E0E7FF
                    TextForeground = CreateFrozenBrush(255, 255, 255);
                    TimeForeground = CreateFrozenBrush(199, 210, 254); // #C7D2FE
                    ShadowOpacity = 0.12;
                }
            }
        }
    }
}
