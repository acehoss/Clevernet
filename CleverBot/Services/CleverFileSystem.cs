using CleverBot.Abstractions;

namespace CleverBot.Services;

public class CleverFileSystem : IFileSystem 
{
    public readonly List<IFileShare> Shares;

    public CleverFileSystem(IEnumerable<IFileShare> shares)
    {
        Shares = shares.ToList();
    }
    
    public (IFileShare, string) GetShare(string sharePath)
    {
        var (shareName, filePath) = FileSystemUtils.ParsePath(sharePath);
        var normalizedPath = FileSystemUtils.NormalizePath(filePath);
        var share = Shares.FirstOrDefault(x => x.Name == shareName);
        if(share == null)
            throw new Exception("Share not found");
        return (share, normalizedPath);
    }

    public async Task<IEnumerable<IFileShare>> GetSharesAsync(CancellationToken cancellationToken = default)
    {
        return Shares;
    }

    public async Task<ClevernetFile?> ReadFileAsync(string sharePath, CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        return await share.ReadFileAsync(path, cancellationToken);
    }

    public async Task WriteFileAsync(string sharePath, string content, string owner, string contentType, 
        FileWriteMode mode, CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        if (share.Name == "system" && owner != "@hoss:matrix.org")
            throw new InvalidOperationException("ERROR: system share is read-only");
        await share.WriteFileAsync(path, content, owner, contentType, mode, cancellationToken);
    }

    public async Task DeleteFileAsync(string sharePath, CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        await share.DeleteFileAsync(path, cancellationToken);
    }

    public async Task<IEnumerable<SearchMatch>> SearchAsync(string sharePath, string query, FileSearchMode fileSearchMode,
        CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        if(share == null)
            throw new Exception("Share not found");
        return await share.SearchAsync(path, query, fileSearchMode, cancellationToken);
    }

    public async Task<string> GetDirectoryTree(string sharePath, CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        return await share.GetDirectoryTree(path, cancellationToken);
    }

    public async Task<FileStat?> GetFileStat(string sharePath, CancellationToken cancellationToken = default)
    {
        var (share, path) = GetShare(sharePath);
        return await share.GetFileStat(path, cancellationToken);
    }
}