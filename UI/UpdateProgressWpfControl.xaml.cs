using System.Windows;
using System.Windows.Media.Animation;
using SistecHub.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.UI;

public sealed partial class UpdateProgressWpfControl : System.Windows.Controls.UserControl
{
    const string SyncGlyph = "\uE895";
    const string DownloadGlyph = "\uE896";
    const string CheckGlyph = "\uE73E";
    const string ErrorGlyph = "\uE783";

    static MediaBrush IndigoBadgeBg => AppThemeManager.IndigoSoftBg;
    static MediaBrush IndigoBadgeFg => AppThemeManager.IndigoText;
    static readonly MediaBrush IndigoAccent = CreateFrozenBrush(99, 102, 241);

    static MediaBrush EmeraldBadgeBg => AppThemeManager.SuccessSoftBg;
    static MediaBrush EmeraldBadgeFg => AppThemeManager.SuccessText;

    static MediaBrush RedBadgeBg => AppThemeManager.DangerSoftBg;
    static MediaBrush RedBadgeFg => AppThemeManager.DangerText;

    public event EventHandler? RestartNowClicked;
    public event EventHandler? RetryClicked;
    public event EventHandler? ViewLogsClicked;
    public event EventHandler? CloseClicked;

    public UpdateProgressWpfControl()
    {
        InitializeComponent();
    }

    public void ShowCheckingStage(bool isBeta)
    {
        SetBadge(SyncGlyph, IndigoBadgeBg, IndigoBadgeFg, spin: true);
        TxtTitle.Text = isBeta ? "Verificando atualização Beta" : "Verificando atualizações";
        TxtSubtitle.Text = "SistecHub Update";
        TxtMessage.Text = isBeta
            ? "Verificando se há pré-releases (Beta) disponíveis no servidor..."
            : "Verificando se há atualizações disponíveis no servidor...";

        PnlProgressSection.Visibility = Visibility.Visible;
        BarProgress.IsIndeterminate = true;
        BarProgress.Foreground = IndigoAccent;
        TxtProgressHint.Text = "Consultando servidor de releases...";
        TxtProgressHint.Foreground = IndigoAccent;
        TxtPercent.Visibility = Visibility.Collapsed;

        PnlActions.Visibility = Visibility.Collapsed;
        BtnRestartNow.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnViewLogs.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Collapsed;
    }

    public void ShowDownloadingStage(string version, int progressPercent)
    {
        var verText = NormalizeVersionLabel(version);
        var clamped = Math.Clamp(progressPercent, 0, 100);

        SetBadge(DownloadGlyph, IndigoBadgeBg, IndigoBadgeFg, spin: false);
        TxtTitle.Text = "Baixando atualização";
        TxtSubtitle.Text = $"Nova versão {verText}";
        TxtMessage.Text = $"Baixando atualização para a versão {verText}...";

        PnlProgressSection.Visibility = Visibility.Visible;
        BarProgress.IsIndeterminate = clamped <= 0;
        BarProgress.Value = clamped;
        BarProgress.Foreground = IndigoAccent;
        TxtProgressHint.Text = "Transferindo pacotes de atualização...";
        TxtProgressHint.Foreground = IndigoAccent;
        TxtPercent.Text = $"{clamped}% concluído";
        TxtPercent.Visibility = Visibility.Visible;

        PnlActions.Visibility = Visibility.Collapsed;
        BtnRestartNow.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnViewLogs.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Collapsed;
    }

