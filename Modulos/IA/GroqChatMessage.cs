namespace SistecHub.Modulos.IA;

/// <summary>Mensagem no formato chat da API Groq (compatível com OpenAI).</summary>
public sealed class GroqChatMessage
{
    public GroqChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    /// <summary>Valores típicos: <c>system</c>, <c>user</c>, <c>assistant</c>.</summary>
    public string Role { get; }

    public string Content { get; }
}
