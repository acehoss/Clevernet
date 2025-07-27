using CleverBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Clevernet.Tests;
//

public class TextWebBrowserTests : ClevernetTestBase
{
    [Test]
    public async Task TestBrowser()
    {
        var browser = Services.GetRequiredService<TextWebBrowser>();
        var (title, md) = await browser.GetMarkdownAsync("https://weather.gov/");
        Assert.That(title, Contains.Substring("National"));
        Assert.That(md, Contains.Substring("Commerce"));
    }
    
    [Test]
    public async Task TestMessy()
    {
        var browser = Services.GetRequiredService<TextWebBrowser>();
        var (title, md) = await browser.GetMarkdownAsync("https://techcrunch.com/2024/12/23/openais-o3-suggests-ai-models-are-scaling-in-new-ways-but-so-are-the-costs/");
        Assert.That(title, Contains.Substring("o3"));
        Assert.That(md, Contains.Substring("170x"));
    }
}