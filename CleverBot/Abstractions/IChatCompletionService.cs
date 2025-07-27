using OpenAI;
using OpenAI.Chat;

namespace CleverBot.Abstractions;

/// <summary>
/// Abstraction for chat completion services (OpenRouter, Anthropic, etc.)
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// Gets a chat completion for the given messages and parameters, handling function calls
    /// </summary>
    Task<IReadOnlyCollection<Message>> GetCompletionAsync(
        IEnumerable<Message> messages,
        string model,
        IEnumerable<Tool>? tools = null,
        string? toolChoice = "auto",
        decimal temperature = 0.7m,
        int? maxTokens = null,
        bool preventParallelFunctionCalls = false,
        bool preventFunctionCallsWithoutThinking = false,
        Func<(IList<Message> OldMessages, IList<Message> NewMessages), Task>? onMessagesReceived = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a streaming chat completion for the given messages and parameters
    /// </summary>
    Task StreamCompletionAsync(
        IEnumerable<Message> messages,
        string model,
        Action<ChatResponse> onResponse,
        IEnumerable<Tool>? tools = null,
        string? toolChoice = "auto",
        decimal temperature = 0.7m,
        int? maxTokens = null,
        CancellationToken cancellationToken = default);

    void SetApp(string app, string url);
} 