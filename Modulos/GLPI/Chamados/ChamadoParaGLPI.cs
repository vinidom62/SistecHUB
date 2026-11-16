using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using SistecHub.Core;
using SistecHub.Modulos.AcessoRemoto;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.IA;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Monta o texto do chamado a partir do formulário e envia-o ao GLPI (campo <c>content</c> do ticket).
/// </summary>
public static class ChamadoParaGLPI
{
    const int TituloMaxLength = 200;

    const int MaxPalavrasTituloIa = 8;

    /// <summary>Login GLPI do requerente dos chamados criados pelo SistecHub.</summary>
    public const string LoginRequerenteGlpi = GlpiApiClient.ServiceAccountLogin;

    const string PrefixoTitulo = "[SistecHub]";

    /// <summary>Limite de linhas na lista enviada ao modelo (evita prompts excessivos).</summary>
    const int MaxCategoriasNoPromptIa = 400;

    /// <summary>Instruções + descrição técnica enviadas ao modelo (uma única mensagem <c>user</c>).</summary>
    static string MontarPromptTituloIa(string descricaoChamado) =>
        "Com base na seguinte descrição de chamado técnico, crie um título descritivo e conciso. O título deve ter no máximo 8 palavras e resumir o problema principal. Retorne APENAS o título, sem explicações ou formatação adicional."
        + Environment.NewLine
        + Environment.NewLine
        + (descricaoChamado ?? "").Trim();

    const string UrlMaquinaInventario =
        "https://angelus.sisteconsultoria.com.br/angelus/plugins/sistechub/front/maquina.php?id=";

    /// <summary>Texto completo da descrição (corpo do ticket no GLPI).</summary>
    /// <remarks>GLPI renderiza HTML: usa <c>&lt;br&gt;</c> para quebras (newline puro é ignorado).</remarks>
    public static string MontarDescricao(
        string textoProblema,
        string whatsapp,
        string nomeContato,
        string anyDesk,
        bool anyDeskNaoSeAplica,
        string? remotoId,
        string observacoes,
        AppUserSettings settings)
    {
        var sb = new StringBuilder();
        AppendHtmlLine(sb, "Chamado aberto a partir do SistecHub.");
        AppendHtmlBreak(sb);
        AppendHtmlLine(sb, "— Relato do problema —");
        AppendHtmlMultiline(sb, string.IsNullOrEmpty(textoProblema) ? "(não indicado)" : textoProblema);
        AppendHtmlBreak(sb);
        AppendHtmlLine(sb, "— Contato —");
        AppendLinhaWhatsAppHtml(sb, whatsapp);
        AppendHtmlLine(
            sb,
            "Nome: " + (string.IsNullOrEmpty(nomeContato) ? "(não indicado)" : nomeContato));
        AppendLinhaAcessoRemotoSistecHubHtml(sb, remotoId, settings);
        AppendLinhaAnyDeskHtml(sb, anyDesk, anyDeskNaoSeAplica);

        AppendLinhaMaquinaHtml(sb, settings);
        AppendHtmlBreak(sb);
        AppendHtmlLine(sb, "— Observações —");
        AppendHtmlMultiline(
            sb,
            string.IsNullOrEmpty(observacoes) ? "(não indicado)" : observacoes);

        return sb.ToString();
    }

    /// <summary>Texto completo da descrição a partir dos parâmetros de campos comuns.</summary>
    public static string MontarDescricao(
        string textoProblema,
        string whatsapp,
        string nomeContato,
        string anyDesk,
        bool anyDeskNaoSeAplica,
        string observacoes,
        AppUserSettings settings) =>
        MontarDescricao(
            textoProblema,
            whatsapp,
            nomeContato,
            anyDesk,
            anyDeskNaoSeAplica,
            settings.RemotoId,
            observacoes,
            settings);

    /// <summary>Texto completo da descrição a partir do formulário.</summary>
    public static string MontarDescricao(AberturaChamadoView view, AppUserSettings settings) =>
        MontarDescricao(
            view.TextoProblema,
            view.Whatsapp,
            view.NomeContato,
            view.AnyDesk,
            view.AnyDeskNaoSeAplica,
            view.RemotoId,
            view.Observacoes,
            settings);

    static void AppendLinhaAcessoRemotoSistecHubHtml(StringBuilder sb, string? remotoId, AppUserSettings settings)
    {
        var raw = (remotoId ?? "").Trim().Replace(" ", "").Replace("-", "");
        if (raw.Length == 0)
            raw = (settings.RemotoId ?? "").Trim().Replace(" ", "").Replace("-", "");

        if (raw.Length == 0)
            return;

        var formatado = AcessoRemotoAgentService.FormatId(raw);
        AppendHtmlLine(sb, "Acesso Remoto SistecHUB: " + formatado);
    }

