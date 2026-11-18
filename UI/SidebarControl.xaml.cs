using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace SistecHub.UI;

/// <summary>
/// Barra lateral de navegação em WPF hospedada no MainForm via ElementHost.
/// </summary>
public partial class SidebarControl : System.Windows.Controls.UserControl
{
    sealed class NavItemVisuals
    {
        public required string Id { get; init; }
        public required WpfButton Button { get; init; }
        public required Border ActiveBar { get; init; }
        public required TextBlock IconBlock { get; init; }
        public required TextBlock TextBlock { get; init; }
    }

    readonly List<NavItemVisuals> _navItems = new();
    string _activeId = "home";
    bool _isDarkMode;

    static readonly SolidColorBrush BrushIdleBg = CreateFrozenBrush("#162033");
    static readonly SolidColorBrush BrushActiveBg = CreateFrozenBrush("#1E293B");
    static readonly SolidColorBrush BrushDarkIdleBg = CreateFrozenBrush("#18181B");
    static readonly SolidColorBrush BrushDarkActiveBg = CreateFrozenBrush("#242429");
    static readonly SolidColorBrush BrushActiveBorder = CreateFrozenBrush("#4F46E5");
    static readonly SolidColorBrush BrushIdleIcon = CreateFrozenBrush("#94A3B8");
    static readonly SolidColorBrush BrushActiveIcon = CreateFrozenBrush("#818CF8");
    static readonly SolidColorBrush BrushIdleText = CreateFrozenBrush("#CBD5E1");
    static readonly SolidColorBrush BrushActiveText = CreateFrozenBrush("#FFFFFF");

    public event Action<string>? PageSelected;

