using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SistecHub.Modulos.GLPI;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SistecHub.Modulos.GLPI.Chamados;

public sealed partial class ChamadosWpfControl : System.Windows.Controls.UserControl
{
    public event EventHandler? RefreshClicked;
    public event EventHandler? OpenNewTicketClicked;
    public event EventHandler? PrevPageClicked;
    public event EventHandler? NextPageClicked;

    static readonly MediaBrush PrimaryTextBrush = CreateFrozenBrush(15, 23, 42);
    static readonly MediaBrush MutedTextBrush = CreateFrozenBrush(100, 116, 139);
    static readonly MediaBrush ErrorTextBrush = CreateFrozenBrush(220, 38, 38);
    static readonly MediaBrush AccentBrush = CreateFrozenBrush(99, 102, 241);

    public ChamadosWpfControl()
    {
        InitializeComponent();
    }

    public void SetEntityText(string text, bool isError = false, bool isMuted = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetEntityText(text, isError, isMuted));
            return;
        }

        TxtEntityName.Text = text ?? "";
        TxtEntityName.Foreground = isError
            ? ErrorTextBrush
            : isMuted
                ? MutedTextBrush
                : PrimaryTextBrush;
    }

    public void SetLastUpdateText(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetLastUpdateText(text));
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            TxtStatusCategory.Text = "SINCRONIZAÇÃO";
            TxtLastUpdate.Text = "Aguardando…";
            TxtLastUpdate.Foreground = MutedTextBrush;
            StatusDotContainer.Background = CreateFrozenBrush(148, 163, 184);
            return;
        }

        if (text.StartsWith("A atualizar", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Atualizando", StringComparison.OrdinalIgnoreCase))
        {
            TxtStatusCategory.Text = "STATUS";
            TxtLastUpdate.Text = "Sincronizando…";
            TxtLastUpdate.Foreground = AccentBrush;
            StatusDotContainer.Background = AccentBrush;
            return;
        }

        const string prefix = "Última atualização: ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw = text.Substring(prefix.Length).Trim();
            if (DateTime.TryParse(raw, out var dt))
            {
                var isToday = dt.Date == DateTime.Today;
                TxtLastUpdate.Text = isToday ? $"Hoje às {dt:HH:mm}" : dt.ToString("dd/MM HH:mm");
            }
            else
            {
                TxtLastUpdate.Text = $"Às {raw}";
            }

            TxtStatusCategory.Text = "ÚLTIMA SINCRONIZAÇÃO";
            TxtLastUpdate.Foreground = CreateFrozenBrush(30, 41, 59);
            StatusDotContainer.Background = CreateFrozenBrush(16, 185, 129);
            return;
        }

        TxtStatusCategory.Text = "ÚLTIMA SINCRONIZAÇÃO";
        TxtLastUpdate.Text = text;
        TxtLastUpdate.Foreground = CreateFrozenBrush(30, 41, 59);
        StatusDotContainer.Background = CreateFrozenBrush(16, 185, 129);
    }

    public void SetRefreshEnabled(bool enabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetRefreshEnabled(enabled));
            return;
        }

        BtnRefresh.IsEnabled = enabled;
        if (!enabled)
        {
            TxtRefreshBtnLabel.Text = "Atualizando…";
            TxtRefreshBtnLabel.Foreground = AccentBrush;
            TxtStatusCategory.Text = "STATUS";
            TxtLastUpdate.Text = "Sincronizando…";
            TxtLastUpdate.Foreground = AccentBrush;
            StatusDotContainer.Background = AccentBrush;
            StartSpinning();
        }
        else
        {
            TxtRefreshBtnLabel.Text = "Atualizar";
            TxtRefreshBtnLabel.Foreground = CreateFrozenBrush(51, 65, 85);
            StopSpinning();
        }
    }

    void StartSpinning()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(StartSpinning);
            return;
        }

        IconRefresh.Foreground = AccentBrush;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.75),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        RefreshRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, anim);
    }

    void StopSpinning()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(StopSpinning);
            return;
        }

        RefreshRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        RefreshRotateTransform.Angle = 0;
        IconRefresh.Foreground = CreateFrozenBrush(51, 65, 85);
    }

    public void SetCounts(GlpiTicketCounts counts)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetCounts(counts));
            return;
        }

        ValNovos.Text = counts.Novos.ToString();
        ValPendentes.Text = counts.Pendentes.ToString();
        ValAtribuidos.Text = counts.Atribuidos.ToString();
        ValPausados.Text = counts.EmAtendimentoPlanejado.ToString();
        ValFechados.Text = counts.Fechados.ToString();
    }

    public void SetValuesDash()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(SetValuesDash);
            return;
        }

        ValNovos.Text = "—";
        ValPendentes.Text = "—";
        ValAtribuidos.Text = "—";
        ValPausados.Text = "—";
        ValFechados.Text = "—";
    }

    public void RenderTickets(IReadOnlyList<GlpiTicketSummary> tickets, bool scrollToTop = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => RenderTickets(tickets, scrollToTop));
            return;
        }

        if (tickets == null || tickets.Count == 0)
        {
            TicketsItemsControl.ItemsSource = null;
            TxtEmptyTickets.Visibility = Visibility.Visible;
            return;
        }

        TxtEmptyTickets.Visibility = Visibility.Collapsed;
        var items = tickets.Select(t => new TicketRowViewModel(t)).ToList();
        TicketsItemsControl.ItemsSource = items;

        if (scrollToTop)
            TicketsScrollViewer.ScrollToTop();
    }

    public void ClearTickets()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ClearTickets);
            return;
        }

        TicketsItemsControl.ItemsSource = null;
        TxtEmptyTickets.Visibility = Visibility.Collapsed;
        PaginationPanel.Visibility = Visibility.Collapsed;
    }

    public void UpdatePagination(bool visible, string pageInfo, bool canPrev, bool canNext)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdatePagination(visible, pageInfo, canPrev, canNext));
            return;
        }

        PaginationPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
            return;

        TxtPageInfo.Text = pageInfo ?? "";
        BtnPrevPage.IsEnabled = canPrev;
        BtnNextPage.IsEnabled = canNext;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => RefreshClicked?.Invoke(this, EventArgs.Empty);

    private void BtnOpenNewTicket_Click(object sender, RoutedEventArgs e)
        => OpenNewTicketClicked?.Invoke(this, EventArgs.Empty);

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        => PrevPageClicked?.Invoke(this, EventArgs.Empty);

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        => NextPageClicked?.Invoke(this, EventArgs.Empty);

    static MediaBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public sealed class TicketRowViewModel
    {
        public string IdDisplay { get; }
        public string TitleDisplay { get; }
        public string StatusLabel { get; }
        public MediaBrush BadgeBackground { get; }

        public TicketRowViewModel(GlpiTicketSummary ticket)
        {
            IdDisplay = "#" + ticket.Id;
            TitleDisplay = string.IsNullOrWhiteSpace(ticket.Title) ? "(sem título)" : ticket.Title.Trim();
            StatusLabel = ticket.StatusLabel;
            BadgeBackground = GetStatusBadgeBrush(ticket.Status);
        }

        static MediaBrush GetStatusBadgeBrush(int status) =>
            status switch
            {
                1 => CreateFrozenBrush(22, 163, 74),    // Novo — verde #16A34A
                2 => CreateFrozenBrush(37, 99, 235),    // Em atendimento — azul #2563EB
                3 => CreateFrozenBrush(234, 88, 12),    // Pausado — laranja #EA580C
                4 => CreateFrozenBrush(217, 119, 6),    // Pendente — âmbar #D97706
                5 => CreateFrozenBrush(124, 58, 237),   // Resolvido — roxo #7C3AED
                6 => CreateFrozenBrush(100, 116, 139),  // Fechado — ardósia #64748B
                _ => CreateFrozenBrush(148, 163, 184),
            };
    }
}
