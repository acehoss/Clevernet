using System.Collections.Immutable;
using CleverBot;
using CleverBot.Abstractions;
using CleverBot.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using Clevernet.Data;
using OpenAI;
using Message = OpenAI.Chat.Message;

namespace Clevernet.Tests;

[TestFixture]
public class ChatCompletionIntegrationTests : ClevernetTestBase
{
    [Test]
    public async Task SearchAndSummarize_FindsAndProcessesFiles()
    {
        var fs = Services.GetRequiredService<IFileSystem>();
        // Create test files
        var shareName = $"test";
        var files = new[]
        {
            ($"{shareName}:/docs/architecture.md", 
             "# System Architecture\n\nThe system uses a microservices architecture with the following components:\n- API Gateway\n- Authentication Service\n- User Service\n- Notification Service", 
             "text/markdown"),
            
            ($"{shareName}:/docs/api.md",
             "# API Documentation\n\nThis document describes the REST API endpoints:\n\n## Users\n- GET /users\n- POST /users\n- GET /users/{id}", 
             "text/markdown"),
             
            ($"{shareName}:/notes/meeting.md",
             "# Team Meeting Notes\n\nDiscussed the following topics:\n1. New feature requests\n2. Performance improvements\n3. Upcoming deadlines", 
             "text/markdown")
        };

        foreach (var (path, content, type) in files)
        {
            await fs.WriteFileAsync(path, content, DefaultOwner, type, FileWriteMode.CreateText);
        }

        // Search for documentation files
        var searchResults = await fs.SearchAsync(shareName + ":/", "API", FileSearchMode.FileContents);
        Assert.That(searchResults, Is.Not.Empty);
        
        var chatClient = Services.GetRequiredService<IChatCompletionService>();

        var messages = new List<Message>
        {
            new Message(Role.User, $"Search for files matching 'API' under the path '{shareName}:/' and summarize the result content.")
        };

        var tools = new []
        {
            Tool.FromFunc("FS_Search", 
                 ([FunctionParameter("Path including share name to search, i.e share:/")] string path, 
                         [FunctionParameter("search query")] string query,
                         [FunctionParameter($"search mode\n- filename: glob search against filenames, allowed wildcards are * and ?. Beware that leading and/or trailing glob may be needed. \n- content: plain search on file content")] string searchMode = "content")
                     => fs.SearchAsync(path, query, searchMode == "filename" ? FileSearchMode.FileNames : FileSearchMode.FileContents).Result, 
                 "Search for files containing a search query in a file share. Returns results in LMML format with line context.")
        };
        var result = await chatClient.GetCompletionAsync(messages, OpenRouterCompletionService.Models.OpenAIGpt4Mini, tools: tools);
        
        Assert.That(result, Is.Not.Null);
        var notToolMessages = result.Where(r => r.ToolCalls == null || !r.ToolCalls.Any()).ToImmutableArray();
        notToolMessages.Select(r => r.Content).ToList().ForEach(t => TestContext.WriteLine(t));
        var toolMessages = result.Where(r => r.ToolCalls != null && r.ToolCalls.Any()).ToImmutableArray();
        Assert.That(notToolMessages.Any(), Is.True);
        Assert.That(toolMessages.Any(), Is.True);
    }
} 