    public string EntityDisplayText
    {
        get => TxtEntityName.Text;
        set
        {
            var next = value ?? "";
            if (Dispatcher.CheckAccess())
            {
                TxtEntityName.Text = next;
                EntityBadge.ToolTip = string.IsNullOrWhiteSpace(next) ? null : next;
            }
            else
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TxtEntityName.Text = next;
                    EntityBadge.ToolTip = string.IsNullOrWhiteSpace(next) ? null : next;
                });
            }
        }
    }

    public SidebarControl()
    {
        InitializeComponent();
        LoadBrandLogo();
    }

    void LoadBrandLogo()
    {
        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "sidebar-logo.png");
            if (!File.Exists(logoPath))
                return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            ImgLogo.Source = bmp;
            ImgLogo.Visibility = Visibility.Visible;
            TxtBrandFallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Mantém o texto caso falhe o carregamento do arquivo de logo.
        }
    }

    public void SetMenuEntries(IEnumerable<(string Id, string MenuText)> entries)
    {
        NavItemsPanel.Children.Clear();
        _navItems.Clear();

        var navStyle = (Style)FindResource("NavButtonStyle");

        foreach (var entry in entries)
        {
            var grid = new Grid { Height = 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var activeBar = new Border
            {
                Width = 4,
                Height = 20,
                CornerRadius = new CornerRadius(2),
                Background = CreateFrozenBrush("#6366F1"),
                Margin = new Thickness(6, 0, 6, 0),
                Visibility = Visibility.Hidden,
            };
            Grid.SetColumn(activeBar, 0);

            var iconBlock = new TextBlock
            {
                Text = ResolveMenuGlyph(entry.Id, entry.MenuText),
                FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = BrushIdleIcon,
                VerticalAlignment = WpfVerticalAlignment.Center,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
            };
            Grid.SetColumn(iconBlock, 1);

            var textBlock = new TextBlock
            {
                Text = entry.MenuText,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = BrushIdleText,
                VerticalAlignment = WpfVerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(textBlock, 2);

            grid.Children.Add(activeBar);
            grid.Children.Add(iconBlock);
            grid.Children.Add(textBlock);

            var btn = new WpfButton
            {
                Style = navStyle,
                Content = grid,
                Tag = entry.Id,
            };

            string capturedId = entry.Id;
            btn.Click += (_, _) => PageSelected?.Invoke(capturedId);

            NavItemsPanel.Children.Add(btn);
            _navItems.Add(new NavItemVisuals
            {
                Id = entry.Id,
                Button = btn,
                ActiveBar = activeBar,
                IconBlock = iconBlock,
                TextBlock = textBlock,
            });
        }

        ApplySelectionVisuals(_activeId);
    }

    public void SetDarkMode(bool isDark)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetDarkMode(isDark));
            return;
        }

        _isDarkMode = isDark;
        if (isDark)
        {
            var bg = CreateFrozenBrush("#111113");
            Background = bg;
            Resources["SidebarBg"] = bg;
            Resources["HeaderBg"] = CreateFrozenBrush("#0C0C0E");
            Resources["DividerBrush"] = CreateFrozenBrush("#232328");
            Resources["DividerStrongBrush"] = CreateFrozenBrush("#2E2E34");
            Resources["NavHoverBrush"] = CreateFrozenBrush("#27272C");
            Resources["EntityBgBrush"] = CreateFrozenBrush("#18181B");
            Resources["EntityBorderBrush"] = CreateFrozenBrush("#2C2C32");
            Resources["TextMutedBrush"] = CreateFrozenBrush("#71717A");
        }
        else
        {
            var bg = CreateFrozenBrush("#0F172A");
            Background = bg;
            Resources["SidebarBg"] = bg;
            Resources["HeaderBg"] = CreateFrozenBrush("#0C1220");
            Resources["DividerBrush"] = CreateFrozenBrush("#1E293B");
            Resources["DividerStrongBrush"] = CreateFrozenBrush("#334155");
            Resources["NavHoverBrush"] = CreateFrozenBrush("#25344D");
            Resources["EntityBgBrush"] = CreateFrozenBrush("#162033");
            Resources["EntityBorderBrush"] = CreateFrozenBrush("#22304A");
            Resources["TextMutedBrush"] = CreateFrozenBrush("#64748B");
        }

        ApplySelectionVisuals(_activeId);
    }

    public void SetActivePage(string id)
    {
        _activeId = id ?? "home";
        if (Dispatcher.CheckAccess())
            ApplySelectionVisuals(_activeId);
        else
            Dispatcher.BeginInvoke(() => ApplySelectionVisuals(_activeId));
    }

    void ApplySelectionVisuals(string activeId)
    {
        var idleBg = _isDarkMode ? BrushDarkIdleBg : BrushIdleBg;
        var activeBg = _isDarkMode ? BrushDarkActiveBg : BrushActiveBg;

        foreach (var item in _navItems)
        {
            bool selected = string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase);
            item.ActiveBar.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            item.Button.Background = selected ? activeBg : idleBg;
            item.Button.BorderBrush = selected ? BrushActiveBorder : System.Windows.Media.Brushes.Transparent;
            item.IconBlock.Foreground = selected ? BrushActiveIcon : BrushIdleIcon;
            item.TextBlock.Foreground = selected ? BrushActiveText : BrushIdleText;
            item.TextBlock.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        bool isSettings = string.Equals(activeId, "settings", StringComparison.OrdinalIgnoreCase);
        SettingsActiveBar.Visibility = isSettings ? Visibility.Visible : Visibility.Hidden;
        BtnSettings.Background = isSettings ? activeBg : idleBg;
        BtnSettings.BorderBrush = isSettings ? BrushActiveBorder : System.Windows.Media.Brushes.Transparent;
        SettingsIcon.Foreground = isSettings ? BrushActiveIcon : BrushIdleIcon;
        SettingsLabel.Foreground = isSettings ? BrushActiveText : BrushIdleText;
        SettingsLabel.FontWeight = isSettings ? FontWeights.SemiBold : FontWeights.Normal;
    }

    void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        PageSelected?.Invoke("settings");
    }

    static string ResolveMenuGlyph(string id, string menuText)
    {
        var key = (id ?? "").Trim().ToLowerInvariant();
        if (key == "home")
            return "\uE80F";
        if (key.Contains("remoto"))
            return "\uE7F4";
        if (key.Contains("glpi") || key.Contains("chamado"))
            return "\uE8F2";
        if (key.Contains("invent"))
            return "\uE950";

        var text = (menuText ?? "").Trim().ToLowerInvariant();
        if (text.Contains("início") || text.Contains("inicio"))
            return "\uE80F";
        if (text.Contains("remoto"))
            return "\uE7F4";
        if (text.Contains("chamado"))
            return "\uE8F2";
        if (text.Contains("invent"))
            return "\uE950";

        return "\uE8A5";
    }

    static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

