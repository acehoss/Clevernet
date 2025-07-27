using CleverBot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Clevernet.Tests;

[TestFixture]
public class OllamaTests : ClevernetTestBase
{
    
    [Test]
    public async Task TestOllama()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "Hi") };
        IChatClient client = 
            new OllamaChatClient(new Uri("http://10.0.4.137:11434/"), "mistral-small3.2");
        var response =  await client.CompleteAsync(messages);
    }
}