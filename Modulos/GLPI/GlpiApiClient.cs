using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SistecHub.Core;

namespace SistecHub.Modulos.GLPI;

/// <summary>Resposta mínima de <c>GET Entity/:id</c> na API REST do GLPI.</summary>
public sealed class GlpiEntityInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string CompleteName { get; init; } = "";

    /// <summary>Texto exibido no ComboBox de entidades (sanitizado para WinForms).</summary>
    public string PickerLabel { get; init; } = "";

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CompleteName) ? Name : CompleteName.Trim();

    public override string ToString() =>
        string.IsNullOrWhiteSpace(PickerLabel) ? DisplayName : PickerLabel;

    /// <summary>Último segmento do caminho da entidade (ex.: ignora "Sistec Sistemas &gt; ").</summary>
    public string LeafDisplayName
    {
        get
        {
            var full = DisplayName.Trim();
            if (full.Length == 0)
                return Name.Trim();

            var idx = full.LastIndexOf('>');
            if (idx < 0 || idx >= full.Length - 1)
                return full;

            return full[(idx + 1)..].Trim();
        }
    }
}

/// <summary>Categoria ITIL (chamado) devolvida pelo GLPI para escolha via IA.</summary>
public sealed record GlpiItilCategoryLite(int Id, string Label);

/// <summary>Cliente HTTP para a API REST do GLPI (sessão + consulta de entidade).</summary>
public static class GlpiApiClient
{
    const string BaseUrl = "https://angelus.sisteconsultoria.com.br/angelus";
    const string AppToken = "HIdUB6NQVzatXVpLNlQCSZAUKtMhVQm97mRHErZ8";

    /// <summary>Login GLPI da conta de serviço usada pela API.</summary>
    public const string ServiceAccountLogin = "sistechub";

    static string NormalizeApiRoot(string? baseUrl)
    {
        var t = (baseUrl ?? "").Trim().TrimEnd('/');
        if (t.Length == 0)
            throw new InvalidOperationException("A URL base do GLPI não está configurada.");

        if (t.EndsWith("/apirest.php", StringComparison.OrdinalIgnoreCase))
            return t;

        return t + "/apirest.php";
    }

    static void EnsureGlpiCredentials(AppUserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            throw new InvalidOperationException("Configure o User token do GLPI nas configurações.");
    }

