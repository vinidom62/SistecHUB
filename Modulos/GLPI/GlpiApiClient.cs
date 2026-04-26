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

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CompleteName) ? Name : CompleteName.Trim();
}

/// <summary>Categoria ITIL (chamado) devolvida pelo GLPI para escolha via IA.</summary>
public sealed record GlpiItilCategoryLite(int Id, string Label);

/// <summary>Cliente HTTP para a API REST do GLPI (sessão + consulta de entidade).</summary>
public static class GlpiApiClient
{
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
        if (string.IsNullOrWhiteSpace(settings.GlpiAppToken) || string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            throw new InvalidOperationException("Configure o App token e o User token do GLPI nas configurações.");
    }

    static async Task<T> ExecuteWithGlpiSessionAsync<T>(
        AppUserSettings settings,
        Func<HttpClient, string, string, string, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        EnsureGlpiCredentials(settings);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var apiRoot = NormalizeApiRoot(settings.GlpiBaseUrl);
        var appToken = settings.GlpiAppToken.Trim();
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

    /// <summary>Uma sessão: carrega a entidade e as contagens de chamados por estado para essa entidade.</summary>
    public static Task<(GlpiEntityInfo Entity, GlpiTicketCounts Counts)> GetEntityAndTicketCountsAsync(
        AppUserSettings settings,
        int entityId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithGlpiSessionAsync(
            settings,
            async (http, apiRoot, appToken, session, ct) =>
            {
                var entity = await FetchEntityAsync(http, apiRoot, appToken, session, entityId, ct)
                    .ConfigureAwait(false);
                var counts = await FetchTicketCountsForEntityAsync(
                        http,
                        apiRoot,
                        appToken,
                        session,
                        entityId,
                        ct)
                    .ConfigureAwait(false);
                return (entity, counts);
            },
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
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
            return id;

        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
        {
            var first = root[0];
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var idEl2) && idEl2.TryGetInt32(out var id2))
                return id2;
        }

        throw new InvalidOperationException($"Resposta do GLPI sem id ({contexto}).");
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
