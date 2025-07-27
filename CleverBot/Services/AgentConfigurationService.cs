using System.Text.Json;
using CleverBot.Abstractions;
using CleverBot.Agents;
using Microsoft.EntityFrameworkCore;

namespace CleverBot.Services;

public class AgentConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AgentConfigurationService> _logger;

    public AgentConfigurationService(IFileSystem fileSystem, ILogger<AgentConfigurationService> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<AgentParameters> GetAgentParametersAsync(string agentName)
    {
        var agentConfigFilePath = $"system:/users/{agentName}/agent.json";
        var configFile = await _fileSystem.ReadFileAsync(agentConfigFilePath) ?? throw new Exception($"Unable to read agent configuration file {agentConfigFilePath} for {agentName}");
        if(configFile.ContentType != "application/json" || configFile.TextContent == null)
            throw new Exception("Agent configuration file content invalid");
        var agentParameters = JsonSerializer.Deserialize<AgentParameters>(configFile.TextContent) 
               ?? throw new Exception("Agent configuration deserialization failed");
        
        var personaFile = await _fileSystem.ReadFileAsync(agentParameters.ActivePersonaFile) ?? throw new Exception($"Unable to read persona file {agentParameters.ActivePersonaFile} for {agentName}");
        agentParameters.Persona = personaFile.TextContent;
        return agentParameters;
    }
}