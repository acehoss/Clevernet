namespace CleverBot.Abstractions;

public interface IFileSystem
{
    Task<IEnumerable<IFileShare>> GetSharesAsync(CancellationToken cancellationToken = default);
    Task<ClevernetFile?> ReadFileAsync(string sharePath, CancellationToken cancellationToken = default);
    Task WriteFileAsync(string sharePath, string content, string owner, string contentType, FileWriteMode mode, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string sharePath, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchMatch>> SearchAsync(string sharePath, string query, FileSearchMode fileSearchMode, CancellationToken cancellationToken = default);
    Task<string> GetDirectoryTree(string sharePath, CancellationToken cancellationToken = default);
    Task<FileStat?> GetFileStat(string sharePath, CancellationToken cancellationToken = default);
}

public interface IFileShare
{
    string Name { get; }
    Task<ClevernetFile?> ReadFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteFileAsync(string path, string content, string owner, string contentType, FileWriteMode mode, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task<IEnumerable<SearchMatch>> SearchAsync(string path, string query, FileSearchMode fileSearchMode, CancellationToken cancellationToken = default);
    Task<string> GetDirectoryTree(string path, CancellationToken cancellationToken = default);
    Task<FileStat?> GetFileStat(string path, CancellationToken cancellationToken = default);
}

public static class FileSystemUtils
{
    public static (string ShareName, string FilePath) ParsePath(string path)
    {
        var parts = path.Split(":/", 2);
        if (parts.Length != 2)
            throw new ArgumentException("Path must be in format 'ShareName:/path/to/file'", nameof(path));
        
        return (parts[0], parts[1]);
    }

    public static string NormalizePath(string path)
    {
        return path.TrimStart('/');
    }
}

public class FileStat
{
    public required string Path { get; set; }
    public required string Share { get; set; }
    public required string ContentType { get; set; }
    public required long Size { get; set; }
    public required int LineCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string Owner { get; set; }
}

public class SearchMatch
{
    public required string Path { get; set; }
    public required string ContentType { get; set; }
    public required long Size { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }
    public required string Owner { get; set; }
    public required int LineNumber { get; set; }
    public string? BeforeLine { get; set; }
    public string? MatchLine { get; set; }
    public string? AfterLine { get; set; }
    public bool IsBinary { get; set; } = false;

    public LmmlElement ToLmml(bool suppressContent = false)
    {
        var el = new LmmlElement("searchMatch")
        {
            Attributes = new ()
            {
                ["path"] = Path,
                ["contentType"] = ContentType,
                ["size"] = Size.ToString(),
                ["createdAt"] = CreatedAt.ToString(Constants.DateTimeFormat),
                ["updatedAt"] = UpdatedAt.ToString(Constants.DateTimeFormat),
                ["owner"] = Owner,
            }
        };
        if (suppressContent)
        {
            el.Attributes["lineNumber"] = LineNumber.ToString();
            el.Attributes["matchContent"] = "suppressed";
            
        }
        else if (LineNumber == 0)
        {
            el.Attributes["matchContent"] = "filename";
        }
        else if (LineNumber == -1)
        {
            el.Attributes["lineNumber"] = "unavailable";
            el.Attributes["preview"] = "unavailable";
        }
        else
        {
            el.Attributes["lineNumber"] = LineNumber.ToString();
            el.Content = new LmmlStringContent(string.Join("\n",new [] { BeforeLine, MatchLine, AfterLine  }.Where(x => x is not null)));
        }
        return el;
    }
}

public enum FileWriteMode
{
    CreateText,
    CreateBinary,
    OverwriteText,
    OverwriteBinary,
    AppendText,
    AppendLineText,
    AppendLineTextWithTimestamp
}

public enum FileSearchMode
{
    FileNames,
    FileContents,
}

public class ClevernetFile
{
    public ClevernetFile() {}

    public ClevernetFile(Clevernet.Data.File file)
    {
        Id = file.Id;
        Path = file.Path;
        Share = file.Share;
        ContentType = file.ContentType;
        TextContent = file.TextContent;
        BinaryContent = file.BinaryContent;
        CreatedAt = file.CreatedAt;
        UpdatedAt = file.UpdatedAt;
        Owner = file.Owner;
    }
    
    public int Id { get; set; }
    public string Path { get; set; }
    public string SharePath => $"{Share}:/{Path}";
    public string Owner { get; set; }
    public string Share { get; set; }
    public string ContentType { get; set; }
    public string? TextContent { get; set; }
    public byte[]? BinaryContent { get; set; }
    public int LineCount => TextContent?.Count(c => c == '\n') ?? 0;
    public string OpenFileContent => TextContent ?? ($"Binary file ({BinaryContent?.Length ?? 0} bytes)");
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}