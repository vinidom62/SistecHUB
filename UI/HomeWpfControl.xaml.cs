using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using SistecHub.Core;
using SistecHub.Modulos.AcessoRemoto;

namespace SistecHub.UI;

public sealed partial class HomeWpfControl : System.Windows.Controls.UserControl
{
    static readonly SolidColorBrush BrushGreenBg = CreateFrozenBrush("#DCFCE7");
    static readonly SolidColorBrush BrushGreenFg = CreateFrozenBrush("#166534");
    static readonly SolidColorBrush BrushIndigoBg = CreateFrozenBrush("#EEF2FF");
    static readonly SolidColorBrush BrushIndigoFg = CreateFrozenBrush("#4F46E5");

    static readonly SolidColorBrush LightPageBg = CreateFrozenBrush("#F8FAFC");
    static readonly SolidColorBrush LightTitleFg = CreateFrozenBrush("#0F172A");
    static readonly SolidColorBrush LightMutedFg = CreateFrozenBrush("#64748B");
    static readonly SolidColorBrush LightFooterFg = CreateFrozenBrush("#94A3B8");
    static readonly SolidColorBrush LightTagBg = CreateFrozenBrush("#F1F5F9");
    static readonly SolidColorBrush LightToggleIconFg = CreateFrozenBrush("#475569");
    static readonly SolidColorBrush LightTicketGreen = CreateFrozenBrush("#16A34A");
    static readonly SolidColorBrush LightRemoteIndigo = CreateFrozenBrush("#6366F1");

    // Paleta preto acinzentado
    static readonly SolidColorBrush DarkPageBg = CreateFrozenBrush("#141416");
    static readonly SolidColorBrush DarkTitleFg = CreateFrozenBrush("#F4F4F5");
    static readonly SolidColorBrush DarkMutedFg = CreateFrozenBrush("#A1A1AA");
    static readonly SolidColorBrush DarkFooterFg = CreateFrozenBrush("#71717A");
    static readonly SolidColorBrush DarkTagBg = CreateFrozenBrush("#27272C");
    static readonly SolidColorBrush DarkTagFg = CreateFrozenBrush("#D4D4D8");
    static readonly SolidColorBrush DarkToggleIconFg = CreateFrozenBrush("#FBBF24");
    static readonly SolidColorBrush DarkTicketIconBg = CreateFrozenBrush("#182E22");
    static readonly SolidColorBrush DarkTicketGreen = CreateFrozenBrush("#4ADE80");
    static readonly SolidColorBrush DarkRemoteIconBg = CreateFrozenBrush("#232336");
    static readonly SolidColorBrush DarkRemoteIndigo = CreateFrozenBrush("#818CF8");
    static readonly SolidColorBrush DarkBadgeBg = CreateFrozenBrush("#222230");
    static readonly SolidColorBrush DarkBadgeFg = CreateFrozenBrush("#A5B4FC");
    static readonly SolidColorBrush DarkBlockedBg = CreateFrozenBrush("#3B1518");
    static readonly SolidColorBrush DarkBlockedFg = CreateFrozenBrush("#FCA5A5");

    string _currentId = "";
    string _formattedId = "";
    bool _isDarkMode;

    public event Action<string>? NavigateRequested;
    public event Action<bool>? DarkModeChanged;

    public bool IsDarkMode => _isDarkMode;

    public HomeWpfControl()
    {
        InitializeComponent();
        AppThemeManager.Initialize();
        _isDarkMode = AppThemeManager.IsDarkMode;
        ApplyTheme();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateGreeting();

        AcessoRemotoAgentService.Start();
        AcessoRemotoAgentService.StateChanged += OnRemoteStateChanged;

        UpdateRemoteId();
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AcessoRemotoAgentService.StateChanged -= OnRemoteStateChanged;
    }

