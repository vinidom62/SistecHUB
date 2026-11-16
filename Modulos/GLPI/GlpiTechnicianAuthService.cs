using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SistecHub.Modulos.GLPI;

/// <summary>Perfil completo do técnico obtido no GLPI após login bem-sucedido.</summary>
public sealed class GlpiTechnicianProfile
{
    public int UserId { get; init; }
    public string Login { get; init; } = "";
    public string FullName { get; init; } = "";
    public string AvatarBase64 { get; init; } = "";
}

/// <summary>Serviço de autenticação e obtenção de perfil do técnico no GLPI.</summary>
public static class GlpiTechnicianAuthService
{
    const string BaseUrl = "https://angelus.sisteconsultoria.com.br/angelus";
    const string AppToken = "HIdUB6NQVzatXVpLNlQCSZAUKtMhVQm97mRHErZ8";

    static string NormalizeApiRoot(string? baseUrl)
    {
        var t = (baseUrl ?? "").Trim().TrimEnd('/');
        if (t.Length == 0)
            throw new InvalidOperationException("A URL base do GLPI não está configurada.");

        if (t.EndsWith("/apirest.php", StringComparison.OrdinalIgnoreCase))
            return t;

        return t + "/apirest.php";
    }

    /// <summary>
    /// Autentica o técnico via usuário e senha no GLPI, captura o nome completo e a foto de perfil.
    /// </summary>
    public static async Task<GlpiTechnicianProfile> AuthenticateAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default)
    {
        var cleanLogin = (login ?? "").Trim();
        var cleanPassword = (password ?? "").Trim();

        if (string.IsNullOrWhiteSpace(cleanLogin))
            throw new ArgumentException("Informe o usuário do GLPI.", nameof(login));

        if (string.IsNullOrWhiteSpace(cleanPassword))
            throw new ArgumentException("Informe a senha do GLPI.", nameof(password));

        var apiRoot = NormalizeApiRoot(BaseUrl);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

        // 1. Iniciar Sessão com credenciais Basic Auth
        var sessionToken = await InitSessionWithCredentialsAsync(http, apiRoot, AppToken, cleanLogin, cleanPassword, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            // 2. Obter dados da sessão do usuário autenticado
            var (userId, rawName, firstName, realName) = await FetchFullSessionUserInfoAsync(http, apiRoot, AppToken, sessionToken, cancellationToken)
                .ConfigureAwait(false);

            // Montar nome completo (Primeiro Nome + Sobrenome, ou login se vazio)
            var nameParts = new[] { firstName?.Trim(), realName?.Trim() }.Where(s => !string.IsNullOrEmpty(s));
            var fullName = string.Join(" ", nameParts).Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                fullName = !string.IsNullOrWhiteSpace(rawName) ? rawName.Trim() : cleanLogin;

            // 3. Capturar foto de perfil (se houver)
            var avatarBase64 = "";
            if (userId > 0)
            {
                try
                {
                    avatarBase64 = await FetchUserPictureBase64Async(http, apiRoot, AppToken, sessionToken, userId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    avatarBase64 = "";
                }
            }

            return new GlpiTechnicianProfile
            {
                UserId = userId,
                Login = cleanLogin,
                FullName = fullName,
                AvatarBase64 = avatarBase64,
            };
        }
        finally
        {
            // 4. Encerrar sessão temporária da API
            await TryKillSessionAsync(http, apiRoot, AppToken, sessionToken, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    static async Task<string> InitSessionWithCredentialsAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var url = $"{apiRoot}/initSession";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{password}"));
        req.Headers.TryAddWithoutValidation("Authorization", "Basic " + basicAuth);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            if (body.Contains("ERROR_GLPI_LOGIN", StringComparison.OrdinalIgnoreCase)
                || body.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || resp.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                throw new InvalidOperationException("Usuário ou senha incorretos no GLPI.");
            }

            throw new InvalidOperationException($"Falha ao autenticar no GLPI (HTTP {(int)resp.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("session_token", out var stEl))
            throw new InvalidOperationException("Resposta do GLPI sem session_token.");

        var token = stEl.GetString();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("session_token vazio na resposta do GLPI.");

        return token;
    }

    static async Task<(int UserId, string RawName, string FirstName, string RealName)> FetchFullSessionUserInfoAsync(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiRoot}/getFullSession");
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao obter perfil da sessão GLPI (HTTP {(int)resp.StatusCode}).");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("session", out var sessionEl))
            throw new InvalidOperationException("Dados de sessão ausentes na resposta do GLPI.");

        var userId = 0;
        if (sessionEl.TryGetProperty("glpiID", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var parsedId))
                userId = parsedId;
            else if (idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out var strId))
                userId = strId;
        }

        var rawName = sessionEl.TryGetProperty("glpiname", out var rnEl) ? rnEl.GetString() ?? "" : "";
        var firstName = sessionEl.TryGetProperty("glpifirstname", out var fnEl) ? fnEl.GetString() ?? "" : "";
        var realName = sessionEl.TryGetProperty("glpirealname", out var relEl) ? relEl.GetString() ?? "" : "";

        return (userId, rawName, firstName, realName);
    }

    static async Task<string> FetchUserPictureBase64Async(
        HttpClient http,
        string apiRoot,
        string appToken,
        string sessionToken,
        int userId,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{apiRoot}/User/{userId}/Picture");
        req.Headers.TryAddWithoutValidation("App-Token", appToken);
        req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return "";

        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
            return "";

        return OptimizeAvatarImage(bytes, 128);
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
            req.Headers.TryAddWithoutValidation("App-Token", appToken);
            req.Headers.TryAddWithoutValidation("Session-Token", sessionToken);
            using var _ = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Melhor esforço ao fechar a sessão temporária
        }
    }

    /// <summary>
    /// Redimensiona a foto de perfil para uma resolução compacta (máx 128x128)
    /// garantindo transmissão leve e rápida via WebSockets e armazenamento otimizado.
    /// </summary>
    public static string OptimizeAvatarImage(byte[] rawBytes, int maxSize = 128)
    {
        if (rawBytes == null || rawBytes.Length == 0)
            return "";

        try
        {
            using var msIn = new MemoryStream(rawBytes);
            using var original = new Bitmap(msIn);

            var srcW = original.Width;
            var srcH = original.Height;
            if (srcW <= 0 || srcH <= 0)
                return "";

            var destW = srcW;
            var destH = srcH;
            if (srcW > maxSize || srcH > maxSize)
            {
                if (srcW > srcH)
                {
                    destW = maxSize;
                    destH = (int)Math.Round((double)srcH * maxSize / srcW);
                }
                else
                {
                    destH = maxSize;
                    destW = (int)Math.Round((double)srcW * maxSize / srcH);
                }
            }
            destW = Math.Max(1, destW);
            destH = Math.Max(1, destH);

            using var resized = new Bitmap(destW, destH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(original, 0, 0, destW, destH);
            }

            using var msOut = new MemoryStream();
            resized.Save(msOut, ImageFormat.Png);
            return Convert.ToBase64String(msOut.ToArray());
        }
        catch
        {
            if (rawBytes.Length <= 500 * 1024)
                return Convert.ToBase64String(rawBytes);

            return "";
        }
    }
}