    static void AppendLinhaWhatsAppHtml(StringBuilder sb, string whatsapp)
    {
        sb.Append(System.Net.WebUtility.HtmlEncode("WhatsApp: "));
        var valor = (whatsapp ?? "").Trim();
        if (valor.Length == 0)
        {
            sb.Append(System.Net.WebUtility.HtmlEncode("(não indicado)"));
            AppendHtmlBreak(sb);
            return;
        }

        var url = MontarUrlWhatsApp(valor);
        if (url is null)
            sb.Append(System.Net.WebUtility.HtmlEncode(valor));
        else
            AppendHtmlAnchor(sb, url, valor);

        AppendHtmlBreak(sb);
    }

    static void AppendLinhaAnyDeskHtml(StringBuilder sb, string anyDesk, bool anyDeskNaoSeAplica)
    {
        string valor;
        if (anyDeskNaoSeAplica && string.IsNullOrEmpty(anyDesk))
            valor = "não se aplica";
        else if (string.IsNullOrEmpty(anyDesk))
            valor = "(não indicado)";
        else
            valor = anyDesk.Trim();

        AppendHtmlLine(sb, "AnyDesk: " + valor);
    }

    static void AppendLinhaAnyDeskHtml(StringBuilder sb, AberturaChamadoView view) =>
        AppendLinhaAnyDeskHtml(sb, view.AnyDesk, view.AnyDeskNaoSeAplica);

    static void AppendLinhaMaquinaHtml(StringBuilder sb, AppUserSettings settings)
    {
        var hostname = (Environment.MachineName ?? "").Trim();
        if (hostname.Length == 0)
            hostname = "(não indicado)";

        sb.Append(System.Net.WebUtility.HtmlEncode("Aberto atráves da máquina: "));
        var machineId = (settings.GlpiMachineId ?? "").Trim();
        if (int.TryParse(machineId, out var id) && id > 0)
        {
            var url = UrlMaquinaInventario + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AppendHtmlAnchor(sb, url, hostname);
        }
        else
            sb.Append(System.Net.WebUtility.HtmlEncode(hostname));

        AppendHtmlBreak(sb);
    }

    static void AppendHtmlAnchor(StringBuilder sb, string url, string displayText)
    {
        sb.Append("<a href=\"");
        sb.Append(System.Net.WebUtility.HtmlEncode(url));
        sb.Append("\" target=\"_blank\" rel=\"noopener noreferrer\">");
        sb.Append(System.Net.WebUtility.HtmlEncode(displayText));
        sb.Append("</a>");
    }

    /// <summary>Link <c>https://wa.me/</c> com DDI 55 quando o número parecer brasileiro sem país.</summary>
    static string? MontarUrlWhatsApp(string valor)
    {
        var digits = SomenteDigitos(valor);
        if (digits.Length == 0)
            return null;

        if (digits.Length is >= 10 and <= 11 && !digits.StartsWith("55", StringComparison.Ordinal))
            digits = "55" + digits;

        return "https://wa.me/" + digits;
    }

