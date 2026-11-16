using System.Windows.Forms.Integration;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.UI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Área do submódulo Chamados (GLPI) renderizada em WPF.
/// </summary>
public sealed class ChamadosView : UserControl
{
    /// <summary>O utilizador pediu a vista de abertura de chamado (navegação no módulo GLPI).</summary>
    public event EventHandler? AberturaChamadoSolicitada;

    readonly ChamadosWpfControl _wpfView;
    readonly System.Windows.Forms.Timer _cooldownTimer;

    const int TicketsPageSize = GlpiTicketPagination.DefaultPageSize;
    int _currentTicketsPage = 1;
    int _ticketsTotalCount;
    bool _loadingTicketsPage;

    public ChamadosView()
    {
        BackColor = ShellTheme.MainBg;
        Padding = new Padding(0);

        _cooldownTimer = new System.Windows.Forms.Timer { Enabled = false };
        _cooldownTimer.Tick += OnCooldownTimerTick;
        Disposed += (_, _) =>
        {
            _cooldownTimer.Tick -= OnCooldownTimerTick;
            _cooldownTimer.Dispose();
        };

        _wpfView = new ChamadosWpfControl();
        _wpfView.RefreshClicked += async (_, _) => await OnRefreshClickedAsync().ConfigureAwait(true);
        _wpfView.OpenNewTicketClicked += (_, _) => AberturaChamadoSolicitada?.Invoke(this, EventArgs.Empty);
        _wpfView.PrevPageClicked += async (_, _) => await GoToTicketsPageAsync(_currentTicketsPage - 1).ConfigureAwait(true);
        _wpfView.NextPageClicked += async (_, _) => await GoToTicketsPageAsync(_currentTicketsPage + 1).ConfigureAwait(true);

        var host = new ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.MainBg,
            Child = _wpfView,
        };

        Controls.Add(host);

