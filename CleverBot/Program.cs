using CleverBot;
using CleverBot.Agents;
using LibMatrix.Services;
using CleverBot.Abstractions;
using CleverBot.Services;
using Clevernet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartComponents.LocalEmbeddings;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();


builder.Services.AddTransient<HomeserverResolverService>();
builder.Services.AddTransient<HomeserverProviderService>();
builder.Services.AddTransient<IChatCompletionService, OpenRouterCompletionService>();
builder.Services.Configure<WebBrowserOptions>(builder.Configuration.GetSection("TextWebBrowser"));
builder.Services.Configure<GoogleServiceConfiguration>(builder.Configuration.GetSection("Google"));
builder.Services.AddTransient<GoogleService>();
builder.Services.AddTransient<TextWebBrowser>();
builder.Services.AddTransient<AgentConfigurationService>();
builder.Services.AddSingleton<LocalEmbedder>();
builder.Services.AddTransient<IFileSystem, CleverFileSystem>(sp =>
{
    return new CleverFileSystem(new[] { "system", "agents" }.Select(share =>
        new CleverDbContextFileShare(share,
            builder.Configuration.GetSection("SystemAdmin").Value ??
            throw new InvalidOperationException("SystemAdmin not configured."),
            sp.GetRequiredService<IDbContextFactory<CleverDbContext>>(),
            sp.GetRequiredService<ILogger<CleverDbContextFileShare>>())));
});
builder.Services.AddDbContextFactory<CleverDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? 
        throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    options.UseNpgsql(connectionString, o => {
        o.MigrationsHistoryTable("__EFMigrationsHistory", "clevernet");
    }).ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});

// Skip agent validation if running in design time (e.g. EF tools)
var isDesignTime = AppDomain.CurrentDomain.GetAssemblies()
    .Any(a => a.FullName?.Contains("Microsoft.EntityFrameworkCore.Design") == true);

if (!isDesignTime)
{
    var agents = builder.Configuration.GetSection("Agents").Get<string[]>() ?? 
                 throw new InvalidOperationException("Agents not configured.");

    foreach (var agentName in agents)
    {
        builder.Services.AddSingleton(sp =>
        {
            var acs = sp.GetRequiredService<AgentConfigurationService>();
            var agentParameters = acs.GetAgentParametersAsync(agentName).GetAwaiter().GetResult() ??
                                  throw new Exception($"Agent {agentName} configuration not found");

            // Use reflection to resolve dependencies
            var ctor = typeof(Agent).GetConstructors().Single();
            var args = ctor.GetParameters()
                .Select(p =>
                    p.ParameterType == typeof(AgentParameters) ? agentParameters : sp.GetRequiredService(p.ParameterType))
                .ToArray();

            return (Agent)Activator.CreateInstance(typeof(Agent), args)!;
        });
    }

    builder.Services.AddHostedService<CompositeAgentHostedService>();
}

var host = builder.Build();
host.Run();
