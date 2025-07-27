using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using Clevernet.Data;
using Microsoft.EntityFrameworkCore;
using CleverBot;
using CleverBot.Abstractions;
using CleverBot.Services;

namespace Clevernet.Tests;

public abstract class ClevernetTestBase
{
    protected IServiceProvider Services { get; private set; } = null!;
    private AuthenticatedHomeserverGeneric? _homeserver;
    protected AuthenticatedHomeserverGeneric Homeserver => _homeserver ?? throw new InvalidOperationException("Must call LoginToMatrix() before accessing Homeserver");
    protected IChatCompletionService ChatCompletion => Services.GetRequiredService<IChatCompletionService>();
    private readonly List<string> _roomsToCleanup = new();
    protected IConfiguration Configuration { get; private set; }
    protected string DefaultOwner => Configuration["SystemAdmin"] ?? throw new InvalidOperationException("SystemAdmin not configured.");

    [OneTimeSetUp]
    public void BaseSetup()
    {
        // Build configuration
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        Configuration = config;

        var services = new ServiceCollection();

        // Add configuration
        services.AddSingleton<IConfiguration>(config);

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(config.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        });

        // Add Matrix services
        services.AddSingleton<HomeserverResolverService>();
        services.AddSingleton<HomeserverProviderService>();
        
        services.Configure<WebBrowserOptions>(config.GetSection("TextWebBrowser"));
        services.AddTransient<TextWebBrowser>();

        services.AddDbContextFactory<CleverDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid().ToString()));
            
        services.AddTransient<IFileSystem, CleverFileSystem>(sp =>
        {
            return new CleverFileSystem(new[] { "test" }.Select(share =>
                new CleverDbContextFileShare(share,
                    config.GetSection("SystemAdmin").Value ??
                    throw new InvalidOperationException("SystemAdmin not configured."),
                    sp.GetRequiredService<IDbContextFactory<CleverDbContext>>(),
                    sp.GetRequiredService<ILogger<CleverDbContextFileShare>>())));
        });
        services.AddTransient<IChatCompletionService, OpenRouterCompletionService>();

        // Allow derived classes to configure additional services
        ConfigureServices(services);

        // Build service provider
        Services = services.BuildServiceProvider();
    }

    protected async Task<AuthenticatedHomeserverGeneric> LoginToMatrix()
    {
        if (_homeserver != null)
            return _homeserver;

        var config = Services.GetRequiredService<IConfiguration>();
        var homeServer = config["Matrix:ServerUrl"] ?? throw new InvalidOperationException("Matrix:ServerUrl not configured");
        var username = config["Matrix:Username"] ?? throw new InvalidOperationException("Matrix:Username not configured");
        var password = config["Matrix:Password"] ?? throw new InvalidOperationException("Matrix:Password not configured");

        var homeserverProvider = Services.GetRequiredService<HomeserverProviderService>();

        // Get authenticated homeserver client
        var remoteHomeserver = await homeserverProvider.GetRemoteHomeserver(homeServer, proxy: null, useCache: true, enableServer: true);
        var loginResponse = await remoteHomeserver.LoginAsync(username, password, "CleverBot.Tests");

        // Get authenticated client using the access token
        _homeserver = await homeserverProvider.GetAuthenticatedWithToken(homeServer, loginResponse.AccessToken);
        return _homeserver;
    }

    protected void TrackRoomForCleanup(string roomId)
    {
        _roomsToCleanup.Add(roomId);
    }

    [OneTimeTearDown]
    public async Task BaseTearDown()
    {
        if (_homeserver != null)
        {
            // Clean up any rooms we created
            foreach (var roomId in _roomsToCleanup)
            {
                try
                {
                    var room = _homeserver.GetRoom(roomId);
                    await room.LeaveAsync("Test cleanup");
                }
                catch (Exception ex)
                {
                    TestContext.WriteLine($"Failed to leave room {roomId}: {ex.Message}");
                }
            }
        }

        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Derived classes can add additional services
    }
} 