using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.GLPI.Chamados;

public sealed partial class AberturaChamadoWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? IaProblemaClicked;
    public event EventHandler? CapturaAreaClicked;
    public event EventHandler? CapturaTelaClicked;
    public event EventHandler? AnexoEscolherClicked;
    public event EventHandler? AnexoLimparClicked;
    public event EventHandler? CapturarAcessoRemotoClicked;
    public event EventHandler? AcessoRemotoNaoSeAplicaClicked;
    public event EventHandler? AcessoRemotoTextChangedByUser;
    public event EventHandler? CapturarRemotoIdClicked;
    public event EventHandler? CapturarAnyDeskClicked;
    public event EventHandler? AnyDeskNaoSeAplicaClicked;
    public event EventHandler? TermosClicked;
    public event EventHandler? CancelarClicked;
    public event EventHandler? SolicitarChamadoClicked;
    public event EventHandler? AnyDeskTextChangedByUser;

    static readonly MediaBrush WhiteBrush = CreateFrozenBrush(255, 255, 255);
    static readonly MediaBrush DisabledFieldBgBrush = CreateFrozenBrush(226, 232, 240);
    static readonly MediaBrush PrimaryTextBrush = CreateFrozenBrush(15, 23, 42);
    static readonly MediaBrush MutedTextBrush = CreateFrozenBrush(100, 116, 139);
    static readonly MediaBrush IndigoBrush = CreateFrozenBrush(67, 56, 202);
    static readonly MediaBrush PastelRedBgBrush = CreateFrozenBrush(254, 226, 226);
    static readonly MediaBrush PastelRedActiveBgBrush = CreateFrozenBrush(254, 202, 202);

    public AberturaChamadoWpfControl()
    {
        InitializeComponent();
    }

    public string TextoProblema
    {
        get
        {
            if (Dispatcher.CheckAccess())
                return (TxtProblema.Text ?? "").Trim();
            return Dispatcher.Invoke(() => (TxtProblema.Text ?? "").Trim());
        }
        set
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => TextoProblema = value);
                return;
            }
            var v = value ?? "";
            if (v.Length > TxtProblema.MaxLength)
                v = v[..TxtProblema.MaxLength];
            TxtProblema.Text = v;
        }
    }

    public string Whatsapp =>
        Dispatcher.CheckAccess() ? (TxtWhatsapp.Text ?? "").Trim() : Dispatcher.Invoke(() => (TxtWhatsapp.Text ?? "").Trim());

    public string NomeContato =>
        Dispatcher.CheckAccess() ? (TxtNomeContato.Text ?? "").Trim() : Dispatcher.Invoke(() => (TxtNomeContato.Text ?? "").Trim());

    public string Observacoes =>
        Dispatcher.CheckAccess() ? (TxtObservacoes.Text ?? "").Trim() : Dispatcher.Invoke(() => (TxtObservacoes.Text ?? "").Trim());

    public string AnyDesk
    {
        get => Dispatcher.CheckAccess() ? (TxtAnyDesk.Text ?? "").Trim() : Dispatcher.Invoke(() => (TxtAnyDesk.Text ?? "").Trim());
        set
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AnyDesk = value);
                return;
            }
            TxtAnyDesk.Text = value ?? "";
            TxtAnyDesk.CaretIndex = TxtAnyDesk.Text.Length;
            TxtAnyDesk.Focus();
        }
    }

    public string RemotoId
    {
        get => Dispatcher.CheckAccess() ? (TxtRemotoId.Text ?? "").Trim() : Dispatcher.Invoke(() => (TxtRemotoId.Text ?? "").Trim());
        set
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => RemotoId = value);
                return;
            }
            TxtRemotoId.Text = value ?? "";
            TxtRemotoId.CaretIndex = TxtRemotoId.Text.Length;
        }
    }

    public void SetIaButtonEnabled(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetIaButtonEnabled(enabled));
            return;
        }
        BtnIaProblema.IsEnabled = enabled;
    }

    public void SetCapturaTelaButtonEnabled(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetCapturaTelaButtonEnabled(enabled));
            return;
        }
        BtnCapturaTela.IsEnabled = enabled;
    }

    public void SetAcessoRemotoVisualState(bool ativo)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetAcessoRemotoVisualState(ativo));
            return;
        }

        if (!ativo)
        {
            TxtRemotoId.Clear();
            TxtAnyDesk.Clear();
        }

        RemotoIdBorder.Background = ativo ? WhiteBrush : DisabledFieldBgBrush;
        TxtRemotoId.IsReadOnly = !ativo;
        TxtRemotoId.Foreground = ativo ? IndigoBrush : MutedTextBrush;
        TxtRemotoId.Cursor = ativo ? System.Windows.Input.Cursors.IBeam : System.Windows.Input.Cursors.Arrow;

        AnyDeskBorder.Background = ativo ? WhiteBrush : DisabledFieldBgBrush;
        TxtAnyDesk.IsReadOnly = !ativo;
        TxtAnyDesk.Foreground = ativo ? PrimaryTextBrush : MutedTextBrush;
        TxtAnyDesk.Cursor = ativo ? System.Windows.Input.Cursors.IBeam : System.Windows.Input.Cursors.Arrow;

        BtnAcessoRemotoNaoSeAplica.Background = ativo ? PastelRedBgBrush : PastelRedActiveBgBrush;
        UpdateAnyDeskHintVisibility();
        HintRemotoId.Visibility = (string.IsNullOrEmpty(TxtRemotoId.Text) && ativo) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetAnyDeskVisualState(bool ativo) => SetAcessoRemotoVisualState(ativo);

    public void ClearAttachmentPreview()
    {
        ImgCapturaPreview.Source = null;
        ImgCapturaPreview.Visibility = Visibility.Collapsed;
        TxtCapturaHint.Text = "Clique para escolher\nimagem ou ficheiro";
        TxtCapturaHint.Visibility = Visibility.Visible;
        BtnAnexoLimpar.IsEnabled = false;
        CapturaPreviewHost.ToolTip = null;
    }

    public void ShowAttachmentPreview(string caminhoCompleto, bool isImage)
    {
        if (isImage)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(caminhoCompleto, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                ImgCapturaPreview.Source = bmp;
                ImgCapturaPreview.Visibility = Visibility.Visible;
                TxtCapturaHint.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ImgCapturaPreview.Source = null;
                ImgCapturaPreview.Visibility = Visibility.Collapsed;
                TxtCapturaHint.Text = "Ficheiro:\n" + Path.GetFileName(caminhoCompleto);
                TxtCapturaHint.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ImgCapturaPreview.Source = null;
            ImgCapturaPreview.Visibility = Visibility.Collapsed;
            TxtCapturaHint.Text = "Ficheiro anexado:\n" + Path.GetFileName(caminhoCompleto);
            TxtCapturaHint.Visibility = Visibility.Visible;
        }

        BtnAnexoLimpar.IsEnabled = true;
        CapturaPreviewHost.ToolTip = caminhoCompleto;
    }

    private void TxtProblema_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HintProblema.Visibility = string.IsNullOrEmpty(TxtProblema.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtObservacoes_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        HintObservacoes.Visibility = string.IsNullOrEmpty(TxtObservacoes.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtWhatsapp_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filtered = FiltrarSomenteDigitos(TxtWhatsapp.Text);
        if (filtered != TxtWhatsapp.Text)
        {
            var caret = Math.Min(TxtWhatsapp.CaretIndex, filtered.Length);
            TxtWhatsapp.Text = filtered;
            TxtWhatsapp.CaretIndex = caret;
        }
        HintWhatsapp.Visibility = string.IsNullOrEmpty(TxtWhatsapp.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtNomeContato_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filtered = FiltrarSomenteLetras(TxtNomeContato.Text);
        if (filtered != TxtNomeContato.Text)
        {
            var caret = Math.Min(TxtNomeContato.CaretIndex, filtered.Length);
            TxtNomeContato.Text = filtered;
            TxtNomeContato.CaretIndex = caret;
        }
        HintNomeContato.Visibility = string.IsNullOrEmpty(TxtNomeContato.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtAnyDesk_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filtered = FiltrarSomenteDigitos(TxtAnyDesk.Text);
        if (filtered != TxtAnyDesk.Text)
        {
            var caret = Math.Min(TxtAnyDesk.CaretIndex, filtered.Length);
            TxtAnyDesk.Text = filtered;
            TxtAnyDesk.CaretIndex = caret;
        }
        UpdateAnyDeskHintVisibility();
        if (!string.IsNullOrEmpty(TxtAnyDesk.Text) && TxtAnyDesk.IsFocused)
        {
            AcessoRemotoTextChangedByUser?.Invoke(this, EventArgs.Empty);
            AnyDeskTextChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    void UpdateAnyDeskHintVisibility()
    {
        HintAnyDesk.Visibility = string.IsNullOrEmpty(TxtAnyDesk.Text) && !TxtAnyDesk.IsReadOnly
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TxtRemotoId_TextChanged(object sender, TextChangedEventArgs e)
    {
        HintRemotoId.Visibility = string.IsNullOrEmpty(TxtRemotoId.Text) && !TxtRemotoId.IsReadOnly
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(TxtRemotoId.Text) && TxtRemotoId.IsFocused)
        {
            AcessoRemotoTextChangedByUser?.Invoke(this, EventArgs.Empty);
            AnyDeskTextChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DigitsAndSpaces_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch) && ch != ' ' && ch != '-')
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void DigitsAndSpaces_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string));
            if (FiltrarDigitosEEspacos(text) != text)
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    static string FiltrarDigitosEEspacos(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch) || ch == ' ' || ch == '-')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            if (!char.IsDigit(ch))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void DigitsOnly_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string));
            if (FiltrarSomenteDigitos(text) != text)
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void LettersOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            if (!char.IsLetter(ch) && ch is not ' ' and not '-' and not '\'')
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void LettersOnly_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            var text = (string)e.DataObject.GetData(typeof(string));
            if (FiltrarSomenteLetras(text) != text)
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    static string FiltrarSomenteDigitos(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    static string FiltrarSomenteLetras(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetter(ch) || ch is ' ' or '-' or '\'')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private void BtnIaProblema_Click(object sender, RoutedEventArgs e)
        => IaProblemaClicked?.Invoke(this, EventArgs.Empty);

    private void CapturaPreviewHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => CapturaAreaClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCapturaTela_Click(object sender, RoutedEventArgs e)
        => CapturaTelaClicked?.Invoke(this, EventArgs.Empty);

    private void BtnAnexoEscolher_Click(object sender, RoutedEventArgs e)
        => AnexoEscolherClicked?.Invoke(this, EventArgs.Empty);

    private void BtnAnexoLimpar_Click(object sender, RoutedEventArgs e)
        => AnexoLimparClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCapturarAcessoRemoto_Click(object sender, RoutedEventArgs e)
    {
        CapturarAcessoRemotoClicked?.Invoke(this, EventArgs.Empty);
        CapturarRemotoIdClicked?.Invoke(this, EventArgs.Empty);
        CapturarAnyDeskClicked?.Invoke(this, EventArgs.Empty);
    }

    private void BtnAcessoRemotoNaoSeAplica_Click(object sender, RoutedEventArgs e)
    {
        AcessoRemotoNaoSeAplicaClicked?.Invoke(this, EventArgs.Empty);
        AnyDeskNaoSeAplicaClicked?.Invoke(this, EventArgs.Empty);
    }

    private void BtnCapturarRemotoId_Click(object sender, RoutedEventArgs e)
        => CapturarRemotoIdClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCapturarAnyDesk_Click(object sender, RoutedEventArgs e)
        => CapturarAnyDeskClicked?.Invoke(this, EventArgs.Empty);

    private void BtnAnyDeskNaoSeAplica_Click(object sender, RoutedEventArgs e)
        => AnyDeskNaoSeAplicaClicked?.Invoke(this, EventArgs.Empty);

    private void TermosHyperlink_Click(object sender, RoutedEventArgs e)
        => TermosClicked?.Invoke(this, EventArgs.Empty);

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        => CancelarClicked?.Invoke(this, EventArgs.Empty);

    private void BtnSolicitarChamado_Click(object sender, RoutedEventArgs e)
        => SolicitarChamadoClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

