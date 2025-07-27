using CleverBot.Abstractions;
using CleverBot.Helpers;
using OpenAI;
using OpenAI.Chat;
using Microsoft.Extensions.Configuration;

namespace CleverBot.Services;

/// <summary>
/// Implementation of IChatCompletionService using OpenRouter
/// </summary>
public class OpenRouterCompletionService : IChatCompletionService
{
    public static class Models
    {

        /// <summary>
        /// Google Gemini 2.0 Flash
        /// 1M context | $0.1/M input tokens | $0.4/M output tokens
        /// </summary>
        public const string GoogleGemini20Flash = "google/gemini-2.0-flash-001";
        
        /// <summary>
        /// DeepSeek V2.5
        /// 66K context | $0.14/M input tokens | $0.28/M output tokens
        /// https://openrouter.ai/deepseek/deepseek-chat
        /// </summary>
        public const string DeepseekChat = "deepseek/deepseek-chat";

        /// <summary>
        /// Cohere Command R 08-2024
        /// 128K context | $0.1425/M input tokens |$0.57/M output tokens
        /// https://openrouter.ai/cohere/command-r-08-2024
        /// </summary>
        public const string CohereCommandR = "cohere/command-r-08-2024";

        /// <summary>
        /// 128K context | $0.15/M input tokens | $0.6/M output tokens | $7.225/K input imgs
        /// https://openrouter.ai/openai/gpt-4o-mini
        /// </summary>
        public const string OpenAIGpt4Mini = "openai/gpt-4o-mini";

        /// <summary>
        /// Claude 3 Haiku
        /// 200K context | $0.25/M input tokens | $1.25/M output tokens | $0.4/K input imgs
        /// https://openrouter.ai/anthropic/claude-3-haiku
        /// </summary>
        public const string Claude3Haiku = "anthropic/claude-3-haiku";

        /// <summary>
        /// Amazon Nova Pro 1.0
        /// 300K context | $0.8/M input tokens | $3.2/M output tokens | $1.2/K input imgs
        /// https://openrouter.ai/amazon/nova-pro-v1
        /// </summary>
        public const string AmazonNovaPro1 = "amazon/nova-pro-v1";

        /// <summary>
        /// Claude 3.5 Haiku
        /// 200K context | $0.8/M input tokens | $4/M output tokens
        /// https://openrouter.ai/anthropic/claude-3.5-haiku-20241022
        /// </summary>
        public const string Claude35Haiku = "anthropic/claude-3.5-haiku";

        /// <summary>
        /// Gemini Pro 1.5
        /// 2M context | $1.25/M input tokens | $5/M output tokens | $0.6575/K input imgs
        /// https://openrouter.ai/google/gemini-pro-1.5
        /// </summary>
        public const string GeminiPro15 = "google/gemini-pro-1.5";

        /// <summary>
        /// x-AI Grok 2
        /// 131K context | $2/M input tokens | $10/M output tokens
        /// https://openrouter.ai/x-ai/grok-2-1212
        /// </summary>
        public const string Grok2 = "x-ai/grok-2-1212";

        /// <summary>
        /// Cohere Command R Plus
        /// 128K context | $2.375/M input tokens | $9.5/M output tokens
        /// https://openrouter.ai/cohere/command-r-plus-08-2024
        /// </summary>
        public const string CohereCommandRPlus = "cohere/command-r-plus-08-2024";

        /// <summary>
        /// GPT-4o
        /// 128K context | $2.5/M input tokens | $10/M output tokens | $3.613/K input imgs
        /// https://openrouter.ai/openai/gpt-4o
        /// </summary>
        public const string OpenAIGpt4 = "openai/gpt-4o";

        /// <summary>
        /// Claude 3.5 Sonnet
        /// 200K context | $3/M input tokens | $15/M output tokens | $4.8/K input imgs
        /// https://openrouter.ai/anthropic/claude-3.5-sonnet
        /// </summary>
        public const string Claude35Sonnet = "anthropic/claude-3.5-sonnet";

        /// <summary>
        /// GPT-4o:extended
        /// 128K context | $6/M input tokens | $18/M output tokens | $7.225/K input imgs
        /// https://openrouter.ai/openai/gpt-4o:extended
        /// </summary>
        public const string OpenAIGpt4Extended = "openai/gpt-4o:extended";

        /// <summary>
        /// Claude 3 Opus
        /// 200K context | $15/M input tokens | $75/M output tokens | $24/K input imgs
        /// https://openrouter.ai/anthropic/claude-3-opus
        /// </summary>
        public const string Claude3Opus = "anthropic/claude-3-opus";

        /// <summary>
        /// Gets an array of all available model names by using reflection to collect const string values
        /// </summary>
        public static string[] AllModels => typeof(Models)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
            .Select(fi => (string)fi.GetValue(null)!)
            .ToArray();
    }
    
    private readonly ILogger<OpenRouterCompletionService> _logger;
    
    private string _appName = "Clevernet";
    private string _appUrl = "https://hizlabs.com/";
    private readonly string _apiKey;
    public void SetApp(string app, string url)
    {
        _appName = app;
        _appUrl = url;
    }

