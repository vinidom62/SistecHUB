using SistecHub.Modulos.GLPI;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>Dados de chamados obtidos do GLPI para reutilizar na sessão da aplicação.</summary>
public sealed class ChamadosSnapshot
{
    public required int EntityId { get; init; }
    public required string EntityLeafName { get; init; }
    public required GlpiTicketCounts Counts { get; init; }
    public required int TicketsTotalCount { get; init; }
    public required IReadOnlyList<GlpiTicketSummary> RecentTickets { get; init; }
    public required DateTime LoadedAtLocal { get; init; }
}

/// <summary>Cache em memória (vida = processo) e controlo de cooldown do botão atualizar.</summary>
public static class ChamadosDataCache
{
    static readonly object Gate = new();
    static ChamadosSnapshot? _snapshot;
    static DateTime _nextRefreshAllowedUtc = DateTime.MinValue;

    /// <summary>Disparado quando um novo snapshot é gravado no cache.</summary>
    public static event Action? SnapshotUpdated;

    public static bool TryGetForEntity(int entityId, out ChamadosSnapshot? snapshot)
    {
        lock (Gate)
        {
            if (_snapshot != null && _snapshot.EntityId == entityId)
            {
                snapshot = _snapshot;
                return true;
            }

            snapshot = null;
            return false;
        }
    }

    public static void SetSnapshot(ChamadosSnapshot snapshot)
    {
        lock (Gate)
        {
            _snapshot = snapshot;
        }

        SnapshotUpdated?.Invoke();
    }

    public static bool TryGetEntityDisplayName(out string displayName)
    {
        lock (Gate)
        {
            if (_snapshot != null && !string.IsNullOrWhiteSpace(_snapshot.EntityLeafName))
            {
                displayName = _snapshot.EntityLeafName.Trim();
                return true;
            }
        }

        displayName = "";
        return false;
    }

    public static bool IsRefreshAllowed()
    {
        lock (Gate)
        {
            return DateTime.UtcNow >= _nextRefreshAllowedUtc;
        }
    }

    public static void SetRefreshCooldownFromNow()
    {
        lock (Gate)
        {
            _nextRefreshAllowedUtc = DateTime.UtcNow.AddMinutes(1);
        }
    }

    public static DateTime GetNextRefreshAllowedUtc()
    {
        lock (Gate)
        {
            return _nextRefreshAllowedUtc;
        }
    }
}
