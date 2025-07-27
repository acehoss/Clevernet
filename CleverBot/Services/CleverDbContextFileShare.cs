using System.Text;
using CleverBot.Abstractions;
using CleverBot.Helpers;
using Clevernet.Data;
using Microsoft.EntityFrameworkCore;
using File = Clevernet.Data.File;

namespace CleverBot.Services;

public class CleverDbContextFileShare(
    string name,
    string defaultOwner,
    IDbContextFactory<CleverDbContext> contextFactory,
    ILogger<CleverDbContextFileShare> logger)
    : IFileShare
{
    private readonly ILogger<CleverDbContextFileShare> _logger = logger;
    public readonly string DefaultOwner = defaultOwner;
    public string Name { get; } = name;

    public async Task<ClevernetFile?> GetFile(CleverDbContext context, string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = FileSystemUtils.NormalizePath(path);
        var file = await context.Files.FirstOrDefaultAsync(f => 
            f.Share == Name && f.Path == normalizedPath, cancellationToken);
        return file == null ? null : new ClevernetFile(file);
    }
    
    public async Task<ClevernetFile?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await GetFile(context, path, cancellationToken);
    }

    public async Task WriteFileAsync(string path, string content, string owner, string contentType, 
        FileWriteMode mode, CancellationToken cancellationToken = default)
    {
        var normalizedPath = FileSystemUtils.NormalizePath(path);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        
        var file = await context.Files.FirstOrDefaultAsync(f => 
            f.Share == Name && f.Path == normalizedPath, cancellationToken: cancellationToken);

        var isNew = file == null;
        if (file == null)
        {
            file = new File
            {
                Share = Name,
                Path = normalizedPath,
                ContentType = contentType,
                Owner = DefaultOwner
            };
            context.Files.Add(file);
        }

        file.ContentType = contentType;
        
        switch (mode)
        {
            case FileWriteMode.CreateText:
            {
                if(!isNew) throw new Exception($"File already exists: {path}");
                file.TextContent = content;
                file.BinaryContent = null;
                break;
            }
            case FileWriteMode.CreateBinary:
            {
                if(!isNew) throw new Exception($"File already exists: {path}");
                file.TextContent = null;
                file.BinaryContent = System.Text.Encoding.UTF8.GetBytes(content);
                break;
            }
            case FileWriteMode.OverwriteText:
            {
                file.TextContent = content;
                file.BinaryContent = null;
                break;
            }
            case FileWriteMode.OverwriteBinary:
            {
                file.TextContent = null;
                file.BinaryContent = System.Text.Encoding.UTF8.GetBytes(content);
                break;
            }
            case FileWriteMode.AppendText:
            {
                file.TextContent += content;
                file.BinaryContent = null;
                break;
            }
            case FileWriteMode.AppendLineText:
            {
                file.TextContent += "\n" + content;
                file.BinaryContent = null;
                break;
            }
            case FileWriteMode.AppendLineTextWithTimestamp:
            {
                file.TextContent += $"\n[{DateTimeOffset.Now.ToString(Constants.DateTimeFormat)}] {content}";
                file.BinaryContent = null;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        file.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
         await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
         
         var file = await context.Files.FirstOrDefaultAsync(f => 
             f.Share == Name && f.Path == path, cancellationToken: cancellationToken);
         
         if (file == null)
             throw new Exception($"File not found: {path}");

         context.Files.Remove(file);
         await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Abstractions.SearchMatch>> SearchAsync(string path, string searchTerm, FileSearchMode searchMode, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Clevernet.Data.File> files = null;

        // If using PostgreSQL, use full-text search
        if (context.Database.IsNpgsql())
        {
            var wildcardQuery = searchTerm.Replace("*", "%").Replace("?", "_");
            var query = context.Files.Where(f => f.Share == Name && f.Path.StartsWith(path));
            if (searchMode == FileSearchMode.FileNames)
                query = query.Where(f => EF.Functions.Like(f.Path, $"{wildcardQuery}"));
            else // if (searchMode == FileSearchMode.FileContents)
                query = query.Where(f => f.TextContent != null && EF.Functions.ToTsVector("english", f.TextContent)
                    .Matches(EF.Functions.PlainToTsQuery("english", searchTerm)));
            
            files = await query.ToListAsync(cancellationToken);
        }
        else
        {
            // Simple string contains search
            files = (await context.Files
                    .Where(f => f.Share == Name && f.Path.StartsWith(path))
                    .ToArrayAsync(cancellationToken))
                .Where(f => (f.Share + ":/" + f.Path).Like(searchTerm)
                            || (f.TextContent != null &&
                                f.TextContent.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        
        var matches = new List<SearchMatch>();

        foreach (var file in files)
        {
            if (searchMode == FileSearchMode.FileNames && file.SharePath.Like(searchTerm))
            {
                matches.Add(new SearchMatch
                {
                    Path = file.SharePath,
                    ContentType = file.ContentType,
                    Size = file.TextContent?.Length ?? file.BinaryContent?.Length ?? 0,
                    IsBinary = file.BinaryContent != null,
                    CreatedAt = file.CreatedAt,
                    UpdatedAt = file.UpdatedAt,
                    Owner = file.Owner,
                    LineNumber = 0,
                });
            } 
            else 
            {
                if (file.TextContent == null) continue;

                var found = false;
                var lines = file.TextContent.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        matches.Add(new SearchMatch
                        {
                            Path = file.SharePath,
                            ContentType = file.ContentType,
                            Size = file.TextContent.Length,
                            CreatedAt = file.CreatedAt,
                            UpdatedAt = file.UpdatedAt,
                            Owner = file.Owner,
                            LineNumber = i + 1,
                            BeforeLine = i > 0 ? lines[i - 1] : null,
                            MatchLine = lines[i],
                            AfterLine = i < lines.Length - 1 ? lines[i + 1] : null
                        });
                    }
                }

                if (!found)
                {
                    matches.Add(new SearchMatch
                    {
                        Path = file.SharePath,
                        ContentType = file.ContentType,
                        Size = file.TextContent.Length,
                        CreatedAt = file.CreatedAt,
                        UpdatedAt = file.UpdatedAt,
                        Owner = file.Owner,
                        LineNumber = -1,
                    });
                }
            }
        }
        return matches;
    }

    public async Task<string> GetDirectoryTree(string path, CancellationToken cancellationToken = default)
    {
             using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        
        var query = context.Files
            .Where(f => f.Share == Name);
        
        path = FileSystemUtils.NormalizePath(path);
        query = query.Where(f => f.Path.StartsWith(path));
        
        var files = await query
            .OrderBy(f => f.Path)
            .Select(f => new { f.Path, f.ContentType })
            .ToListAsync(cancellationToken);
            
        if (!files.Any())
            return $"No files found in {Name}:/{path}";
            
        var sb = new StringBuilder();
        sb.AppendLine($"Directory tree for {Name}:/{path}");
        
        var currentPath = new List<string>();
        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            
            // Find common prefix with previous path
            int commonParts = 0;
            while (commonParts < currentPath.Count && 
                   commonParts < parts.Length - 1 && 
                   currentPath[commonParts] == parts[commonParts])
            {
                commonParts++;
            }
            
            // Remove different parts from current path
            while (currentPath.Count > commonParts)
            {
                currentPath.RemoveAt(currentPath.Count - 1);
            }
            
            // Add new directory parts
            while (currentPath.Count < parts.Length - 1)
            {
                var dirName = parts[currentPath.Count];
                sb.AppendLine($"{new string(' ', currentPath.Count * 2)}📁 {dirName}/");
                currentPath.Add(dirName);
            }
            
            // Add file
            var fileName = parts[^1];
            var icon = GetFileIcon(file.ContentType);
            sb.AppendLine($"{new string(' ', currentPath.Count * 2)}{icon} {fileName}");
        }
        
        return sb.ToString();
    }
    
    private static string GetFileIcon(string contentType)
    {
        return contentType switch
        {
            var ct when ct.StartsWith("text/") => "📄",
            var ct when ct.StartsWith("image/") => "🖼️",
            var ct when ct.StartsWith("application/json") => "📋",
            var ct when ct.StartsWith("application/lmml") => "🤖",
            var ct when ct.StartsWith("application/octet-stream") => "📦",
            var ct when ct.StartsWith("application/") => "📎",
            _ => "📄"
        };
    }

    public async Task<FileStat?> GetFileStat(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = FileSystemUtils.NormalizePath(path);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        
        var file = await context.Files.FirstOrDefaultAsync(f => 
            f.Share == Name && f.Path == normalizedPath, cancellationToken: cancellationToken);
        
        if (file == null)
            return null;

        return new FileStat
        { 
            Path = $"{file.SharePath}",
            Share = file.Share,
            ContentType = file.ContentType,
            Size = file.TextContent?.Length ?? file.BinaryContent?.Length ?? 0,
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt,
            Owner = file.Owner,
            LineCount = file.TextContent?.Split('\n').Length ?? 0,
        };
    }
}