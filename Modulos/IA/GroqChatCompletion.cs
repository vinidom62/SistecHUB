namespace SistecHub.Modulos.IA;

/// <summary>Resultado de uma conclusão de chat na Groq.</summary>
public sealed class GroqChatCompletion
{
    public GroqChatCompletion(string content, string? finishReason, string? id, string? model)
    {
        Content = content;
        FinishReason = finishReason;
        Id = id;
        Model = model;
    }

    public string Content { get; }

    public string? FinishReason { get; }

    public string? Id { get; }

    public string? Model { get; }
}