        Load += (_, _) => _ = OnInitialLoadAsync();
    }

    async Task OnInitialLoadAsync()
    {
        try
        {
            if (!TryReadGlpiSettings(out var settings, out var entityId, out var error))
            {
                ShowConfigError(error);
                return;
            }

            if (ChamadosDataCache.TryGetForEntity(entityId, out var cached) && cached != null)
            {
                _currentTicketsPage = 1;
                ApplySnapshot(cached);
                UpdateRefreshButtonState();
                return;
            }

            await FetchAndCacheAsync(settings, entityId).ConfigureAwait(true);
            UpdateRefreshButtonState();
        }
        catch (Exception ex)
        {
            _wpfView.SetEntityText("GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex), isError: true);
            _wpfView.SetValuesDash();
            ClearRecentTickets();
        }
    }

    async Task OnRefreshClickedAsync()
    {
        if (!ChamadosDataCache.IsRefreshAllowed())
            return;

        if (!TryReadGlpiSettings(out var settings, out var entityId, out var error))
        {
            ShowConfigError(error);
            return;
        }

        _wpfView.SetRefreshEnabled(false);
        try
        {
            await FetchAndCacheAsync(settings, entityId).ConfigureAwait(true);
            ChamadosDataCache.SetRefreshCooldownFromNow();
            ArmCooldownTimer(60_000);
        }
        catch (Exception ex)
        {
            _wpfView.SetEntityText("GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex), isError: true);
            _wpfView.SetRefreshEnabled(ChamadosDataCache.IsRefreshAllowed());
        }
    }

    void OnCooldownTimerTick(object? sender, EventArgs e)
    {
        _cooldownTimer.Stop();
        _wpfView.SetRefreshEnabled(ChamadosDataCache.IsRefreshAllowed());
    }

    void ArmCooldownTimer(int intervalMs)
    {
        _cooldownTimer.Stop();
        _cooldownTimer.Interval = Math.Max(1, intervalMs);
        _cooldownTimer.Start();
    }

    void UpdateRefreshButtonState()
    {
        if (ChamadosDataCache.IsRefreshAllowed())
        {
            _wpfView.SetRefreshEnabled(true);
            return;
        }

        _wpfView.SetRefreshEnabled(false);
        var remain = (int)(ChamadosDataCache.GetNextRefreshAllowedUtc() - DateTime.UtcNow).TotalMilliseconds;
        if (remain <= 0)
        {
            _wpfView.SetRefreshEnabled(true);
            return;
        }

        ArmCooldownTimer(remain);
    }

    async Task FetchAndCacheAsync(AppUserSettings settings, int entityId)
    {
        var (info, counts, ticketsPage) =
            await GlpiApiClient.GetEntityAndTicketCountsAsync(settings, entityId).ConfigureAwait(true);

        var leaf = info.LeafDisplayName;
        var name = string.IsNullOrEmpty(leaf) ? info.Name.Trim() : leaf;
        if (string.IsNullOrWhiteSpace(name))
            name = "Entidade #" + entityId;

        var snap = new ChamadosSnapshot
        {
            EntityId = entityId,
            EntityLeafName = name,
            Counts = counts,
            TicketsTotalCount = ticketsPage.TotalCount,
            RecentTickets = ticketsPage.Tickets,
            LoadedAtLocal = DateTime.Now,
        };
        ChamadosDataCache.SetSnapshot(snap);
        _currentTicketsPage = 1;
        ApplySnapshot(snap);
    }

    void ApplySnapshot(ChamadosSnapshot snap)
    {
        var displayName = string.IsNullOrWhiteSpace(snap.EntityLeafName)
            ? "Entidade #" + snap.EntityId
            : snap.EntityLeafName.Trim();

        _wpfView.SetEntityText(displayName);
        _wpfView.SetLastUpdateText("Última atualização: " + snap.LoadedAtLocal.ToString("dd/MM/yyyy HH:mm"));
        _wpfView.SetCounts(snap.Counts);

        _ticketsTotalCount = snap.TicketsTotalCount;
        _wpfView.RenderTickets(snap.RecentTickets, scrollToTop: true);
        UpdatePaginationControls();
    }

    async Task GoToTicketsPageAsync(int page)
    {
        if (_loadingTicketsPage)
            return;

        var totalPages = GetTicketsTotalPages();
        if (page < 1 || page > totalPages)
            return;

        if (!TryReadGlpiSettings(out var settings, out var entityId, out _))
            return;

        _loadingTicketsPage = true;
        UpdatePaginationControls();
        try
        {
            var ticketsPage = await GlpiApiClient
                .GetTicketsPageAsync(settings, entityId, page - 1, TicketsPageSize)
                .ConfigureAwait(true);

            _currentTicketsPage = page;
            _ticketsTotalCount = ticketsPage.TotalCount;
            _wpfView.RenderTickets(ticketsPage.Tickets, scrollToTop: true);
            UpdatePaginationControls();
        }
        catch (Exception ex)
        {
            _wpfView.SetEntityText("GLPI: " + global::SistecHub.UserFacingErrorHelper.FormatForUser(ex), isError: true);
        }
        finally
        {
            _loadingTicketsPage = false;
            UpdatePaginationControls();
        }
    }

    int GetTicketsTotalPages()
    {
        if (_ticketsTotalCount <= 0)
            return 1;
        return (int)Math.Ceiling(_ticketsTotalCount / (double)TicketsPageSize);
    }

    void UpdatePaginationControls()
    {
        var totalPages = GetTicketsTotalPages();
        var showPagination = _ticketsTotalCount > TicketsPageSize;
        if (!showPagination)
        {
            _wpfView.UpdatePagination(visible: false, pageInfo: "", canPrev: false, canNext: false);
            return;
        }

        var from = (_currentTicketsPage - 1) * TicketsPageSize + 1;
        var to = Math.Min(_currentTicketsPage * TicketsPageSize, _ticketsTotalCount);
        var pageInfo = $"Página {_currentTicketsPage} de {totalPages}  ·  {from}–{to} de {_ticketsTotalCount}";

        var canNavigate = !_loadingTicketsPage;
        var canPrev = canNavigate && _currentTicketsPage > 1;
        var canNext = canNavigate && _currentTicketsPage < totalPages;

        _wpfView.UpdatePagination(visible: true, pageInfo: pageInfo, canPrev: canPrev, canNext: canNext);
    }

    void ClearRecentTickets()
    {
        _ticketsTotalCount = 0;
        _currentTicketsPage = 1;
        _wpfView.ClearTickets();
    }

    void ShowConfigError(string message)
    {
        _wpfView.SetEntityText(message, isError: false, isMuted: true);
        _wpfView.SetLastUpdateText("");
        _wpfView.SetValuesDash();
        ClearRecentTickets();
        _wpfView.SetRefreshEnabled(false);
    }

    static bool TryReadGlpiSettings(out AppUserSettings settings, out int entityId, out string error)
    {
        settings = AppSettingsStore.Load();
        entityId = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
        {
            error =
                "Configure o User token do GLPI em Configurações para identificar a entidade.";
            return false;
        }

        if (!int.TryParse((settings.EntityId ?? "").Trim(), out entityId))
        {
            error =
                "Indique um Id da entidade numérico (client id) em Configurações para consultar o GLPI.";
            return false;
        }

        return true;
    }
}
