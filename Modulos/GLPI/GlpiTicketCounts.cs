namespace SistecHub.Modulos.GLPI;

/// <summary>Contagens de chamados por estado GLPI para uma entidade.</summary>
/// <remarks>
/// Estados GLPI (Ticket): 1 Novo, 2 Atribuído, 3 Planeado, 4 Em espera, 5 Resolvido, 6 Fechado.
/// </remarks>
public readonly record struct GlpiTicketCounts(
    int Novos,
    int Pendentes,
    int Atribuidos,
    int EmAtendimentoPlanejado,
    int Fechados);
