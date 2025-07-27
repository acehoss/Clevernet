using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleverBot.Services;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace Clevernet.Tests;

[TestFixture]
public class GoogleTests
{
    // [Test]
    public async Task TestYoutubeSearch()
    {
        var youtube = new YoutubeClient();
        var videos = await youtube.Search.GetVideosAsync("wes roth").Take(20);
        Assert.That(videos, Is.Not.Null);
        Assert.That(videos.Count, Is.GreaterThan(0));
        foreach (var video in videos)
        {
            Console.WriteLine(video.Title);
        }
    }
    
    // [Test]
    public async Task TestGoogleSearch()
    {
        // Replace these with your actual API credentials
        string apiKey = "...";
        string cx = "...";
        string query = "anthropic claude";

        string url = $"https://www.googleapis.com/customsearch/v1?q={Uri.EscapeDataString(query)}&key={apiKey}&cx={cx}";

        using (HttpClient client = new HttpClient())
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode(); // Throw if not a success code

                string responseBody = await response.Content.ReadAsStringAsync();
                
                // Deserialize the JSON response
                var searchResults = JsonSerializer.Deserialize<GoogleSearchResponse>(responseBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                Console.WriteLine("Search Results:");
                if (searchResults?.Items != null)
                {
                    foreach (var item in searchResults.Items)
                    {
                        Console.WriteLine($"Title: {item.Title}");
                        Console.WriteLine($"Link: {item.Link}");
                        Console.WriteLine($"Description: {item.Snippet}\n");
                    }
                }
                else
                {
                    Console.WriteLine("No results found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}