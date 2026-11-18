using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using SistecHub.Core;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfWindow = System.Windows.Window;

namespace SistecHub.UI;

/// <summary>
/// Gerenciador de tema nativo (Claro / Escuro) baseado em recursos globais WPF (DynamicResource).
/// </summary>
internal static class AppThemeManager
{
    static bool _initialized;
    static bool _isDarkMode;

    static readonly List<WeakReference<WpfWindow>> TrackedWindows = [];
    static readonly List<WeakReference<WpfUserControl>> TrackedUserControls = [];

    // Paleta Clara (congelada)
    static readonly SolidColorBrush LightPageBg = CreateFrozenBrush("#F8FAFC");
    static readonly SolidColorBrush LightCardBg = CreateFrozenBrush("#FFFFFF");
    static readonly SolidColorBrush LightCardAltBg = CreateFrozenBrush("#FAFAFE");
    static readonly SolidColorBrush LightSurfaceBg = CreateFrozenBrush("#F8FAFC");
    static readonly SolidColorBrush LightSubtleBg = CreateFrozenBrush("#F1F5F9");
    static readonly SolidColorBrush LightStrongBg = CreateFrozenBrush("#E2E8F0");
    static readonly SolidColorBrush LightPressedBg = CreateFrozenBrush("#CBD5E1");
    static readonly SolidColorBrush LightBorderSubtle = CreateFrozenBrush("#E2E8F0");
    static readonly SolidColorBrush LightBorderStrong = CreateFrozenBrush("#CBD5E1");
    static readonly SolidColorBrush LightTextPrimary = CreateFrozenBrush("#0F172A");
    static readonly SolidColorBrush LightTextHeading = CreateFrozenBrush("#1E293B");
    static readonly SolidColorBrush LightTextSecondary = CreateFrozenBrush("#334155");
    static readonly SolidColorBrush LightTextBody = CreateFrozenBrush("#475569");
    static readonly SolidColorBrush LightTextMuted = CreateFrozenBrush("#64748B");
    static readonly SolidColorBrush LightTextSubtle = CreateFrozenBrush("#94A3B8");
    static readonly SolidColorBrush LightIndigoSoftBg = CreateFrozenBrush("#EEF2FF");
    static readonly SolidColorBrush LightIndigoHoverBg = CreateFrozenBrush("#E0E7FF");
    static readonly SolidColorBrush LightIndigoPressedBg = CreateFrozenBrush("#C7D2FE");
    static readonly SolidColorBrush LightIndigoSoftBorder = CreateFrozenBrush("#C7D2FE");
    static readonly SolidColorBrush LightIndigoText = CreateFrozenBrush("#4F46E5");
    static readonly SolidColorBrush LightSuccessSoftBg = CreateFrozenBrush("#DCFCE7");
    static readonly SolidColorBrush LightSuccessHoverBg = CreateFrozenBrush("#BBF7D0");
    static readonly SolidColorBrush LightSuccessSoftBorder = CreateFrozenBrush("#86EFAC");
    static readonly SolidColorBrush LightSuccessText = CreateFrozenBrush("#166534");
    static readonly SolidColorBrush LightDangerSoftBg = CreateFrozenBrush("#FEE2E2");
    static readonly SolidColorBrush LightDangerHoverBg = CreateFrozenBrush("#FECACA");
    static readonly SolidColorBrush LightDangerSoftBorder = CreateFrozenBrush("#FECACA");
    static readonly SolidColorBrush LightDangerText = CreateFrozenBrush("#DC2626");
    static readonly SolidColorBrush LightWarningSoftBg = CreateFrozenBrush("#FEF3C7");
    static readonly SolidColorBrush LightWarningSoftBorder = CreateFrozenBrush("#FDE68A");
    static readonly SolidColorBrush LightWarningText = CreateFrozenBrush("#D97706");
    static readonly SolidColorBrush LightPurpleSoftBg = CreateFrozenBrush("#FAF5FF");
    static readonly SolidColorBrush LightPurpleText = CreateFrozenBrush("#7C3AED");
    static readonly SolidColorBrush LightBlueSoftBg = CreateFrozenBrush("#EFF6FF");
    static readonly SolidColorBrush LightBlueText = CreateFrozenBrush("#2563EB");
    static readonly RadialGradientBrush LightPageGradient = CreateFrozenRadialGradient("#F8FAFC", "#E8EEF5");

