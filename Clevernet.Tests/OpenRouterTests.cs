using CleverBot.Abstractions;
using CleverBot.Services;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

namespace Clevernet.Tests;

public class OpenRouterTests : ClevernetTestBase
{
    
    [Test]
    public async Task TestOpenRouter()
    {
        using var api = new OpenAIClient(new OpenAIAuthentication(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!), 
            new OpenAIClientSettings("openrouter.ai", "api/v1"));
        var model = OpenRouterCompletionService.Models.DeepseekChat;
        
        var messages = new List<Message>
        {
            new(Role.System, "You are a helpful weather assistant. Always prompt the user for their location."),
            new Message(Role.User, "What's the weather like today?"),
        };

        foreach (var message in messages)
        {
            Console.WriteLine($"{message.Role}: {message}");
        }

        // Define the tools that the assistant is able to use:
        // 1. Get a list of all the static methods decorated with FunctionAttribute
        // var tools = Tool.GetAllAvailableTools(includeDefaults: false, forceUpdate: true, clearCache: true);
        // 2. Define a custom list of tools:
        var tools = new List<Tool>
        {
            // Tool.GetOrCreateTool(objectInstance, "TheNameOfTheMethodToCall"),
            Tool.FromFunc("getWeather", ([FunctionParameter("location to fetch weather for")] string location) => $"{location}: 10 deg C, sunny", "Fetch weather for a location")
        };
        var chatRequest = new ChatRequest(messages, model: model, tools: tools, toolChoice: "auto");
        var response = await api.ChatEndpoint.GetCompletionAsync(chatRequest);
        messages.Add(response.FirstChoice.Message);

        Console.WriteLine($"{response.FirstChoice.Message.Role}: {response.FirstChoice} | Finish Reason: {response.FirstChoice.FinishReason}");

        var locationMessage = new Message(Role.User, "I'm in Glasgow, Scotland");
        messages.Add(locationMessage);
        Console.WriteLine($"{locationMessage.Role}: {locationMessage.Content}");
        chatRequest = new ChatRequest(messages, model: model, tools: tools, toolChoice: "auto");
        response = await api.ChatEndpoint.GetCompletionAsync(chatRequest);

        messages.Add(response.FirstChoice.Message);
        

        // iterate over all tool calls and invoke them
        foreach (var toolCall in response.FirstChoice.Message.ToolCalls)
        {
            Console.WriteLine($"{response.FirstChoice.Message.Role}: {toolCall.Function.Name} | Finish Reason: {response.FirstChoice.FinishReason}");
            Console.WriteLine($"{toolCall.Function.Arguments}");
            // Invokes function to get a generic json result to return for tool call.
            var functionResult = await toolCall.InvokeFunctionAsync();
            messages.Add(new Message(toolCall, functionResult));
            Console.WriteLine($"{Role.Tool}: {functionResult}");
        }
        
        chatRequest = new ChatRequest(messages, model: model, tools: tools, toolChoice: "auto");
        response = await api.ChatEndpoint.GetCompletionAsync(chatRequest);
        messages.Add(response.FirstChoice.Message);
        Console.WriteLine($"{response.FirstChoice.Message.Role}: {response.FirstChoice} | Finish Reason: {response.FirstChoice.FinishReason}");
        
        // System: You are a helpful weather assistant.
        // User: What's the weather like today?
        // Assistant: Sure, may I know your current location? | Finish Reason: stop
        // User: I'm in Glasgow, Scotland
        // Assistant: GetCurrentWeather | Finish Reason: tool_calls
        // {
        //   "location": "Glasgow, Scotland",
        //   "unit": "celsius"
        // }
        // Tool: The current weather in Glasgow, Scotland is 39°C.
    }
    