    private OpenAIClient GetClient()
    {
        return new OpenAIClient(
            new OpenAIAuthentication(_apiKey),
            new OpenAIClientSettings("openrouter.ai", "api/v1"), new HttpClient()
            {
                DefaultRequestHeaders =
                {
                    { "HTTP-Referer", _appUrl },
                    { "X-Title", _appName }
                },
                Timeout = TimeSpan.FromSeconds(300)
            });
    }

    public OpenRouterCompletionService(IConfiguration configuration, ILogger<OpenRouterCompletionService> logger)
    {
        _logger = logger;
        _apiKey = configuration["OpenRouter:ApiKey"] ?? throw new InvalidOperationException("OpenRouter:ApiKey not configured");
    }

    public async Task<IReadOnlyCollection<Message>> GetCompletionAsync(
        IEnumerable<Message> messages,
        string model,
        IEnumerable<Tool>? tools = null,
        string? toolChoice = "auto",
        decimal temperature = 0.7m,
        int? maxTokens = null,
        bool preventParallelFunctionCalls = false,
        bool preventFunctionCallsWithoutThinking = false,
        Func<(IList<Message> OldMessages, IList<Message> NewMessages), Task>? onMessagesReceived = null,
        CancellationToken cancellationToken = default)
    {
        var toolsTmp = tools?.ToList();
        var oldMessages = messages.ToList();
        var newMessages = new List<Message>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = new ChatRequest(
                oldMessages.Concat(newMessages),
                model: model,
                tools: toolsTmp,
                toolChoice: toolChoice,
                temperature: (double?)temperature,
                maxTokens: maxTokens);
            
            _logger.LogInformation("Requesting completion with {Model}", model);
            
            ChatResponse? response = null;
            for(var i = 0; i < 1 && response?.FirstChoice?.Message == null; i++)
                response = await GetClient().ChatEndpoint.GetCompletionAsync(request, cancellationToken);
            if (response?.FirstChoice?.Message == null)
                throw new CompletionFailedException("OpenRouter API returned null response");
            
            newMessages.Add(response.FirstChoice.Message);
            
            if (response.FirstChoice.Message.ToolCalls?.Any() == true)
            {
                var first = true;
                foreach (var toolCall in response.FirstChoice.Message.ToolCalls)
                {
                    _logger.LogDebug("Handling tool call {ToolCall}", toolCall?.Function?.Name);
                    try
                    {
                        if (preventParallelFunctionCalls && !first)
                        {
                            var fr = new Message(toolCall, @"{""result"":""ERROR: Parallel function calls are not allowed. Please wait for the first function call to finish before calling another function.""}");
                            newMessages.Add(fr);
                            _logger.LogError("{MessageRole}: {FunctionName} | Finish Reason: {FirstChoiceFinishReason}",
                                response.FirstChoice?.Message.Role, toolCall.Function.Name, response.FirstChoice?.FinishReason);
                            continue;
                        }
                        first = false;
                        _logger.LogDebug("{MessageRole}: {FunctionName} | Finish Reason: {FirstChoiceFinishReason}",
                            response.FirstChoice?.Message.Role, toolCall.Function.Name, response.FirstChoice?.FinishReason);
                        _logger.LogDebug("{FunctionArguments}", toolCall.Function.Arguments);
                        if (preventFunctionCallsWithoutThinking && response.FirstChoice.Message.Content is null)
                        {
                            var fr = new Message(toolCall, @"{""result"":""ERROR: You must think step-by-step before each function call. Ensure the function call is necessary and has not already been performed.""}");
                            newMessages.Add(fr);
                            _logger.LogError("{MessageRole}: {FunctionName} | Finish Reason: {FirstChoiceFinishReason}",
                                response.FirstChoice?.Message.Role, toolCall.Function.Name, response.FirstChoice?.FinishReason);
                            continue;
                        }
                        // Invokes function to get a generic json result to return for tool call.
                        var functionResult = await toolCall.InvokeFunctionAsync(cancellationToken);
                        newMessages.Add(new Message(toolCall, functionResult));
                        _logger.LogDebug("{Tool}: {FunctionResult}", Role.Tool, functionResult);
                    }
                    catch (Exception e)
                    {
                        var functionResult = new Message(toolCall, $"EXCEPTION: {e.Unwrap().Message}");
                        newMessages.Add(functionResult);
                        _logger.LogError(e, "Error invoking function {FunctionName}", toolCall.Function.Name);
                    }
                    if(onMessagesReceived != null)
                        await onMessagesReceived((oldMessages, newMessages));
                }
            }
            else
            {
                break;
            }
        }
        _logger.LogInformation("Received completion with {Model}", model);
        return newMessages;
    }

    public async Task StreamCompletionAsync(
        IEnumerable<Message> messages,
        string model,
        Action<ChatResponse> onResponse,
        IEnumerable<Tool>? tools = null,
        string? toolChoice = "auto",
        decimal temperature = 0.7m,
        int? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(
            messages,
            model: model,
            tools: tools,
            toolChoice: toolChoice,
            temperature: (double?)temperature,
            maxTokens: maxTokens);

        await GetClient().ChatEndpoint.StreamCompletionAsync(request, onResponse, false, cancellationToken);
    }
}

public class CompletionFailedException : Exception
{
    public CompletionFailedException(string message) : base(message)
    {
    }
}