    // Paleta Escura preto-acinzentada (congelada)
    static readonly SolidColorBrush DarkPageBg = CreateFrozenBrush("#141416");
    static readonly SolidColorBrush DarkCardBg = CreateFrozenBrush("#1C1C20");
    static readonly SolidColorBrush DarkCardAltBg = CreateFrozenBrush("#1C1C20");
    static readonly SolidColorBrush DarkSurfaceBg = CreateFrozenBrush("#242429");
    static readonly SolidColorBrush DarkSubtleBg = CreateFrozenBrush("#242429");
    static readonly SolidColorBrush DarkStrongBg = CreateFrozenBrush("#2E2E36");
    static readonly SolidColorBrush DarkPressedBg = CreateFrozenBrush("#383842");
    static readonly SolidColorBrush DarkBorderSubtle = CreateFrozenBrush("#2C2C32");
    static readonly SolidColorBrush DarkBorderStrong = CreateFrozenBrush("#383842");
    static readonly SolidColorBrush DarkTextPrimary = CreateFrozenBrush("#F4F4F5");
    static readonly SolidColorBrush DarkTextHeading = CreateFrozenBrush("#F4F4F5");
    static readonly SolidColorBrush DarkTextSecondary = CreateFrozenBrush("#D4D4D8");
    static readonly SolidColorBrush DarkTextBody = CreateFrozenBrush("#D4D4D8");
    static readonly SolidColorBrush DarkTextMuted = CreateFrozenBrush("#A1A1AA");
    static readonly SolidColorBrush DarkTextSubtle = CreateFrozenBrush("#71717A");
    static readonly SolidColorBrush DarkIndigoSoftBg = CreateFrozenBrush("#232336");
    static readonly SolidColorBrush DarkIndigoHoverBg = CreateFrozenBrush("#2D2D46");
    static readonly SolidColorBrush DarkIndigoPressedBg = CreateFrozenBrush("#3730A3");
    static readonly SolidColorBrush DarkIndigoSoftBorder = CreateFrozenBrush("#4338CA");
    static readonly SolidColorBrush DarkIndigoText = CreateFrozenBrush("#818CF8");
    static readonly SolidColorBrush DarkSuccessSoftBg = CreateFrozenBrush("#182E22");
    static readonly SolidColorBrush DarkSuccessHoverBg = CreateFrozenBrush("#1E3A2B");
    static readonly SolidColorBrush DarkSuccessSoftBorder = CreateFrozenBrush("#166534");
    static readonly SolidColorBrush DarkSuccessText = CreateFrozenBrush("#4ADE80");
    static readonly SolidColorBrush DarkDangerSoftBg = CreateFrozenBrush("#3B1518");
    static readonly SolidColorBrush DarkDangerHoverBg = CreateFrozenBrush("#4C1D21");
    static readonly SolidColorBrush DarkDangerSoftBorder = CreateFrozenBrush("#7F1D1D");
    static readonly SolidColorBrush DarkDangerText = CreateFrozenBrush("#FCA5A5");
    static readonly SolidColorBrush DarkWarningSoftBg = CreateFrozenBrush("#332814");
    static readonly SolidColorBrush DarkWarningSoftBorder = CreateFrozenBrush("#854D0E");
    static readonly SolidColorBrush DarkWarningText = CreateFrozenBrush("#FBBF24");
    static readonly SolidColorBrush DarkPurpleSoftBg = CreateFrozenBrush("#2A1E36");
    static readonly SolidColorBrush DarkPurpleText = CreateFrozenBrush("#C084FC");
    static readonly SolidColorBrush DarkBlueSoftBg = CreateFrozenBrush("#1A2639");
    static readonly SolidColorBrush DarkBlueText = CreateFrozenBrush("#60A5FA");
    static readonly RadialGradientBrush DarkPageGradient = CreateFrozenRadialGradient("#141416", "#0E0E10");

