namespace SistecHub.Modulos.IA;

/// <summary>Resultado de uma conclusão de chat na Groq.</summary>
public sealed class GroqChatCompletion
{
    public GroqChatCompletion(string content) => Content = content;

    public string Content { get; }
}