    static async Task<T> ExecuteWithGlpiSessionAsync<T>(
        AppUserSettings settings,
        Func<HttpClient, string, string, string, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        EnsureGlpiCredentials(settings);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var apiRoot = NormalizeApiRoot(BaseUrl);
        var appToken = AppToken;
        var userToken = settings.GlpiUserToken.Trim();
        var sessionToken = await InitSessionAsync(http, apiRoot, appToken, userToken, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await action(http, apiRoot, appToken, sessionToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await TryKillSessionAsync(http, apiRoot, appToken, sessionToken, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Uma sessão: carrega a entidade, contagens por estado e a primeira página de chamados.</summary>
    public static Task<(GlpiEntityInfo Entity, GlpiTicketCounts Counts, GlpiTicketListPage TicketsPage)> GetEntityAndTicketCountsAsync(
        AppUserSettings settings,
        int entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            async (http, apiRoot, appToken, session, ct) =>
            {
                var entity = await FetchEntityAsync(http, apiRoot, appToken, session, entityId, ct)
                    .ConfigureAwait(false);
                var countsTask = FetchTicketCountsForEntityAsync(
                    http,
                    apiRoot,
                    appToken,
                    session,
                    entityId,
                    ct);
                var ticketsTask = FetchTicketsPageForEntityAsync(
                    http,
                    apiRoot,
                    appToken,
                    session,
                    entityId,
                    pageIndex: 0,
                    pageSize: GlpiTicketPagination.DefaultPageSize,
                    ct);
                await Task.WhenAll(countsTask, ticketsTask).ConfigureAwait(false);
                return (entity, await countsTask.ConfigureAwait(false), await ticketsTask.ConfigureAwait(false));
            },
            cancellationToken);

    /// <summary>Lista uma página de chamados da entidade (10 por página, ordenados do mais recente).</summary>
    public static Task<GlpiTicketListPage> GetTicketsPageAsync(
        AppUserSettings settings,
        int entityId,
        int pageIndex,
        int pageSize = GlpiTicketPagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            (http, apiRoot, appToken, session, ct) =>
                FetchTicketsPageForEntityAsync(http, apiRoot, appToken, session, entityId, pageIndex, pageSize, ct),
            cancellationToken);

    /// <summary>Verifica se o user token permite abrir sessão na API REST do GLPI.</summary>
    public static Task ValidateGlpiUserTokenAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            static (_, _, _, _, _) => Task.FromResult(true),
            cancellationToken);

    /// <summary>
    /// Verifica se os tokens e o <see cref="AppUserSettings.EntityId"/> permitem sessão GLPI
    /// e leitura da <c>Entity</c> indicada (mesma lógica de uso no arranque).
    /// </summary>
    public static Task ValidateGlpiAndEntityAsync(AppUserSettings settings, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(settings.EntityId?.Trim(), out var eid) || eid < 1)
        {
            throw new InvalidOperationException(
                "O ID da entidade tem de ser um número inteiro (ex.: o id da entidade no GLPI).");
        }

        return GetEntityAndTicketCountsAsync(settings, eid, cancellationToken);
    }

    /// <summary>Consulta apenas os dados da entidade no GLPI.</summary>
    public static Task<GlpiEntityInfo> GetEntityAsync(
        AppUserSettings settings,
        int entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            (http, apiRoot, appToken, session, ct) =>
                FetchEntityAsync(http, apiRoot, appToken, session, entityId, ct),
            cancellationToken);

    /// <summary>Lista entidades acessíveis à sessão GLPI.</summary>
    public static Task<IReadOnlyList<GlpiEntityInfo>> GetEntitiesAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            FetchAllEntitiesAsync,
            cancellationToken);

    /// <summary>Obtém a chave API Groq configurada no plugin <c>PluginGroqSistechubconfig</c>.</summary>
    public static Task<string> GetGroqApiKeyFromPluginAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            FetchGroqApiKeyFromPluginAsync,
            cancellationToken);

    /// <summary>Obtém o <c>id</c> do utilizador GLPI pelo login (<c>glpi_users.name</c>).</summary>
    public static Task<int?> GetUserIdByLoginAsync(
        AppUserSettings settings,
        string login,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            (http, apiRoot, appToken, session, ct) =>
                FindUserIdByLoginAsync(http, apiRoot, appToken, session, login, ct),
            cancellationToken);

