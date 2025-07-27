using System.Collections.Concurrent;
using SmartComponents.LocalEmbeddings;

namespace CleverBot.Agents;

public class EmbeddingRepository<T>
{
    private readonly ILogger<EmbeddingRepository<T>> _logger;
    private readonly LocalEmbedder _localEmbedder;
    private readonly ConcurrentBag<EmbeddingResult<T>> _embeddings = new();

    public EmbeddingRepository(ILogger<EmbeddingRepository<T>> logger, LocalEmbedder localEmbedder)
    {
        _logger = logger;
        _localEmbedder = localEmbedder;
    }
    
    public SimilarityScore<EmbeddingResult<T>>[] SearchEvents(string query, int maxResults = 10, Func<EmbeddingResult<T>, bool>? filter = null)
    {
        var queryChunks = ChunkText(query).ToList();
        var allResults = new List<SimilarityScore<EmbeddingResult<T>>>();
        var allEmbeddings = _embeddings.ToArray();
        if(filter != null)
            allEmbeddings = allEmbeddings.Where(filter).ToArray();
        var embeddingPairs = allEmbeddings.Select(i => (i, i.Embedding)).ToArray();

        foreach (var chunk in queryChunks)
        {
            var queryEmbedding = _localEmbedder.Embed(chunk);
            var chunkResults = LocalEmbedder.FindClosestWithScore(queryEmbedding, embeddingPairs, maxResults: maxResults);
            allResults.AddRange(chunkResults);
        }

        // Combine results, taking highest score when same result appears multiple times
        return allResults
            .GroupBy(r => r.Item)
            .Select(g => g.OrderByDescending(r => r.Similarity).First())
            .OrderByDescending(r => r.Similarity)
            .Take(maxResults)
            .ToArray();
    }

    public EmbeddingResult<T>[] Embed(T item, string text)
    {
        var chunks = ChunkText(text).ToList();
                    
        var results = new List<EmbeddingResult<T>>();
        foreach (var chunk in chunks)
        {
            var embedding = _localEmbedder.Embed(chunk);
            var embeddingResult = new EmbeddingResult<T>
            {
                Item = item,
                Embedding = embedding,
                TextChunk = chunk
            };
            _embeddings.Add(embeddingResult);
            results.Add(embeddingResult);
        }
        return results.ToArray();
    }
    
    private const int ChunkSize = 512;
    private const int ChunkOverlap = 50;

    private static IEnumerable<string> ChunkText(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var word in words)
        {
            currentChunk.Add(word);
            currentLength += word.Length + 1; // +1 for space

            if (currentLength >= ChunkSize)
            {
                yield return string.Join(" ", currentChunk);
                
                // Keep overlap words for next chunk
                var overlapWords = currentChunk.Skip(Math.Max(0, currentChunk.Count - (ChunkOverlap / 4))).ToList();
                currentChunk.Clear();
                currentChunk.AddRange(overlapWords);
                currentLength = string.Join(" ", overlapWords).Length + 1;
            }
        }

        if (currentChunk.Any())
            yield return string.Join(" ", currentChunk);
    }
}

public class EmbeddingResult<T> : IEquatable<EmbeddingResult<T>>
{
    public required T Item { get; set; }
    public required EmbeddingF32 Embedding { get; set; }
    public required string TextChunk { get; set; }

    public bool Equals(EmbeddingResult<T>? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Item != null && Item.Equals(other.Item) && TextChunk == other.TextChunk;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((EmbeddingResult<T>)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Item, TextChunk);
    }
}