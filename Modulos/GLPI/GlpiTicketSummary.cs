namespace SistecHub.Modulos.GLPI;

/// <summary>Página de chamados devolvida pela pesquisa GLPI.</summary>
public sealed record GlpiTicketListPage(IReadOnlyList<GlpiTicketSummary> Tickets, int TotalCount);

/// <summary>Resumo de um chamado GLPI para exibição em listas.</summary>
public sealed record GlpiTicketSummary(int Id, string Title, int Status, DateTime? OpenedAt)
{
    /// <summary>Etiqueta legível do estado GLPI (campo 12).</summary>
    public string StatusLabel => GlpiTicketStatus.GetLabel(Status);
}

/// <summary>Tamanho padrão de paginação na listagem de chamados.</summary>
public static class GlpiTicketPagination
{
    public const int DefaultPageSize = 10;
}

/// <summary>Mapeamento dos estados GLPI (<c>Ticket.status</c>).</summary>
public static class GlpiTicketStatus
{
    public static string GetLabel(int status) =>
        status switch
        {
            1 => "Novo",
            2 => "Em atendimento",
            3 => "Pausado",
            4 => "Pendente",
            5 => "Resolvido",
            6 => "Fechado",
            _ => "Desconhecido",
        };
}
