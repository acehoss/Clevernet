using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ReverseMarkdown;

namespace CleverBot.Services;

public class TextWebBrowser
{
    private readonly IOptions<WebBrowserOptions> _options;
    private readonly ILogger<TextWebBrowser> _logger;
    private readonly ReverseMarkdown.Converter _md;
    public TextWebBrowser(ILogger<TextWebBrowser> logger, IOptions<WebBrowserOptions> options)
    {
        _logger = logger;
        _options = options;
        _md = new ReverseMarkdown.Converter(new ReverseMarkdown.Config
        {
            GithubFlavored = false,
            RemoveComments = true,
            SmartHrefHandling = true,
            TableHeaderColumnSpanHandling = true,
            CleanupUnnecessarySpaces = true,
            UnknownTags = Config.UnknownTagsOption.Bypass,
        });
    }

    public async Task<(string? title, string markdown)> GetRawAsync(string url, CancellationToken cancellationToken = default)
    {
        var scrapingFishUrl = _options.Value.ScrapingFishUrl;
        var apiKey = _options.Value.ScrapingFishApiKey;

        if (string.IsNullOrEmpty(scrapingFishUrl) || string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("ScrapingFishUrl or ScrapingFishApiKey is not configured.");
        }

        var httpClient = new HttpClient();
        var requestUrl = $"{scrapingFishUrl}?api_key={apiKey}&url={Uri.EscapeDataString(url)}";

        _logger.LogInformation($"Sending GET request to {requestUrl}");

        var response = await httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var title = Regex.Match(raw, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)?.Groups[1].Value?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = null;
        }
        return (title, raw);
    }

    public async Task<(string? title, string markdown)> GetMarkdownAsync(string url, CancellationToken cancellationToken = default)
    {
        var (title, raw) = await GetRawAsync(url, cancellationToken);
        var markdown = _md.Convert(raw);
        return (title, markdown);
    }
}

public class WebBrowserOptions
{
    public string ScrapingFishUrl { get; set; }
    public string ScrapingFishApiKey { get; set; }
}