    public void ShowReadyCountdownStage(string version, int secondsRemaining, int totalSeconds = 10)
    {
        var verText = NormalizeVersionLabel(version);
        var remaining = Math.Max(0, secondsRemaining);
        var total = Math.Max(1, totalSeconds);
        var progress = Math.Clamp((int)Math.Round(((total - remaining) / (double)total) * 100.0), 0, 100);

        SetBadge(CheckGlyph, EmeraldBadgeBg, EmeraldBadgeFg, spin: false);
        TxtTitle.Text = "Download concluído com sucesso!";
        TxtSubtitle.Text = $"Versão {verText} pronta para aplicação";
        TxtMessage.Text = $"Download concluído com sucesso! O SistecHub será atualizado para a versão {verText}. A atualização será aplicada agora.";

        PnlProgressSection.Visibility = Visibility.Visible;
        BarProgress.IsIndeterminate = false;
        BarProgress.Value = progress;
        BarProgress.Foreground = IndigoAccent;
        TxtProgressHint.Text = $"O aplicativo será reiniciado em {remaining} segundo{(remaining == 1 ? "" : "s")}...";
        TxtProgressHint.Foreground = IndigoBadgeFg;
        TxtPercent.Visibility = Visibility.Collapsed;

        PnlActions.Visibility = Visibility.Visible;
        BtnRestartNow.Visibility = Visibility.Visible;
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnViewLogs.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Collapsed;
    }

    public void ShowUpToDateStage(string currentVersion, bool isBeta)
    {
        var verText = NormalizeVersionLabel(currentVersion);

        SetBadge(CheckGlyph, EmeraldBadgeBg, EmeraldBadgeFg, spin: false);
        TxtTitle.Text = "Sistema atualizado";
        TxtSubtitle.Text = $"Versão atual: {verText}";
        TxtMessage.Text = isBeta
            ? $"Não há pré-releases mais recentes disponíveis no momento. Você já está utilizando a versão {verText}."
            : $"Você já está utilizando a versão mais recente ({verText}).";

        PnlProgressSection.Visibility = Visibility.Collapsed;

        PnlActions.Visibility = Visibility.Visible;
        BtnRestartNow.Visibility = Visibility.Collapsed;
        BtnRetry.Visibility = Visibility.Collapsed;
        BtnViewLogs.Visibility = Visibility.Collapsed;
        BtnClose.Visibility = Visibility.Visible;
    }

    public void ShowErrorStage(string friendlyErrorMessage)
    {
        SetBadge(ErrorGlyph, RedBadgeBg, RedBadgeFg, spin: false);
        TxtTitle.Text = "Não foi possível concluir a verificação";
        TxtSubtitle.Text = "Falha na comunicação ou serviço";
        TxtMessage.Text = string.IsNullOrWhiteSpace(friendlyErrorMessage)
            ? "Ocorreu um problema ao verificar ou baixar a atualização. Verifique sua conexão com a internet e tente novamente."
            : friendlyErrorMessage;

        PnlProgressSection.Visibility = Visibility.Collapsed;

        PnlActions.Visibility = Visibility.Visible;
        BtnRestartNow.Visibility = Visibility.Collapsed;
        BtnViewLogs.Visibility = Visibility.Visible;
        BtnRetry.Visibility = Visibility.Visible;
        BtnClose.Visibility = Visibility.Visible;
    }

    void SetBadge(string glyph, MediaBrush bg, MediaBrush fg, bool spin)
    {
        TxtIconGlyph.Text = glyph;
        BdIconBadge.Background = bg;
        TxtIconGlyph.Foreground = fg;

        if (spin)
        {
            var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.4)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            IconRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim);
        }
        else
        {
            IconRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            IconRotateTransform.Angle = 0;
        }
    }

    static string NormalizeVersionLabel(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "v" + AppVersion.Current;

        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? trimmed : "v" + trimmed;
    }

    void BtnRestartNow_Click(object sender, RoutedEventArgs e) =>
        RestartNowClicked?.Invoke(this, EventArgs.Empty);

    void BtnRetry_Click(object sender, RoutedEventArgs e) =>
        RetryClicked?.Invoke(this, EventArgs.Empty);

    void BtnViewLogs_Click(object sender, RoutedEventArgs e) =>
        ViewLogsClicked?.Invoke(this, EventArgs.Empty);

    void BtnClose_Click(object sender, RoutedEventArgs e) =>
        CloseClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