    [Test]
    public async Task BasicMessageTest()
    {
        var chatClient = Services.GetRequiredService<IChatCompletionService>();
        var messages = new List<Message>()
        {
            new Message(Role.User, "Say hello!")
        };

        var newMessages = await chatClient.GetCompletionAsync(messages, OpenRouterCompletionService.Models.DeepseekChat);
        
        Assert.That(newMessages, Is.Not.Null);
        Assert.That(newMessages.Count, Is.EqualTo(1));
        var textContent = newMessages.First().Content is string text ? text : null;
        Assert.That(textContent, Is.Not.Empty);
        await TestContext.Out.WriteLineAsync($"Response: {textContent}");
    }
    
    [Test]
    public async Task StreamingMessageTest()
    {
        var chatClient = Services.GetRequiredService<IChatCompletionService>();
        var messages = new List<Message>()
        {
            new Message(Role.User, "Count from 1 to 5 slowly.")
        };

        var fullResponse = "";
        try
        {
            await chatClient.StreamCompletionAsync(messages, OpenRouterCompletionService.Models.OpenAIGpt4Mini, (res) =>
            {
                try
                {
                    if (res.FirstChoice.Delta?.Content != null)
                    {
                        fullResponse += res.FirstChoice.Delta.Content;
                        TestContext.Out.WriteLine($"Received chunk: {res.FirstChoice.Delta.Content}");
                    }
                }
                catch (Exception e)
                {
                    TestContext.Out.WriteLine($"Error: {e}");
                }
            });
        }
        catch (Exception e)
        {
            // Bug with handling SSE comment in OpenAI-Dotnet
            Assert.That(e.Message, Contains.Substring("BytePositionInLine: 23"));
        }

        Assert.That(fullResponse, Is.Not.Empty);
        Assert.That(fullResponse, Does.Contain("1"));
        Assert.That(fullResponse, Does.Contain("5"));
    }

    [Test]
    public async Task LmmlGenerationTest()
    {
        var chatClient = Services.GetRequiredService<IChatCompletionService>();
        var systemPrompt = @"You are a Matrix event formatter that converts natural language descriptions into LMML (Language Model Markup Language) format.

IMPORTANT: Output ONLY the LMML markup with no additional text or explanation.

LMML is an XML-like format optimized for language model processing. Key rules:
1. Tags are descriptive and self-documenting (e.g., <message>, <typing>)
2. Attributes use camelCase and contain metadata (e.g., roomId, userId)
3. Content goes between opening and closing tags
4. Timestamps use ISO 8601 format
5. No extra whitespace or formatting - keep it compact

Example input: A user @bob:matrix.org sent 'Hello world!' in room !xyz789:matrix.org at 2024-03-22T14:00:00Z
Example output:
<message roomId=""!xyz789:matrix.org"" from=""@bob:matrix.org"" timestamp=""2024-03-22T14:00:00Z"">Hello world!</message>";

        var prompt = @"Convert to LMML: A user with ID @alice:matrix.org sent a message saying 'Hello everyone!' in room !abc123:matrix.org at 2024-03-22T15:30:00Z";

        var messages = new List<Message>()
        {
            new Message(Role.System, systemPrompt),
            new Message(Role.User, prompt)
        };

        var result = await chatClient.GetCompletionAsync(messages, OpenRouterCompletionService.Models.DeepseekChat);
        
        Assert.That(result, Is.Not.Null);
        var textContent = result.First().Content.ToString();
        Assert.That(textContent, Is.Not.Empty);
        
        var response = textContent.Trim();
        await TestContext.Out.WriteLineAsync($"Generated LMML:\n{response}");

        // Format checks
        Assert.That(response, Does.StartWith("<message"));
        Assert.That(response, Does.EndWith("</message>"));
        
        // Content checks
        Assert.That(response, Does.Contain("roomId=\"!abc123:matrix.org\""));
        Assert.That(response, Does.Contain("from=\"@alice:matrix.org\""));
        Assert.That(response, Does.Contain("timestamp=\"2024-03-22T15:30:00Z\""));
        Assert.That(response, Does.Contain(">Hello everyone!</message>"));
    }
}