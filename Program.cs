using CleverBot;
using LibMatrix.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<HomeserverProviderService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run(); 