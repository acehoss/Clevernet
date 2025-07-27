using CleverBot;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Microsoft.EntityFrameworkCore;
using Clevernet.Data;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using CleverBot.Abstractions;
using CleverBot.Helpers;
using CleverBot.Services;

namespace Clevernet.Tests;

[TestFixture]
public class FileSystemTests : ClevernetTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
    }

    private IFileSystem _fs;
    private IDbContextFactory<CleverDbContext> _contextFactory;
    [OneTimeSetUp]
    public void Setup()
    {
        _contextFactory = Services.GetRequiredService<IDbContextFactory<CleverDbContext>>();
        _fs = Services.GetRequiredService<IFileSystem>();
    }

    [SetUp]
    public async Task TestSetup()
    {
        // Clean up any test files before each test
        using var context = await _contextFactory.CreateDbContextAsync();
        var testFiles = await context.Files.Where(f => f.Share == "test").ToListAsync();
        context.Files.RemoveRange(testFiles);
        await context.SaveChangesAsync();
    }

    [Test]
    public async Task ReadFile_NonexistentFile_ReturnsNull()
    {
        var result = await _fs.ReadFileAsync("test:/nonexistent.txt");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task WriteAndReadFile_TextFile_MatchesOriginalContent()
    {
        // Write a file
        var path = "test:/test.txt";
        var content = "Hello, World!";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "text/plain", FileWriteMode.CreateText);

        // Read it back
        var result = await _fs.ReadFileAsync(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContentType, Is.EqualTo("text/plain"));
        Assert.That(result.OpenFileContent, Is.EqualTo(content));

        // Verify it's stored as text
        using var context = await _contextFactory.CreateDbContextAsync();
        var file = await context.Files.FirstOrDefaultAsync(f => f.Share == "test" && f.Path == "test.txt");
        Assert.That(file, Is.Not.Null);
        Assert.That(file.TextContent, Is.EqualTo(content));
        Assert.That(file.BinaryContent, Is.Null);
    }
    
    [Test]
    public async Task WriteAndReadFileAndAppend_TextFile()
    {
        // Write a file
        var path = "test:/test.txt";
        var content = "Hello, World!";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "text/plain", FileWriteMode.CreateText);

        // Read it back
        var result = await _fs.ReadFileAsync(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContentType, Is.EqualTo("text/plain"));
        Assert.That(result.OpenFileContent, Is.EqualTo(content));

        // Verify it's stored as text
        using var context = await _contextFactory.CreateDbContextAsync();
        var file = await context.Files.FirstOrDefaultAsync(f => f.Share == "test" && f.Path == "test.txt");
        Assert.That(file, Is.Not.Null);
        Assert.That(file.TextContent, Is.EqualTo(content));
        Assert.That(file.BinaryContent, Is.Null);
        
        await _fs.WriteFileAsync(path, content + "!", DefaultOwner, "text/plain", FileWriteMode.AppendLineText);
        result = await _fs.ReadFileAsync(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContentType, Is.EqualTo("text/plain"));
        Assert.That(result.OpenFileContent, Is.EqualTo($"{content}\n{content}!"));
    }

    [Test]
    public async Task WriteFile_WithContentType_SetsCorrectType()
    {
        var path = "test:/test.json";
        var content = "{ \"test\": true }";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "application/json", FileWriteMode.CreateText);

        var result = await _fs.ReadFileAsync(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContentType, Is.EqualTo("application/json"));
        Assert.That(result.OpenFileContent, Is.EqualTo(content));

        // Verify it's stored as text
        using var context = await _contextFactory.CreateDbContextAsync();
        var file = await context.Files.FirstOrDefaultAsync(f => f.Share == "test" && f.Path == "test.json");
        Assert.That(file, Is.Not.Null);
        Assert.That(file.TextContent, Is.EqualTo(content));
        Assert.That(file.BinaryContent, Is.Null);
    }

    [Test]
    public async Task WriteFile_BinaryContent_StoresAsBinary()
    {
        var path = "test:/test.bin";
        var content = "Binary Data";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "application/octet-stream", FileWriteMode.CreateText);

        var result = await _fs.ReadFileAsync(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ContentType, Is.EqualTo("application/octet-stream"));
        Assert.That(result.BinaryContent, Is.EqualTo(content));

        // Verify it's stored as binary
        using var context = await _contextFactory.CreateDbContextAsync();
        var file = await context.Files.FirstOrDefaultAsync(f => f.Share == "test" && f.Path == "test.bin");
        Assert.That(file, Is.Not.Null);
        Assert.That(file.TextContent, Is.Null);
        Assert.That(file.BinaryContent, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(file.BinaryContent), Is.EqualTo(content));
    }

    [Test]
    public void GlobWorks()
    {
        // Arrange test files
        var files = new[]
        {
            ("test:/docs/readme.md", "# Welcome to the project\nThis is a test file.\nIt contains welcome text.", "text/markdown"),
            ("test:/docs/guide.md", "This is a user guide\nIt has some content\nAnd more lines", "text/markdown"),
            ("test:/config/settings.json", "{ \"searchTerm\": \"value\" }", "application/json"),
            ("test:/notes.txt", "Random notes\nWith welcome message\nAnd some other text", "text/plain")
        };
        
        // Act & Assert
        var results1 = files.Where(f => f.Item1.Like("*/docs/*")).ToList();
        Assert.That(results1, Does.Contain(files[0]));
        Assert.That(results1, Does.Contain(files[1]));
    }

    [Test]
    public async Task Search_FindsMatchingFiles()
    {
        // Arrange test files
        var files = new[]
        {
            ("test:/docs/readme.md", "# Welcome to the project\nThis is a test file.\nIt contains welcome text.", "text/markdown"),
            ("test:/docs/guide.md", "This is a user guide\nIt has some content\nAnd more lines", "text/markdown"),
            ("test:/config/settings.json", "{ \"searchTerm\": \"value\" }", "application/json"),
            ("test:/notes.txt", "Random notes\nWith welcome message\nAnd some other text", "text/plain")
        };

        foreach (var (path, content, type) in files)
        {
            await _fs.WriteFileAsync(path, content, DefaultOwner, type, FileWriteMode.CreateText);
        }

        // Act & Assert

        // Search in content
        var searchResults1Raw = await _fs.SearchAsync("test:/", "welcome", FileSearchMode.FileContents);
        var searchResults1 = string.Join("\n", searchResults1Raw.Select(r => r.ToLmml().ToString()));
        Assert.That(searchResults1, Does.Contain("<searchMatch path=\"test:/docs/readme.md\""));
        Assert.That(searchResults1, Does.Contain("<searchMatch path=\"test:/notes.txt\""));
        Assert.That(searchResults1, Does.Contain("lineNumber=\"1\""));
        Assert.That(searchResults1, Does.Contain("lineNumber=\"2\""));
        Assert.That(searchResults1, Does.Contain("# Welcome to the project\nThis is a test file."));
        Assert.That(searchResults1, Does.Contain("Random notes\nWith welcome message\nAnd some other text"));

        // Search in path
        var searchResults2Raw = await _fs.SearchAsync("test:/", "*conf*.jso*", FileSearchMode.FileContents);
        var searchResults2 = string.Join("\n", searchResults2Raw.Select(r => r.ToLmml().ToString()));
        Assert.That(searchResults2, Does.Contain("<searchMatch path=\"test:/config/settings.json\""));

        // Search with no matches
        var searchResults3Raw = await _fs.SearchAsync("test:/", "nonexistent", FileSearchMode.FileContents);
        var searchResults3 = string.Join("\n", searchResults3Raw.Select(r => r.ToLmml().ToString()));
        Assert.That(searchResults3, Does.Not.Contain("<searchMatch"));
    }

    [Test]
    public async Task GetDirectoryTree_ReturnsCorrectStructure()
    {
        // Create test files and directories
        var files = new[]
        {
            ("test:/docs/readme.md", "# Welcome", "text/markdown"),
            ("test:/docs/api/v1.md", "API docs", "text/markdown"),
            ("test:/config/settings.json", "{}", "application/json"),
            ("test:/images/logo.png", "binary data", "image/png"),
            ("test:/data.bin", "binary stuff", "application/octet-stream"),
            ("test:/notes.txt", "Notes", "text/plain")
        };

        foreach (var (path, content, type) in files)
        {
            await _fs.WriteFileAsync(path, content, DefaultOwner, type, FileWriteMode.CreateText);
        }

        // Get tree
        var tree = await _fs.GetDirectoryTree("test:/");
        
        // Verify structure and icons
        Assert.That(tree, Does.Contain("📁 docs"));
        Assert.That(tree, Does.Contain("📄 readme.md"));  // text/markdown
        Assert.That(tree, Does.Contain("📁 api"));
        Assert.That(tree, Does.Contain("📄 v1.md"));      // text/markdown
        Assert.That(tree, Does.Contain("📁 config"));
        Assert.That(tree, Does.Contain("📋 settings.json")); // application/json
        Assert.That(tree, Does.Contain("📁 images"));
        Assert.That(tree, Does.Contain("🖼️ logo.png"));    // image/png
        Assert.That(tree, Does.Contain("📦 data.bin"));    // application/octet-stream
        Assert.That(tree, Does.Contain("📄 notes.txt"));   // text/plain
    }

    [Test]
    public async Task GetFileStats_ReturnsCorrectInfo()
    {
        // Create a test file
        var path = "test:/test.txt";
        var content = "Hello, World!";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "text/plain", FileWriteMode.CreateText);

        // Get stats
        var stats = await _fs.GetFileStat(path);
        
        // Verify info
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.Path, Is.EqualTo(path));
        Assert.That(stats.Share, Is.EqualTo("test"));
        Assert.That(stats.ContentType, Is.EqualTo("text/plain"));
        Assert.That(stats.Size, Is.EqualTo(content.Length));
        Assert.That(stats.CreatedAt.UtcDateTime, Is.Not.EqualTo(default(DateTime)));
        Assert.That(stats.UpdatedAt.UtcDateTime, Is.Not.EqualTo(default(DateTime)));
        Assert.That(stats.Owner, Is.EqualTo(DefaultOwner));
    }

    [Test]
    public async Task DeleteFile_RemovesFile()
    {
        // Create a test file
        var path = "test:/test.txt";
        var content = "Hello, World!";
        await _fs.WriteFileAsync(path, content, DefaultOwner, "text/plain", FileWriteMode.CreateText);

        // Delete it
        await _fs.DeleteFileAsync(path);

        // Verify it's gone
        var file = await _fs.ReadFileAsync(path);
        Assert.That(file, Is.Null);
    }
} 