    static string SomenteDigitos(string valor)
    {
        var sb = new StringBuilder(valor.Length);
        foreach (var ch in valor)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    static void AppendHtmlLine(StringBuilder sb, string text)
    {
        sb.Append(System.Net.WebUtility.HtmlEncode(text));
        AppendHtmlBreak(sb);
    }

    static void AppendHtmlMultiline(StringBuilder sb, string text)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        sb.Append(encoded.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal));
        AppendHtmlBreak(sb);
    }

    static void AppendHtmlBreak(StringBuilder sb) => sb.Append("<br>");

    /// <summary>Gera o título curto do ticket (campo <c>name</c> no GLPI).</summary>
    public static string MontarTitulo(string textoProblema)
    {
        var linha = textoProblema.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? textoProblema.Trim();
        if (linha.Length == 0)
            linha = "Chamado SistecHub";
        if (linha.Length > TituloMaxLength)
            linha = linha[..TituloMaxLength].TrimEnd() + "…";
        return linha;
    }

    static bool GroqDisponivel(AppUserSettings settings)
    {
        try
        {
            _ = GroqClient.ResolveApiKey(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Primeira linha da resposta da IA, até 8 palavras e limite do GLPI.</summary>
    static string NormalizarTituloGeradoPelaIa(string? textoBruto, string textoProblemaFallback)
    {
        if (string.IsNullOrWhiteSpace(textoBruto))
            return MontarTitulo(textoProblemaFallback);

        var t = textoBruto.Trim().Trim('"', '\'', '«', '»', '*', '`');
        var nl = t.IndexOfAny(['\r', '\n']);
        if (nl >= 0)
            t = t[..nl].Trim();

        if (t.Length == 0)
            return MontarTitulo(textoProblemaFallback);

        var palavras = t.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (palavras.Length == 0)
            return MontarTitulo(textoProblemaFallback);

        var juntas = string.Join(' ', palavras.Take(MaxPalavrasTituloIa));
        if (juntas.Length > TituloMaxLength)
            juntas = juntas[..TituloMaxLength].TrimEnd() + "…";

        return juntas;
    }

    /// <summary>Prefixo obrigatório <c>[SistecHub]</c> + espaço + título (evita duplicar o prefixo).</summary>
    public static string AplicarPrefixoTituloSistecHub(string tituloSemPrefixo)
    {
        var t = (tituloSemPrefixo ?? "").Trim();
        while (t.StartsWith("[", StringComparison.Ordinal))
        {
            var close = t.IndexOf(']', StringComparison.Ordinal);
            if (close <= 0)
                break;
            var tag = t[..(close + 1)];
            if (!tag.Equals(PrefixoTitulo, StringComparison.OrdinalIgnoreCase))
                break;
            t = t[(close + 1)..].TrimStart();
        }

        if (t.Length == 0)
            t = "Chamado";

        var corpo = t;
        var prefixoComEspaco = PrefixoTitulo + " ";
        var disponivel = Math.Max(0, TituloMaxLength - prefixoComEspaco.Length);
        if (corpo.Length > disponivel)
            corpo = disponivel > 0 ? corpo[..disponivel].TrimEnd() + "…" : "";

        return prefixoComEspaco + corpo;
    }

    /// <summary>Gera o título com Groq quando configurado; caso contrário (ou em erro), usa <see cref="MontarTitulo"/>.</summary>
    static async Task<string> GerarTituloChamadoAsync(
        string descricaoTecnica,
        AppUserSettings settings,
        CancellationToken cancellationToken)
    {
        if (!GroqDisponivel(settings))
            return MontarTitulo(descricaoTecnica);

        try
        {
            var prompt = MontarPromptTituloIa(descricaoTecnica);
            var messages = new[] { new GroqChatMessage("user", prompt) };
            var completion = await GroqClient.CompleteChatAsync(
                    settings,
                    messages,
                    GroqClient.TitleGenerationTemperature,
                    cancellationToken)
                .ConfigureAwait(false);

            return NormalizarTituloGeradoPelaIa(completion.Content, descricaoTecnica);
        }
        catch
        {
            return MontarTitulo(descricaoTecnica);
        }
    }

    static string MontarPromptCategoriaIa(string listaCategorias, string descricao) =>
        "Com base na seguinte descrição de chamado técnico, identifique qual é a categoria que o problema mais se enquadra. Você DEVE escolher APENAS UMA categoria da lista abaixo."
        + Environment.NewLine
        + Environment.NewLine
        + "Lista de categorias disponíveis:"
        + Environment.NewLine
        + listaCategorias
        + Environment.NewLine
        + Environment.NewLine
        + "Descrição do chamado:"
        + Environment.NewLine
        + descricao.Trim()
        + Environment.NewLine
        + Environment.NewLine
        + "IMPORTANTE: Retorne apenas o número do ID da categoria (exemplo: 5), sem explicações, sem texto adicional, apenas o número.";

    /// <summary>Extrai um id presente em <paramref name="idsValidos"/> a partir da resposta da IA.</summary>
    static int? ExtrairIdCategoriaValido(string? textoBruto, HashSet<int> idsValidos)
    {
        if (string.IsNullOrWhiteSpace(textoBruto) || idsValidos.Count == 0)
            return null;

        var linha =
            (textoBruto.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? textoBruto.Trim()).Trim().Trim('"', '\'', '`');

        if (linha.Length == 0)
            return null;

        if (int.TryParse(linha, System.Globalization.NumberStyles.Integer, null, out var soNumero)
            && idsValidos.Contains(soNumero))
            return soNumero;

        foreach (Match m in Regex.Matches(linha, @"\d+"))
        {
            if (int.TryParse(m.Value, System.Globalization.NumberStyles.Integer, null, out var n) && idsValidos.Contains(n))
                return n;
        }

        return null;
    }

    /// <summary>Escolhe <c>itilcategories_id</c> via Groq; devolve <c>null</c> se não houver categorias, IA indisponível ou resposta inválida.</summary>
    static async Task<int?> ResolverCategoriaItilComIaAsync(
        string descricaoProblema,
        IReadOnlyList<GlpiItilCategoryLite> categorias,
        AppUserSettings settings,
        CancellationToken cancellationToken)
    {
        if (categorias.Count == 0 || !GroqDisponivel(settings))
            return null;

        var listaTexto = string.Join(Environment.NewLine, categorias.Select(c => $"{c.Id} - {c.Label}"));
        var prompt = MontarPromptCategoriaIa(listaTexto, descricaoProblema);
        var idsValidos = categorias.Select(c => c.Id).ToHashSet();

        try
        {
            var messages = new[] { new GroqChatMessage("user", prompt) };
            var completion = await GroqClient.CompleteChatAsync(
                    settings,
                    messages,
                    GroqClient.CategoryResolutionTemperature,
                    cancellationToken)
                .ConfigureAwait(false);

            return ExtrairIdCategoriaValido(completion.Content, idsValidos);
        }
        catch
        {
            return null;
        }
    }

    static void Reportar(IProgress<string>? progress, string mensagem) => progress?.Report(mensagem);

    /// <summary>Cria o chamado no GLPI e devolve o id do ticket.</summary>
    public static async Task<int> EnviarAsync(
        AberturaChamadoView view,
        AppUserSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Captura todos os dados dos controlos WPF/WinForms no thread de UI antes de qualquer operação assíncrona
        var textoProblema = (view.TextoProblema ?? "").Trim();
        var whatsapp = (view.Whatsapp ?? "").Trim();
        var nomeContato = (view.NomeContato ?? "").Trim();
        var anyDesk = (view.AnyDesk ?? "").Trim();
        var anyDeskNaoSeAplica = view.AnyDeskNaoSeAplica;
        var remotoId = (view.RemotoId ?? "").Trim();
        var observacoes = (view.Observacoes ?? "").Trim();
        var caminhoAnexo = view.CaminhoAnexo;

        if (string.IsNullOrWhiteSpace(textoProblema))
            throw new InvalidOperationException("Indique o relato do problema técnico antes de enviar.");

        if (string.IsNullOrWhiteSpace(whatsapp))
            throw new InvalidOperationException("Indique o número de WhatsApp.");

        if (string.IsNullOrWhiteSpace(nomeContato))
            throw new InvalidOperationException("Indique o nome de contacto.");

        if (string.IsNullOrWhiteSpace(remotoId) && !anyDeskNaoSeAplica && string.IsNullOrWhiteSpace(anyDesk))
            throw new InvalidOperationException("Indique o ID de Acesso Remoto SistecHUB, o número do AnyDesk ou marque \"Não se aplica\".");

        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            throw new InvalidOperationException("Configure o User token do GLPI em Configurações.");

        if (!int.TryParse((settings.EntityId ?? "").Trim(), out var entityId))
            throw new InvalidOperationException("Indique um Id da entidade (client id) válido em Configurações.");

        Reportar(
            progress,
            GroqDisponivel(settings) ? "Criando título com IA…" : "A preparar título…");
        var tituloBase = await GerarTituloChamadoAsync(textoProblema, settings, cancellationToken).ConfigureAwait(false);
        var titulo = AplicarPrefixoTituloSistecHub(tituloBase);
        var descricao = MontarDescricao(
            textoProblema,
            whatsapp,
            nomeContato,
            anyDesk,
            anyDeskNaoSeAplica,
            remotoId,
            observacoes,
            settings);

        Reportar(progress, "A carregar categorias do GLPI…");
        var categoriasGlpi = await GlpiApiClient.GetItilCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var categoriasParaIa = categoriasGlpi.Count <= MaxCategoriasNoPromptIa
            ? categoriasGlpi
            : categoriasGlpi.Take(MaxCategoriasNoPromptIa).ToList();
        var categoriasElegiveisIa = categoriasParaIa.Count > 0 && GroqDisponivel(settings);
        Reportar(
            progress,
            categoriasElegiveisIa ? "Selecionando categoria com IA…" : "A processar categoria do chamado…");
        var categoriaItilId =
            await ResolverCategoriaItilComIaAsync(textoProblema, categoriasParaIa, settings, cancellationToken)
                .ConfigureAwait(false);

        Reportar(progress, "A localizar o requerente no GLPI…");
        var requerenteId = await GlpiApiClient.GetUserIdByLoginAsync(settings, LoginRequerenteGlpi, cancellationToken)
            .ConfigureAwait(false);
        if (requerenteId is null)
        {
            throw new InvalidOperationException(
                $"Não foi encontrado no GLPI um utilizador com o login «{LoginRequerenteGlpi}». "
                + "Crie esse utilizador (ou ajuste o login) para que o chamado possa ser aberto com o requerente correto.");
        }

        Reportar(progress, "Enviando chamado…");
        await Task.Yield();
        Reportar(progress, "Aguardando servidor…");
        var ticketId = await GlpiApiClient.CreateTicketAsync(
                settings,
                entityId,
                titulo,
                descricao,
                requerenteId.Value,
                categoriaItilId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(caminhoAnexo) && File.Exists(caminhoAnexo))
        {
            Reportar(progress, "A enviar anexo, aguarde o servidor…");
            await GlpiApiClient.UploadTicketAttachmentAsync(settings, entityId, ticketId, caminhoAnexo, cancellationToken)
                .ConfigureAwait(false);
        }

        return ticketId;
    }
}