    void BtnToggleDarkMode_Click(object sender, RoutedEventArgs e)
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
        UpdateRemoteId();
        AppThemeManager.SetDarkMode(_isDarkMode);
        DarkModeChanged?.Invoke(_isDarkMode);
    }

    void ApplyTheme()
    {
        var tag = _isDarkMode ? "Dark" : "Light";
        BtnToggleDarkMode.Tag = tag;
        BtnOpenTicket.Tag = tag;
        BtnCopyAndOpenRemote.Tag = tag;

        if (_isDarkMode)
        {
            Background = DarkPageBg;
            RootGrid.Background = DarkPageBg;

            IconDarkMode.Foreground = DarkToggleIconFg;
            BtnToggleDarkMode.ToolTip = "Desativar modo noturno";

            TxtGreeting.Foreground = DarkTitleFg;
            TxtSubtitle.Foreground = DarkMutedFg;
            TxtFooterTip.Foreground = DarkFooterFg;

            BdTicketIcon.Background = DarkTicketIconBg;
            IconTicketCard.Foreground = DarkTicketGreen;
            BdTicketTag.Background = DarkTagBg;
            TxtTicketTag.Foreground = DarkTagFg;
            TxtTicketTitle.Foreground = DarkTitleFg;
            TxtTicketDesc.Foreground = DarkMutedFg;
            TxtTicketAction.Foreground = DarkTicketGreen;
            IconTicketArrow.Foreground = DarkTicketGreen;

            BdRemoteIcon.Background = DarkRemoteIconBg;
            IconRemoteCard.Foreground = DarkRemoteIndigo;
            TxtRemoteTitle.Foreground = DarkTitleFg;
            TxtRemoteDesc.Foreground = DarkMutedFg;
            TxtRemoteActionText.Foreground = DarkRemoteIndigo;
            IconRemoteActionArrow.Foreground = DarkRemoteIndigo;
        }
        else
        {
            Background = LightPageBg;
            RootGrid.Background = LightPageBg;

            IconDarkMode.Foreground = LightToggleIconFg;
            BtnToggleDarkMode.ToolTip = "Ativar modo noturno";

            TxtGreeting.Foreground = LightTitleFg;
            TxtSubtitle.Foreground = LightMutedFg;
            TxtFooterTip.Foreground = LightFooterFg;

            BdTicketIcon.Background = BrushGreenBg;
            IconTicketCard.Foreground = LightTicketGreen;
            BdTicketTag.Background = LightTagBg;
            TxtTicketTag.Foreground = LightMutedFg;
            TxtTicketTitle.Foreground = LightTitleFg;
            TxtTicketDesc.Foreground = LightMutedFg;
            TxtTicketAction.Foreground = LightTicketGreen;
            IconTicketArrow.Foreground = LightTicketGreen;

            BdRemoteIcon.Background = BrushIndigoBg;
            IconRemoteCard.Foreground = LightRemoteIndigo;
            TxtRemoteTitle.Foreground = LightTitleFg;
            TxtRemoteDesc.Foreground = LightMutedFg;
            TxtRemoteActionText.Foreground = BrushIndigoFg;
            IconRemoteActionArrow.Foreground = BrushIndigoFg;
        }
    }

    void OnRemoteStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateRemoteId);
            return;
        }

        UpdateRemoteId();
    }

    void UpdateRemoteId()
    {
        var settings = AppSettingsStore.Load();
        if (settings.RemotoBloqueado)
        {
            _currentId = "";
            _formattedId = "";
            TxtQuickIdBadge.Text = "ACESSO BLOQUEADO";
            BdQuickIdBadge.Background = _isDarkMode ? DarkBlockedBg : CreateFrozenBrush("#FEE2E2");
            TxtQuickIdBadge.Foreground = _isDarkMode ? DarkBlockedFg : CreateFrozenBrush("#DC2626");
            return;
        }

        var snap = AcessoRemotoAgentService.GetSnapshot();
        var id = !string.IsNullOrWhiteSpace(snap.AssignedId)
            ? snap.AssignedId
            : settings.RemotoId;

        var formatted = !string.IsNullOrWhiteSpace(snap.FormattedId)
            ? snap.FormattedId
            : AcessoRemotoAgentService.FormatId(id);

        _currentId = !string.IsNullOrWhiteSpace(id) ? id : "";
        _formattedId = formatted;

        if (!string.IsNullOrWhiteSpace(formatted) && !formatted.Contains("---"))
        {
            TxtQuickIdBadge.Text = $"ID: {formatted}";
            BdQuickIdBadge.Background = _isDarkMode ? DarkBadgeBg : BrushIndigoBg;
            TxtQuickIdBadge.Foreground = _isDarkMode ? DarkBadgeFg : BrushIndigoFg;
        }
        else
        {
            TxtQuickIdBadge.Text = "ACESSO REMOTO";
            BdQuickIdBadge.Background = _isDarkMode ? DarkBadgeBg : BrushIndigoBg;
            TxtQuickIdBadge.Foreground = _isDarkMode ? DarkBadgeFg : BrushIndigoFg;
        }
    }

    void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour switch
        {
            < 12 => "Bom dia",
            < 18 => "Boa tarde",
            _ => "Boa noite"
        };

        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            if (userName.Contains('.'))
                userName = userName.Split('.')[0];
            else if (userName.Contains('_'))
                userName = userName.Split('_')[0];

            if (userName.Length > 0 && char.IsLower(userName[0]))
                userName = char.ToUpper(userName[0]) + userName[1..];

            TxtGreeting.Text = $"{greeting}, {userName}!";
        }
        else
        {
            TxtGreeting.Text = $"{greeting}!";
        }
    }

    void BtnOpenTicket_Click(object sender, RoutedEventArgs e)
    {
        NavigateRequested?.Invoke("glpi:novo");
    }

    async void BtnCopyAndOpenRemote_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        if (settings.RemotoBloqueado)
        {
            TxtRemoteActionText.Text = "Bloqueado nas configurações";
            TxtRemoteActionText.Foreground = CreateFrozenBrush("#DC2626");
            IconRemoteActionArrow.Foreground = CreateFrozenBrush("#DC2626");
            IconRemoteCard.Foreground = CreateFrozenBrush("#DC2626");
            BdRemoteIcon.Background = CreateFrozenBrush("#FEE2E2");
            BdQuickIdBadge.Background = CreateFrozenBrush("#FEE2E2");
            TxtQuickIdBadge.Foreground = CreateFrozenBrush("#DC2626");

            await Task.Delay(260);

            NavigateRequested?.Invoke("settings");
            return;
        }

        var textToCopy = !string.IsNullOrWhiteSpace(_formattedId) && !_formattedId.Contains("---")
            ? _formattedId
            : _currentId;

        if (!string.IsNullOrWhiteSpace(textToCopy))
        {
            try
            {
                System.Windows.Clipboard.SetText(textToCopy);
            }
            catch (ExternalException)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(textToCopy, true);
                }
                catch
                {
                    // Ignora bloqueios temporários de clipboard
                }
            }
        }

        // Feedback visual imediato antes da navegação
        TxtRemoteActionText.Text = "✓ ID copiado! A abrir...";
        TxtRemoteActionText.Foreground = BrushGreenFg;
        IconRemoteActionArrow.Text = "\uE73E"; // Checkmark
        IconRemoteActionArrow.Foreground = BrushGreenFg;
        IconRemoteCard.Foreground = BrushGreenFg;
        BdRemoteIcon.Background = BrushGreenBg;
        BdQuickIdBadge.Background = BrushGreenBg;
        TxtQuickIdBadge.Foreground = BrushGreenFg;

        await Task.Delay(260);

        NavigateRequested?.Invoke("acesso-remoto");
    }

    static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        brush.Freeze();
        return brush;
    }
}
