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

    string _currentId = "";
    string _formattedId = "";

    public event Action<string>? NavigateRequested;

    public HomeWpfControl()
    {
        InitializeComponent();
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
            BdQuickIdBadge.Background = CreateFrozenBrush("#FEE2E2");
            TxtQuickIdBadge.Foreground = CreateFrozenBrush("#DC2626");
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
            BdQuickIdBadge.Background = BrushIndigoBg;
            TxtQuickIdBadge.Foreground = BrushIndigoFg;
        }
        else
        {
            TxtQuickIdBadge.Text = "ACESSO REMOTO";
            BdQuickIdBadge.Background = BrushIndigoBg;
            TxtQuickIdBadge.Foreground = BrushIndigoFg;
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