    // Propriedades semânticas dinâmicas para uso em code-behind
    public static SolidColorBrush PageBg => _isDarkMode ? DarkPageBg : LightPageBg;
    public static SolidColorBrush CardBg => _isDarkMode ? DarkCardBg : LightCardBg;
    public static SolidColorBrush CardAltBg => _isDarkMode ? DarkCardAltBg : LightCardAltBg;
    public static SolidColorBrush SurfaceBg => _isDarkMode ? DarkSurfaceBg : LightSurfaceBg;
    public static SolidColorBrush SubtleBg => _isDarkMode ? DarkSubtleBg : LightSubtleBg;
    public static SolidColorBrush StrongBg => _isDarkMode ? DarkStrongBg : LightStrongBg;
    public static SolidColorBrush PressedBg => _isDarkMode ? DarkPressedBg : LightPressedBg;
    public static SolidColorBrush BorderSubtle => _isDarkMode ? DarkBorderSubtle : LightBorderSubtle;
    public static SolidColorBrush BorderStrong => _isDarkMode ? DarkBorderStrong : LightBorderStrong;
    public static SolidColorBrush TextPrimary => _isDarkMode ? DarkTextPrimary : LightTextPrimary;
    public static SolidColorBrush TextHeading => _isDarkMode ? DarkTextHeading : LightTextHeading;
    public static SolidColorBrush TextSecondary => _isDarkMode ? DarkTextSecondary : LightTextSecondary;
    public static SolidColorBrush TextBody => _isDarkMode ? DarkTextBody : LightTextBody;
    public static SolidColorBrush TextMuted => _isDarkMode ? DarkTextMuted : LightTextMuted;
    public static SolidColorBrush TextSubtle => _isDarkMode ? DarkTextSubtle : LightTextSubtle;
    public static SolidColorBrush IndigoSoftBg => _isDarkMode ? DarkIndigoSoftBg : LightIndigoSoftBg;
    public static SolidColorBrush IndigoHoverBg => _isDarkMode ? DarkIndigoHoverBg : LightIndigoHoverBg;
    public static SolidColorBrush IndigoPressedBg => _isDarkMode ? DarkIndigoPressedBg : LightIndigoPressedBg;
    public static SolidColorBrush IndigoSoftBorder => _isDarkMode ? DarkIndigoSoftBorder : LightIndigoSoftBorder;
    public static SolidColorBrush IndigoText => _isDarkMode ? DarkIndigoText : LightIndigoText;
    public static SolidColorBrush SuccessSoftBg => _isDarkMode ? DarkSuccessSoftBg : LightSuccessSoftBg;
    public static SolidColorBrush SuccessHoverBg => _isDarkMode ? DarkSuccessHoverBg : LightSuccessHoverBg;
    public static SolidColorBrush SuccessSoftBorder => _isDarkMode ? DarkSuccessSoftBorder : LightSuccessSoftBorder;
    public static SolidColorBrush SuccessText => _isDarkMode ? DarkSuccessText : LightSuccessText;
    public static SolidColorBrush DangerSoftBg => _isDarkMode ? DarkDangerSoftBg : LightDangerSoftBg;
    public static SolidColorBrush DangerHoverBg => _isDarkMode ? DarkDangerHoverBg : LightDangerHoverBg;
    public static SolidColorBrush DangerSoftBorder => _isDarkMode ? DarkDangerSoftBorder : LightDangerSoftBorder;
    public static SolidColorBrush DangerText => _isDarkMode ? DarkDangerText : LightDangerText;
    public static SolidColorBrush WarningSoftBg => _isDarkMode ? DarkWarningSoftBg : LightWarningSoftBg;
    public static SolidColorBrush WarningSoftBorder => _isDarkMode ? DarkWarningSoftBorder : LightWarningSoftBorder;
    public static SolidColorBrush WarningText => _isDarkMode ? DarkWarningText : LightWarningText;
    public static SolidColorBrush PurpleSoftBg => _isDarkMode ? DarkPurpleSoftBg : LightPurpleSoftBg;
    public static SolidColorBrush PurpleText => _isDarkMode ? DarkPurpleText : LightPurpleText;
    public static SolidColorBrush BlueSoftBg => _isDarkMode ? DarkBlueSoftBg : LightBlueSoftBg;
    public static SolidColorBrush BlueText => _isDarkMode ? DarkBlueText : LightBlueText;
    public static RadialGradientBrush PageGradient => _isDarkMode ? DarkPageGradient : LightPageGradient;

