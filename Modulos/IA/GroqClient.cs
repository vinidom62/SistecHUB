using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SistecHub.Core;

namespace SistecHub.Modulos.IA;

/// <summary>Cliente HTTP para a API de chat da Groq (endpoint compatível com OpenAI).</summary>
public static class GroqClient
{
    const string DefaultEndpoint = "https://api.groq.com/openai/v1/chat/completions";
    const string Model = "openai/gpt-oss-120b";
    const double DefaultTemperature = 0.5;
    internal const double TitleGenerationTemperature = 0.2;
    internal const double CategoryResolutionTemperature = 0.1;

    static readonly JsonSerializerOptions JsonWrite = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Obtém a chave: variável de ambiente <c>GROQ_API_KEY</c> tem prioridade sobre
    /// <see cref="AppUserSettings.GroqApiKey"/> (sincronizada a partir do plugin GLPI).
    /// </summary>
    public static string ResolveApiKey(AppUserSettings settings)
    {
        var env = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        if (!string.IsNullOrWhiteSpace(settings.GroqApiKey))
            return settings.GroqApiKey.Trim();

        throw new InvalidOperationException(
            "Não foi possível obter a chave Groq. Configure o plugin Groq SistecHub no GLPI ou defina GROQ_API_KEY.");
    }

    /// <summary>Conclusão de chat com as mensagens indicadas.</summary>
    public static Task<GroqChatCompletion> CompleteChatAsync(
        AppUserSettings settings,
        IReadOnlyList<GroqChatMessage> messages,
        CancellationToken cancellationToken = default) =>
        CompleteChatAsync(settings, messages, DefaultTemperature, cancellationToken);

    public static async Task<GroqChatCompletion> CompleteChatAsync(
        AppUserSettings settings,
        IReadOnlyList<GroqChatMessage> messages,
        double temperature,
        CancellationToken cancellationToken = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("É necessário pelo menos uma mensagem.", nameof(messages));

        var apiKey = ResolveApiKey(settings);

        var endpoint = DefaultEndpoint;

        var payload = new ChatCompletionRequest
        {
            Model = Model,
            Temperature = temperature,
            Messages = messages
                .Select(m => new ChatMessageDto { Role = m.Role, Content = m.Content })
                .ToList(),
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonWrite),
            Encoding.UTF8,
            "application/json");

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var err = TryReadErrorMessage(body);
            throw new InvalidOperationException(
                err is not null
                    ? $"Groq HTTP {(int)resp.StatusCode}: {err}"
                    : $"Groq HTTP {(int)resp.StatusCode}: resposta não OK.");
        }

        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonRead);
        var choice = parsed?.Choices?.FirstOrDefault();
        var content = choice?.Message?.Content ?? "";
        return new GroqChatCompletion(content);
    }

    /// <summary>
    /// Contacta a API da Groq (listagem de modelos) para verificar a chave, sem completar chat.
    /// </summary>
    public static async Task ValidateApiKeyConnectionAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey(settings);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.groq.com/openai/v1/models?limit=1");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "A chave da API Groq foi recusada (não autorizada). Verifique o token.");
        }

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var hint = TryReadErrorMessage(body);
            throw new InvalidOperationException(
                hint is not null
                    ? $"Groq: HTTP {(int)resp.StatusCode} — {hint}"
                    : $"Não foi possível validar a chave da Groq (HTTP {(int)resp.StatusCode}).");
        }
    }

    /// <summary>Atalho: uma mensagem de sistema opcional e texto do utilizador.</summary>
    public static Task<GroqChatCompletion> CompleteUserPromptAsync(
        AppUserSettings settings,
        string userContent,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var list = new List<GroqChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            list.Add(new GroqChatMessage("system", systemPrompt.Trim()));
        list.Add(new GroqChatMessage("user", userContent.Trim()));
        return CompleteChatAsync(settings, list, cancellationToken);
    }

    static string? TryReadErrorMessage(string json)
    {
        try
        {
            var err = JsonSerializer.Deserialize<GroqErrorEnvelope>(json, JsonRead);
            return err?.Error?.Message;
        }
        catch
        {
            return null;
        }
    }

    sealed class ChatCompletionRequest
    {
        public string Model { get; set; } = "";

        public double Temperature { get; set; }

        public List<ChatMessageDto> Messages { get; set; } = [];
    }

    sealed class ChatMessageDto
    {
        public string Role { get; set; } = "";

        public string Content { get; set; } = "";
    }

    sealed class ChatCompletionResponse
    {
        public List<ChoiceDto>? Choices { get; set; }
    }

    sealed class ChoiceDto
    {
        public MessageDto? Message { get; set; }
    }

    sealed class MessageDto
    {
        public string? Content { get; set; }
    }

    sealed class GroqErrorEnvelope
    {
        public GroqErrorBody? Error { get; set; }
    }

    sealed class GroqErrorBody
    {
        public string? Message { get; set; }
    }
}