    /// <summary>Lista categorias ITIL acessíveis à sessão (filtradas para incidente quando o campo existir).</summary>
    public static Task<List<GlpiItilCategoryLite>> GetItilCategoriesAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            (http, apiRoot, appToken, session, ct) =>
                FetchItilCategoriesAsync(http, apiRoot, appToken, session, ct),
            cancellationToken);

    /// <summary>Cria um <c>Ticket</c> na entidade indicada (tipo incidente), com requerente explícito.</summary>
    public static Task<int> CreateTicketAsync(
        AppUserSettings settings,
        int entitiesId,
        string name,
        string content,
        int usersIdRequester,
        int? itilcategoriesId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            (http, apiRoot, appToken, session, ct) =>
                PostTicketAsync(
                    http,
                    apiRoot,
                    appToken,
                    session,
                    entitiesId,
                    name,
                    content,
                    usersIdRequester,
                    itilcategoriesId,
                    ct),
            cancellationToken);

    /// <summary>Envia um ficheiro local como <c>Document</c> do GLPI e associa-o ao chamado (<c>Document_Item</c>).</summary>
    public static async Task UploadTicketAttachmentAsync(
        AppUserSettings settings,
        int entitiesId,
        int ticketId,
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithGlpiSessionAsync(
                settings,
                async (http, apiRoot, appToken, session, ct) =>
                {
                    var docId = await PostDocumentMultipartAsync(
                            http,
                            apiRoot,
                            appToken,
                            session,
                            entitiesId,
                            localFilePath,
                            ct)
                        .ConfigureAwait(false);
                    await PostDocumentItemLinkAsync(http, apiRoot, appToken, session, docId, ticketId, ct)
                        .ConfigureAwait(false);
                    return docId;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Em muitos servidores Apache + PHP-FPM o cabeçalho <c>Authorization</c> não é repassado ao PHP
    /// (documentação GLPI: usar <c>user_token</c> / credenciais na query string como alternativa).
    /// Por isso o login usa sempre <c>app_token</c> e <c>user_token</c> na URL, além dos cabeçalhos quando existirem.
    /// </summary>
    static async Task<string> InitSessionAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string userToken,
        CancellationToken cancellationToken)
    {
        var url =
            $"{apiRoot}/initSession"
            + $"?app_token={Uri.EscapeDataString(appToken)}"
            + $"&user_token={Uri.EscapeDataString(userToken)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Authorization", "user_token " + userToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "sessão GLPI");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("session_token", out var stEl))
            throw new InvalidOperationException("Resposta do GLPI sem session_token.");

        var token = stEl.GetString();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("session_token vazio na resposta do GLPI.");

        return token;
    }

    static async Task<GlpiEntityInfo> FetchEntityAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entityId,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiRoot}/Entity/{entityId}");
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Não existe entidade com o id {entityId} no GLPI.");

        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "consulta Entity");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var parsedId)
            ? parsedId
            : entityId;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var complete = root.TryGetProperty("completename", out var c) ? c.GetString() ?? "" : "";

        return new GlpiEntityInfo { Id = id, Name = name, CompleteName = complete };
    }

    static async Task<IReadOnlyList<GlpiEntityInfo>> FetchAllEntitiesAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var list = new List<GlpiEntityInfo>();
        const int pageSize = 200;

        for (var start = 0; start < 10_000; start += pageSize)
        {
            var end = start + pageSize - 1;
            var url = $"{apiRoot}/Entity?range={start}-{end}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddJsonContentType(req);
            req.Headers.TryAddWithoutValidation("App-Token", appToken);
            req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "listar Entity");

            var page = ParseEntityListJson(body);
            if (page.Count == 0)
                break;

            list.AddRange(page);

            if (page.Count < pageSize)
                break;
        }

        return list
            .Where(ShouldIncludeInEntityPicker)
            .Select(FormatEntityForPicker)
            .OrderBy(static e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    const string EntityPickerRootPrefix = "Sistec Sistemas";

    static bool ShouldIncludeInEntityPicker(GlpiEntityInfo entity) =>
        !IsDescendantOfAvulsoClientsEntity(entity);

    static bool IsDescendantOfAvulsoClientsEntity(GlpiEntityInfo entity)
    {
        var path = string.IsNullOrWhiteSpace(entity.CompleteName) ? entity.Name : entity.CompleteName;
        var segments = path.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var avulsoIndex = -1;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Contains("avulso", StringComparison.OrdinalIgnoreCase)
                && segment.Contains("xclient", StringComparison.OrdinalIgnoreCase))
            {
                avulsoIndex = i;
                break;
            }
        }

        return avulsoIndex >= 0 && avulsoIndex < segments.Length - 1;
    }

    static GlpiEntityInfo FormatEntityForPicker(GlpiEntityInfo entity)
    {
        var path = string.IsNullOrWhiteSpace(entity.CompleteName) ? entity.Name : entity.CompleteName;
        var label = SanitizeEntityPickerLabel(StripEntityPickerRootPrefix(path));
        var name = SanitizeEntityPickerLabel(entity.Name);

        return new GlpiEntityInfo
        {
            Id = entity.Id,
            Name = name,
            CompleteName = label,
            PickerLabel = label,
        };
    }

    internal static string SanitizeEntityPickerLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decoded = WebUtility.HtmlDecode(value.Trim());
        return decoded.Replace("&", "&&", StringComparison.Ordinal);
    }

    static string StripEntityPickerRootPrefix(string path)
    {
        var trimmed = path.Trim();
        if (!trimmed.StartsWith(EntityPickerRootPrefix, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        trimmed = trimmed[EntityPickerRootPrefix.Length..].TrimStart();
        if (trimmed.StartsWith('>'))
            trimmed = trimmed[1..].TrimStart();

        return trimmed;
    }

    static async Task<string> FetchGroqApiKeyFromPluginAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiRoot}/PluginGroqSistechubconfig/1");
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Configuração Groq não encontrada no GLPI (plugin SistecHub).");
        }

        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "configuração Groq (plugin)");

        var apiKey = TryExtractGroqApiKeyFromPluginJson(body);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "O plugin Groq no GLPI não devolveu uma chave API válida.");
        }

        return apiKey.Trim();
    }

    static string? TryExtractGroqApiKeyFromPluginJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return FindGroqApiKeyInElement(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    static string? FindGroqApiKeyInElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (LooksLikeGroqApiKey(value) && IsGroqKeyPropertyName(property.Name))
                            return value;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindGroqApiKeyInElement(property.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindGroqApiKeyInElement(item);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }

                break;
        }

        return null;
    }

    static bool IsGroqKeyPropertyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains("groq", StringComparison.OrdinalIgnoreCase)
            && name.Contains("key", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Equals("api_key", StringComparison.OrdinalIgnoreCase)
            || name.Equals("apikey", StringComparison.OrdinalIgnoreCase)
            || name.Equals("key", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeGroqApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.StartsWith("gsk_", StringComparison.OrdinalIgnoreCase)
            || trimmed.Length >= 20;
    }

    static List<GlpiEntityInfo> ParseEntityListJson(string body)
    {
        var result = new List<GlpiEntityInfo>();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
            array = root;
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("data", out var data)
                 && data.ValueKind == JsonValueKind.Array)
            array = data;
        else
            return result;

        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id) || id < 1)
                continue;

            var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var complete = el.TryGetProperty("completename", out var c) ? c.GetString() ?? "" : "";

            result.Add(new GlpiEntityInfo { Id = id, Name = name, CompleteName = complete });
        }

        return result;
    }

    /// <summary>
    /// Pesquisa <c>Ticket</c> com critérios entidade (80) + estado (12); devolve <c>totalcount</c>.
    /// Na entidade usa <c>under</c>: inclui a entidade configurada e <b>todas as entidades filhas</b> na hierarquia GLPI.
    /// </summary>
    static async Task<int> SearchTicketTotalAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entityId,
        int status,
        CancellationToken cancellationToken)
    {
        var url =
            $"{apiRoot}/search/Ticket"
            + "?criteria[0][link]=AND"
            + "&criteria[0][itemtype]=Ticket"
            + "&criteria[0][field]=80"
            + "&criteria[0][searchtype]=under"
            + $"&criteria[0][value]={entityId}"
            + "&criteria[1][link]=AND"
            + "&criteria[1][itemtype]=Ticket"
            + "&criteria[1][field]=12"
            + "&criteria[1][searchtype]=equals"
            + $"&criteria[1][value]={status}"
            + "&range=0-0";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "pesquisa de chamados (Ticket)");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("totalcount", out var tc))
            return 0;

        if (tc.ValueKind == JsonValueKind.Number && tc.TryGetInt32(out var n))
            return n;

        if (tc.ValueKind == JsonValueKind.String && int.TryParse(tc.GetString(), System.Globalization.NumberStyles.Integer, null, out var ns))
            return ns;

        return 0;
    }

    static async Task<int?> FindUserIdByLoginAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        string login,
        CancellationToken cancellationToken)
    {
        var needle = login.Trim();
        if (needle.Length == 0)
            return null;

        const int pageSize = 200;
        for (var start = 0; start < 10_000; start += pageSize)
        {
            var end = start + pageSize - 1;
            var url = $"{apiRoot}/User?range={start}-{end}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddJsonContentType(req);
            req.Headers.TryAddWithoutValidation("App-Token", appToken);
            req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                ThrowIfGlpiError(body, false, (int)resp.StatusCode, "listar User");
                break;
            }

            ThrowIfGlpiError(body, true, (int)resp.StatusCode, "listar User");

            var found = TryFindUserIdInUserListJson(body, needle, out var rowCount);
            if (found.HasValue)
                return found.Value;

            if (rowCount < pageSize)
                break;
        }

        return await SearchUserIdByLoginFallbackAsync(http, apiRoot, appToken, sessionToken, needle, cancellationToken)
            .ConfigureAwait(false);
    }

    static int? TryFindUserIdInUserListJson(string body, string needle, out int rowCount)
    {
        rowCount = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            JsonElement list;
            if (root.ValueKind == JsonValueKind.Array)
                list = root;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                     && data.ValueKind == JsonValueKind.Array)
                list = data;
            else
                return null;

            rowCount = list.GetArrayLength();
            foreach (var el in list.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                if (!el.TryGetProperty("name", out var nameEl))
                    continue;
                var name = nameEl.GetString();
                if (name is null || !name.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                    return id;
            }
        }
        catch
        {
            /* ignorar parse */
        }

        return null;
    }

    static async Task<int?> SearchUserIdByLoginFallbackAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        string needle,
        CancellationToken cancellationToken)
    {
        var url =
            $"{apiRoot}/search/User"
            + "?criteria[0][link]=AND"
            + "&criteria[0][itemtype]=User"
            + "&criteria[0][field]=1"
            + "&criteria[0][searchtype]=contains"
            + $"&criteria[0][value]={Uri.EscapeDataString(needle)}"
            + "&range=0-4";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        ThrowIfGlpiError(body, true, (int)resp.StatusCode, "pesquisa User");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0)
                return null;

            var row = data[0];
            if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() > 0 && row[0].ValueKind == JsonValueKind.Number
                && row[0].TryGetInt32(out var idFromRow))
                return idFromRow;

            if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idOb))
                return idOb;
        }
        catch
        {
            /* ignorar */
        }

        return null;
    }

    static async Task<GlpiTicketListPage> FetchTicketsPageForEntityAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entityId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        pageIndex = Math.Max(0, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var start = pageIndex * pageSize;
        var end = start + pageSize - 1;

        var url =
            $"{apiRoot}/search/Ticket"
            + "?criteria[0][link]=AND"
            + "&criteria[0][itemtype]=Ticket"
            + "&criteria[0][field]=80"
            + "&criteria[0][searchtype]=under"
            + $"&criteria[0][value]={entityId}"
            + "&forcedisplay[0]=2"
            + "&forcedisplay[1]=1"
            + "&forcedisplay[2]=12"
            + "&forcedisplay[3]=19"
            + "&sort=19"
            + "&order=DESC"
            + "&is_deleted=0"
            + $"&range={start}-{end}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddJsonContentType(req);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "listar chamados (Ticket)");

        return ParseTicketSearchResponse(body);
    }

    static GlpiTicketListPage ParseTicketSearchResponse(string body)
    {
        var tickets = new List<GlpiTicketSummary>();
        var totalCount = 0;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("totalcount", out var totalEl))
            totalCount = TryGetJsonInt32(totalEl) ?? 0;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in data.EnumerateArray())
            {
                if (TryParseTicketSearchRow(row, out var ticket))
                    tickets.Add(ticket);
            }
        }

        return new GlpiTicketListPage(tickets, totalCount);
    }

    static bool TryParseTicketSearchRow(JsonElement row, out GlpiTicketSummary ticket)
    {
        ticket = default!;

        int? id;
        string title;
        int status;
        DateTime? openedAt;

        // GLPI Ticket: 1 = título, 2 = id, 12 = status.
        if (row.ValueKind == JsonValueKind.Object)
        {
            var field1Int = TryGetSearchFieldInt(row, 1);
            var field2Int = TryGetSearchFieldInt(row, 2);
            var field1Str = TryGetSearchFieldString(row, 1);
            var field2Str = TryGetSearchFieldString(row, 2);

            if (field2Int is > 0)
            {
                id = field2Int;
                title = field1Str ?? "";
            }
            else if (field1Int is > 0)
            {
                id = field1Int;
                title = field2Str ?? "";
            }
            else
                return false;

            status = TryGetSearchFieldStatus(row);
            openedAt = TryGetSearchFieldDate(row, 19) ?? TryGetSearchFieldDate(row, 15);
        }
        else if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 3)
        {
            id = TryGetJsonInt32(row[0]);
            title = row[1].ValueKind == JsonValueKind.String ? row[1].GetString() ?? "" : row[1].ToString();
            status = TryGetJsonInt32(row[2]) ?? 0;
            openedAt = row.GetArrayLength() > 3 ? TryParseGlpiDateTime(row[3]) : null;
        }
        else
            return false;

        if (id is null or < 1)
            return false;

        ticket = new GlpiTicketSummary(id.Value, title.Trim(), status, openedAt);
        return true;
    }

    static int? TryGetSearchFieldInt(JsonElement row, int fieldId)
    {
        if (!TryGetSearchFieldElement(row, fieldId, out var el))
            return null;
        return TryGetJsonInt32(el);
    }

    static string? TryGetSearchFieldString(JsonElement row, int fieldId)
    {
        if (!TryGetSearchFieldElement(row, fieldId, out var el))
            return null;

        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.ToString(),
        };
    }

    static DateTime? TryGetSearchFieldDate(JsonElement row, int fieldId)
    {
        if (!TryGetSearchFieldElement(row, fieldId, out var el))
            return null;
        return TryParseGlpiDateTime(el);
    }

    static int TryGetSearchFieldStatus(JsonElement row)
    {
        if (!TryGetSearchFieldElement(row, 12, out var el))
            return 0;

        var asInt = TryGetJsonInt32(el);
        if (asInt is > 0)
            return asInt.Value;

        if (el.ValueKind != JsonValueKind.String)
            return 0;

        var label = (el.GetString() ?? "").Trim();
        return label.ToLowerInvariant() switch
        {
            "novo" or "new" => 1,
            "atribuído" or "atribuido" or "assigned" or "processing (assigned)" => 2,
            "planeado" or "planned" => 3,
            "pendente" or "pending" or "waiting" or "em espera" => 4,
            "resolvido" or "solved" => 5,
            "fechado" or "closed" => 6,
            _ => 0,
        };
    }

    static bool TryGetSearchFieldElement(JsonElement row, int fieldId, out JsonElement el)
    {
        if (row.TryGetProperty(fieldId.ToString(), out el))
            return true;

        if (row.TryGetProperty("Ticket." + fieldId, out el))
            return true;

        el = default;
        return false;
    }

    static int? TryGetJsonInt32(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), System.Globalization.NumberStyles.Integer, null, out var ns))
            return ns;
        return null;
    }

    static DateTime? TryParseGlpiDateTime(JsonElement el)
    {
        string? raw = el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
            return dt;

        if (DateTime.TryParse(raw, out dt))
            return dt;

        return null;
    }

    static async Task<GlpiTicketCounts> FetchTicketCountsForEntityAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entityId,
        CancellationToken cancellationToken)
    {
        // GLPI: 1 Novo, 2 Atribuído (em curso), 3 Planeado, 4 Em espera, 5 Resolvido, 6 Fechado
        var novosT = SearchTicketTotalAsync(http, apiRoot, appToken, sessionToken, entityId, 1, cancellationToken);
        var pendentesT = SearchTicketTotalAsync(http, apiRoot, appToken, sessionToken, entityId, 4, cancellationToken);
        var atribuidosT = SearchTicketTotalAsync(http, apiRoot, appToken, sessionToken, entityId, 2, cancellationToken);
        var planejadosT = SearchTicketTotalAsync(http, apiRoot, appToken, sessionToken, entityId, 3, cancellationToken);
        var fechadosT = SearchTicketTotalAsync(http, apiRoot, appToken, sessionToken, entityId, 6, cancellationToken);

        await Task.WhenAll(novosT, pendentesT, atribuidosT, planejadosT, fechadosT).ConfigureAwait(false);

        return new GlpiTicketCounts(
            await novosT.ConfigureAwait(false),
            await pendentesT.ConfigureAwait(false),
            await atribuidosT.ConfigureAwait(false),
            await planejadosT.ConfigureAwait(false),
            await fechadosT.ConfigureAwait(false));
    }

    static async Task<List<GlpiItilCategoryLite>> FetchItilCategoriesAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var acc = new List<GlpiItilCategoryLite>();
        var seen = new HashSet<int>();
        const int pageSize = 200;

        for (var start = 0; start < 20_000; start += pageSize)
        {
            var end = start + pageSize - 1;
            var url = $"{apiRoot}/ITILCategory?range={start}-{end}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddJsonContentType(req);
            req.Headers.TryAddWithoutValidation("App-Token", appToken);
            req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                ThrowIfGlpiError(body, false, (int)resp.StatusCode, "listar ITILCategory");
                break;
            }

            ThrowIfGlpiError(body, true, (int)resp.StatusCode, "listar ITILCategory");

            var rowCount = 0;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                JsonElement list;
                if (root.ValueKind == JsonValueKind.Array)
                    list = root;
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                         && data.ValueKind == JsonValueKind.Array)
                    list = data;
                else
                    break;

                rowCount = list.GetArrayLength();
                foreach (var el in list.EnumerateArray())
                    TryAddItilCategoryIfIncident(el, acc, seen);
            }
            catch
            {
                break;
            }

            if (rowCount < pageSize)
                break;
        }

        acc.Sort((a, b) => a.Id.CompareTo(b.Id));
        return acc;
    }

    static void TryAddItilCategoryIfIncident(JsonElement el, List<GlpiItilCategoryLite> acc, HashSet<int> seen)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return;

        if (el.TryGetProperty("is_incident", out var inc))
        {
            if (inc.ValueKind == JsonValueKind.Number && inc.TryGetInt32(out var inv) && inv == 0)
                return;
            if (inc.ValueKind == JsonValueKind.False)
                return;
        }

        if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id) || id <= 0)
            return;

        if (!seen.Add(id))
            return;

        var label = "";
        if (el.TryGetProperty("completename", out var comp) && comp.ValueKind == JsonValueKind.String)
            label = (comp.GetString() ?? "").Trim();
        if (label.Length == 0 && el.TryGetProperty("name", out var nam) && nam.ValueKind == JsonValueKind.String)
            label = (nam.GetString() ?? "").Trim();
        if (label.Length == 0)
            label = "Categoria #" + id;

        acc.Add(new GlpiItilCategoryLite(id, label));
    }

    static async Task<int> PostTicketAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entitiesId,
        string name,
        string content,
        int usersIdRequester,
        int? itilcategoriesId,
        CancellationToken cancellationToken)
    {
        var input = new Dictionary<string, object>
        {
            ["name"] = name,
            ["content"] = content,
            ["entities_id"] = entitiesId,
            ["type"] = 1,
            ["_users_id_requester"] = usersIdRequester,
        };

        if (itilcategoriesId is > 0)
            input["itilcategories_id"] = itilcategoriesId.Value;

        var payload = new Dictionary<string, object> { ["input"] = input };

        var json = JsonSerializer.Serialize(payload);
        var url = $"{apiRoot}/Ticket";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "criar Ticket");

        try
        {
            return ParseCreatedIdFromBody(body, "Ticket");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("Resposta do GLPI sem id do chamado criado.");
        }
    }

    const long MaxAnexoBytes = 8 * 1024 * 1024;

    static async Task<int> PostDocumentMultipartAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int entitiesId,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new InvalidOperationException("O ficheiro a anexar não existe ou não está acessível.");

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
            throw new InvalidOperationException("Nome de ficheiro inválido para anexo.");

        var length = new FileInfo(filePath).Length;
        if (length > MaxAnexoBytes)
            throw new InvalidOperationException($"O ficheiro excede o limite de {MaxAnexoBytes / (1024 * 1024)} MB para envio ao GLPI.");

        var docName = "Anexo SistecHub — " + fileName;
        if (docName.Length > 240)
            docName = docName[..237] + "…";

        var manifestInput = new Dictionary<string, object>
        {
            ["name"] = docName,
            ["entities_id"] = entitiesId,
            ["_filename"] = new[] { fileName },
        };
        var manifestJson = JsonSerializer.Serialize(new Dictionary<string, object> { ["input"] = manifestInput });

        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

        using var multipart = new MultipartFormDataContent();
        var manifestPart = new StringContent(manifestJson, Encoding.UTF8);
        manifestPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipart.Add(manifestPart, "uploadManifest");

        var filePart = new ByteArrayContent(fileBytes);
        filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(filePart, "filename[0]", fileName);

        var url = $"{apiRoot}/Document";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
        req.Content = multipart;

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "enviar Document (anexo)");

        return ParseCreatedIdFromBody(body, "Document");
    }

    static async Task PostDocumentItemLinkAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int documentId,
        int ticketId,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["input"] = new Dictionary<string, object>
            {
                ["documents_id"] = documentId,
                ["items_id"] = ticketId,
                ["itemtype"] = "Ticket",
            },
        };

        var json = JsonSerializer.Serialize(payload);
        var url = $"{apiRoot}/Document_Item";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfGlpiError(body, resp.IsSuccessStatusCode, (int)resp.StatusCode, "associar Document ao Ticket");
    }

    static int ParseCreatedIdFromBody(string body, string contexto)
    {
        if (!TryGetCreatedIdElement(body, out var id))
            throw new InvalidOperationException($"Resposta do GLPI sem id ({contexto}).");
        return id;
    }

    static bool TryGetCreatedIdElement(string body, out int id)
    {
        id = 0;
        using var doc = JsonDocument.Parse(body);
        return TryGetCreatedIdInElement(doc.RootElement, out id);
    }

    static bool TryGetCreatedIdInElement(JsonElement el, out int id)
    {
        id = 0;
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("id", out var idEl) && TryGetJsonIdAsInt32(idEl, out id))
                return true;
            foreach (var sub in new[] { "data", "message", "item", "result" })
            {
                if (el.TryGetProperty(sub, out var nest) && nest.ValueKind == JsonValueKind.Object &&
                    nest.TryGetProperty("id", out var idNested) && TryGetJsonIdAsInt32(idNested, out id))
                    return true;
            }
        }
        if (el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > 0)
        {
            var first = el[0];
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var idEl2) &&
                TryGetJsonIdAsInt32(idEl2, out id))
                return true;
        }
        return false;
    }

    static bool TryGetJsonIdAsInt32(JsonElement idEl, out int id)
    {
        if (idEl.TryGetInt32(out id))
            return true;
        if (idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out id))
            return true;
        id = 0;
        return false;
    }

    static async Task TryKillSessionAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiRoot}/killSession");
            AddJsonContentType(req);
            req.Headers.TryAddWithoutValidation("App-Token", appToken);
            req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            _ = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            /* ignorar falhas ao fechar sessão */
        }
    }

    static void ThrowIfGlpiError(string body, bool success, int statusCode, string contexto)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    if (first.ValueKind == JsonValueKind.String)
                        throw new InvalidOperationException(first.GetString() ?? "Erro devolvido pelo GLPI.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                /* ignorar parse */
            }
        }

        if (!success)
            throw new InvalidOperationException(
                $"Falha na {contexto} (HTTP {statusCode}). Resposta: {Truncate(body, 400)}");
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    /// <summary>A API GLPI exige este cabeçalho em todos os pedidos.</summary>
    static void AddJsonContentType(HttpRequestMessage req) =>
        req.Headers.TryAddWithoutValidation("Content-Type", "application/json");
}