    static Style? _nativeComboBoxStyle;
    static Style? _nativeComboBoxItemStyle;

    public static bool IsDarkMode => _isDarkMode;

    public static event Action<bool>? ThemeChanged;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            _isDarkMode = AppSettingsStore.Load().ModoNoturno;
        }
        catch
        {
            _isDarkMode = false;
        }

        EnsureWpfApplicationResources();

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyFrameworkElementReady));

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnAnyFrameworkElementReady));
    }

    public static void SetDarkMode(bool isDark)
    {
        Initialize();
        if (_isDarkMode == isDark)
            return;

        _isDarkMode = isDark;

        try
        {
            var settings = AppSettingsStore.Load();
            settings.ModoNoturno = isDark;
            AppSettingsStore.Save(settings);
        }
        catch
        {
            // Mantém o tema em memória se a gravação falhar.
        }

        if (WpfApplication.Current is not null)
            PopulateResourceDictionary(WpfApplication.Current.Resources);

        ReapplyToTrackedRoots(isDark);
        ThemeChanged?.Invoke(isDark);
    }

    static void EnsureWpfApplicationResources()
    {
        try
        {
            if (WpfApplication.Current is null)
            {
                _ = new WpfApplication
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
            }

            BuildNativeComboBoxStyles();

            if (WpfApplication.Current is not null)
                PopulateResourceDictionary(WpfApplication.Current.Resources);
        }
        catch
        {
        }
    }

    static void BuildNativeComboBoxStyles()
    {
        if (_nativeComboBoxStyle is not null)
            return;

        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="NativeComboBoxItemStyle" TargetType="ComboBoxItem">
                    <Setter Property="Foreground" Value="{DynamicResource Theme.TextPrimary}"/>
                    <Setter Property="Background" Value="Transparent"/>
                    <Setter Property="FontSize" Value="13"/>
                    <Setter Property="Cursor" Value="Hand"/>
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ComboBoxItem">
                                <Border x:Name="Bd" Background="{TemplateBinding Background}" CornerRadius="6" Padding="10,7" Margin="2,1">
                                    <ContentPresenter VerticalAlignment="Center"/>
                                </Border>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                    <Style.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter Property="Background" Value="{DynamicResource Theme.SubtleBg}"/>
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter Property="Background" Value="{DynamicResource Theme.IndigoSoftBg}"/>
                            <Setter Property="Foreground" Value="{DynamicResource Theme.IndigoText}"/>
                            <Setter Property="FontWeight" Value="SemiBold"/>
                        </Trigger>
                    </Style.Triggers>
                </Style>

                <Style x:Key="NativeComboBoxStyle" TargetType="ComboBox">
                    <Setter Property="Background" Value="{DynamicResource Theme.CardBg}"/>
                    <Setter Property="Foreground" Value="{DynamicResource Theme.TextPrimary}"/>
                    <Setter Property="BorderBrush" Value="{DynamicResource Theme.BorderStrong}"/>
                    <Setter Property="BorderThickness" Value="1"/>
                    <Setter Property="ItemContainerStyle" Value="{DynamicResource NativeComboBoxItemStyle}"/>
                    <Setter Property="Cursor" Value="Hand"/>
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ComboBox">
                                <Grid>
                                    <Border x:Name="Bd"
                                            Background="{TemplateBinding Background}"
                                            BorderBrush="{TemplateBinding BorderBrush}"
                                            BorderThickness="{TemplateBinding BorderThickness}"
                                            CornerRadius="8"/>
                                    <ContentPresenter Margin="12,0,32,0"
                                                      VerticalAlignment="Center"
                                                      HorizontalAlignment="Left"
                                                      Content="{TemplateBinding SelectionBoxItem}"
                                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                                      ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"
                                                      IsHitTestVisible="False"/>
                                    <Path Data="M 0,0 L 4.5,4.5 L 9,0"
                                          Stroke="{DynamicResource Theme.TextMuted}"
                                          StrokeThickness="1.6"
                                          StrokeStartLineCap="Round"
                                          StrokeEndLineCap="Round"
                                          StrokeLineJoin="Round"
                                          HorizontalAlignment="Right"
                                          VerticalAlignment="Center"
                                          Margin="0,0,12,0"
                                          IsHitTestVisible="False"/>
                                    <ToggleButton Opacity="0"
                                                  Focusable="False"
                                                  ClickMode="Press"
                                                  IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
                                    <Popup x:Name="PART_Popup"
                                           IsOpen="{TemplateBinding IsDropDownOpen}"
                                           Placement="Bottom"
                                           AllowsTransparency="True"
                                           Focusable="False"
                                           PopupAnimation="Fade">
                                        <Border MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                                MaxHeight="{TemplateBinding MaxDropDownHeight}"
                                                Margin="0,4,0,8"
                                                Padding="4"
                                                Background="{DynamicResource Theme.CardBg}"
                                                BorderBrush="{DynamicResource Theme.BorderStrong}"
                                                BorderThickness="1"
                                                CornerRadius="8">
                                            <Border.Effect>
                                                <DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="0.22" Color="#000000"/>
                                            </Border.Effect>
                                            <ScrollViewer SnapsToDevicePixels="True">
                                                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/>
                                            </ScrollViewer>
                                        </Border>
                                    </Popup>
                                </Grid>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsEnabled" Value="False">
                                        <Setter Property="Opacity" Value="0.55"/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """;

        var dict = (ResourceDictionary)XamlReader.Parse(xaml);
        _nativeComboBoxItemStyle = (Style)dict["NativeComboBoxItemStyle"];
        _nativeComboBoxStyle = (Style)dict["NativeComboBoxStyle"];
    }

    public static void PopulateResourceDictionary(ResourceDictionary resources)
    {
        resources["Theme.PageBg"] = PageBg;
        resources["Theme.CardBg"] = CardBg;
        resources["Theme.CardAltBg"] = CardAltBg;
        resources["Theme.SurfaceBg"] = SurfaceBg;
        resources["Theme.SubtleBg"] = SubtleBg;
        resources["Theme.StrongBg"] = StrongBg;
        resources["Theme.PressedBg"] = PressedBg;
        resources["Theme.BorderSubtle"] = BorderSubtle;
        resources["Theme.BorderStrong"] = BorderStrong;
        resources["Theme.TextPrimary"] = TextPrimary;
        resources["Theme.TextHeading"] = TextHeading;
        resources["Theme.TextSecondary"] = TextSecondary;
        resources["Theme.TextBody"] = TextBody;
        resources["Theme.TextMuted"] = TextMuted;
        resources["Theme.TextSubtle"] = TextSubtle;
        resources["Theme.IndigoSoftBg"] = IndigoSoftBg;
        resources["Theme.IndigoHoverBg"] = IndigoHoverBg;
        resources["Theme.IndigoPressedBg"] = IndigoPressedBg;
        resources["Theme.IndigoSoftBorder"] = IndigoSoftBorder;
        resources["Theme.IndigoText"] = IndigoText;
        resources["Theme.SuccessSoftBg"] = SuccessSoftBg;
        resources["Theme.SuccessHoverBg"] = SuccessHoverBg;
        resources["Theme.SuccessSoftBorder"] = SuccessSoftBorder;
        resources["Theme.SuccessText"] = SuccessText;
        resources["Theme.DangerSoftBg"] = DangerSoftBg;
        resources["Theme.DangerHoverBg"] = DangerHoverBg;
        resources["Theme.DangerSoftBorder"] = DangerSoftBorder;
        resources["Theme.DangerText"] = DangerText;
        resources["Theme.WarningSoftBg"] = WarningSoftBg;
        resources["Theme.WarningSoftBorder"] = WarningSoftBorder;
        resources["Theme.WarningText"] = WarningText;
        resources["Theme.PurpleSoftBg"] = PurpleSoftBg;
        resources["Theme.PurpleText"] = PurpleText;
        resources["Theme.BlueSoftBg"] = BlueSoftBg;
        resources["Theme.BlueText"] = BlueText;
        resources["Theme.PageGradient"] = PageGradient;

        resources["BgDeep"] = PageBg;
        resources["BgPanel"] = CardBg;
        resources["BgControl"] = SubtleBg;
        resources["Line"] = BorderSubtle;
        resources["LineStrong"] = BorderStrong;
        resources["TextMain"] = TextPrimary;
        resources["TextMuted"] = TextMuted;
        resources["Muted"] = TextMuted;

        if (_nativeComboBoxItemStyle is not null)
        {
            resources["NativeComboBoxItemStyle"] = _nativeComboBoxItemStyle;
            if (!resources.Contains(typeof(ComboBoxItem)))
                resources[typeof(ComboBoxItem)] = _nativeComboBoxItemStyle;
        }

        if (_nativeComboBoxStyle is not null)
        {
            resources["NativeComboBoxStyle"] = _nativeComboBoxStyle;
            if (!resources.Contains(typeof(System.Windows.Controls.ComboBox)))
                resources[typeof(System.Windows.Controls.ComboBox)] = _nativeComboBoxStyle;
        }
    }

    static void OnAnyFrameworkElementReady(object sender, RoutedEventArgs e)
    {
        if (sender is WpfWindow win)
        {
            if (IsExcludedRoot(win))
                return;

            TrackWindow(win);
            PopulateResourceDictionary(win.Resources);
            ApplyWpfWindowTitleBar(win, _isDarkMode);
            return;
        }

        if (sender is WpfUserControl uc)
        {
            if (IsExcludedRoot(uc))
                return;

            TrackUserControl(uc);
            PopulateResourceDictionary(uc.Resources);
        }
    }

    static bool IsExcludedRoot(DependencyObject obj)
    {
        var typeName = obj.GetType().Name;
        return typeName is "HomeWpfControl" or "SidebarControl";
    }

    static void TrackWindow(WpfWindow win)
    {
        foreach (var wr in TrackedWindows)
        {
            if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, win))
                return;
        }
        TrackedWindows.Add(new WeakReference<WpfWindow>(win));
    }

    static void TrackUserControl(WpfUserControl uc)
    {
        foreach (var wr in TrackedUserControls)
        {
            if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, uc))
                return;
        }
        TrackedUserControls.Add(new WeakReference<WpfUserControl>(uc));
    }

    static void ReapplyToTrackedRoots(bool isDark)
    {
        for (int i = TrackedWindows.Count - 1; i >= 0; i--)
        {
            if (TrackedWindows[i].TryGetTarget(out var win))
            {
                PopulateResourceDictionary(win.Resources);
                ApplyWpfWindowTitleBar(win, isDark);
            }
            else
            {
                TrackedWindows.RemoveAt(i);
            }
        }

        for (int i = TrackedUserControls.Count - 1; i >= 0; i--)
        {
            if (TrackedUserControls[i].TryGetTarget(out var uc))
                PopulateResourceDictionary(uc.Resources);
            else
                TrackedUserControls.RemoveAt(i);
        }
    }

    static void ApplyWpfWindowTitleBar(WpfWindow win, bool isDark)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (hwnd != IntPtr.Zero)
                Win32Dwm.ApplyTitleBarTheme(hwnd, isDark);
        }
        catch
        {
        }
    }

    public static void RefreshIfDark(DependencyObject _)
    {
    }

    public static void ApplyWpfTree(DependencyObject root, bool isDark)
    {
        if (root is FrameworkElement fe)
            PopulateResourceDictionary(fe.Resources);
    }

    static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    static RadialGradientBrush CreateFrozenRadialGradient(string startHex, string endHex)
    {
        var brush = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.4),
            RadiusX = 0.9,
            RadiusY = 0.9,
            GradientStops =
            [
                new GradientStop((WpfColor)WpfColorConverter.ConvertFromString(startHex), 0),
                new GradientStop((WpfColor)WpfColorConverter.ConvertFromString(endHex), 1),
            ],
        };
        brush.Freeze();
        return brush;
    }
}
