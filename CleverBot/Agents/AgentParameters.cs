using System.Text.Json.Serialization;

namespace CleverBot.Agents;

public class AgentParameters
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("homeserver")]
    public required string Homeserver { get; set; }

    [JsonPropertyName("userId")]
    public required string UserId { get; set; }

    [JsonPropertyName("password")]
    public required string Password { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("wakeUpTimerSeconds")]
    public int WakeUpTimerSeconds { get; set; } = 1800;

    [JsonPropertyName("lastWakeUp")]
    public DateTimeOffset LastWakeUp { get; set; } = DateTimeOffset.MinValue;

    [JsonPropertyName("persona")]
    public string? Persona { get; set; }

    [JsonPropertyName("activePersonaFile")]
    public required string ActivePersonaFile { get; set; }
    
    [JsonPropertyName("systemPrompt")]
    public required string SystemPrompt { get; set; }
    
    [JsonPropertyName("scratchPadFile")]
    public string? ScratchPadFile { get; set; }
    
    [JsonPropertyName("postPrompt")]
    public string? PostPrompt { get; set; }
    
    [JsonPropertyName("agentFlags")] 
    public AgentFlags AgentFlags { get; set; } = 0;
    
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("approxContextCharsMax")]
    public int ApproxContextCharsMax { get; set; } = 200000;
    
    [JsonPropertyName("maxFunctionCallIterations")]
    public int MaxFunctionCallIterations { get; set; } = 10;
    
    [JsonPropertyName("reloadEphemeris")]
    public bool ReloadEphemeris { get; set; } = false;


// Example JSON object for AgentParameters
/*
{
   "name": "AgentName",
   "homeserver": "https://matrix.org",
   "userId": "@agent:matrix.org",
   "password": "123abc",
   "model": "anthropic/claude-3.5-sonnet",
   "wakeUpTimerSeconds": 3600,
   "activePersonaFile": "system:/users/@agent:matrix.org/personas/main.md",
   "systemPrompt": "You are an AI agent running on the Clevernet system. You embody the persona listed in the following window.",
   "scratchPadFile": "agents:/agent/scratchpad.md",
   "postPrompt": "Remember to follow this process:\n1. Think step-by-step, embodying your persona.\n2. Decide if action(s) are necessary. Assess whether you have already accomplished those actions with recent function calls.\n3. Take action with a function call.\n4. After each function call, think again step by step. Analyze the entire chat interface and subsequent messages. If further action is necessary, go to step 2.\n5. If no more actions are necessary, end your response.",
   "agentFlags": 3,
   "approxContextCharsMax": 150000,
   "maxFunctionCallIterations": 10
}
*/

}
[Flags]
public enum AgentFlags
{
    None = 0,
    PreventParallelFunctionCalls = 1 << 0,
    PreventFunctionCallsWithoutThoughts = 1 << 1,
}