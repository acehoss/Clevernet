using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CleverBot.Services;

public class GoogleService
{
    private readonly ILogger<GoogleService> _logger;
    private readonly IOptions<GoogleServiceConfiguration> _config;
    
    public GoogleService(ILogger<GoogleService> logger, IOptions<GoogleServiceConfiguration> config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<GoogleSearchResponse?> WebSearch(string query)
    {
        using (HttpClient client = new HttpClient())
        {
            var config = _config.Value;
            string url =
                $"https://www.googleapis.com/customsearch/v1?q={Uri.EscapeDataString(query)}&key={config.ApiKey}&cx={config.CseId}";
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode(); // Throw if not a success code

            string responseBody = await response.Content.ReadAsStringAsync();

            // Deserialize the JSON response
            var searchResults = JsonSerializer.Deserialize<GoogleSearchResponse>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return searchResults;
        }
    }
}

public class GoogleServiceConfiguration
{
    public string ApiKey { get; set; }
    public string CseId { get; set; }
}

// Define classes for deserialization
public class GoogleSearchResponse
{
    [JsonPropertyName("items")]
    public GoogleSearchItem[] Items { get; set; }
}

public class GoogleSearchItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; }
}