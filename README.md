# Clevernet

An advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem. Clevernet uses LMML (Language Model Markup Language) as its primary data interchange format, providing a structured way for agents to process events, maintain state, and interact with users and external systems.

---
> **Note**: This is a proof-of-concept implementation demonstrating complex agentic systems. While mostly functional, it's not production-ready and serves primarily as an example of advanced LLM agent architecture.
---
> **Note2**: A lot of this documentation was AI-generated when I wrote the software. I've fixed up things it got wrong as I could, but take it with a grain of salt. The code is the source of truth.
---

## Purpose

 I wrote this software over about a week while on vacation for the holidays in 2024. My focus was on getting a proof of concept for some ideas I had in my head:

- If I build agent context from chat rooms, could they demonstrate persistent existence  
- If agents can participate in chat rooms, they should able to interact with each other as well as humans

Some specific things I was hoping to see:

- Agents can understand a context containing information from multiple chat rooms ✅
- Agents exhibit understanding of time ✅
- Agents understand and can use the windowing system ✅
- Agents treat chat rooms separately but demonstrate understanding across them ✅
- Assign tasks via chat and agents plan and track the task ✅
- Agents can self-organize via chat ⏳

## Detailed Documentation

A much longer write-up about the internals of the software is in [Clevernet.md](Clevernet.md)

## Features

- **Persistent AI Agents**: Agents maintain context and memory across sessions using PostgreSQL-backed storage
- **Multi-Model Support**: Integration with any LLM that supports tool calling via OpenRouter API
- **Sophisticated Window System**: Content management through scrollable, searchable windows with lifecycle management
- **Virtual Filesystem**: Database-backed file storage with path-based organization
- **Extensible Tool Framework**: Function-calling system enabling diverse agent capabilities
- **LMML Processing**: Token-efficient XML markup language optimized for LLM interaction
- **Subagents**: agents can leverage subagents to interact with large-context data sources or handle summarization

## Architecture Overview

```
Clevernet
├── CleverBot (Main Application)        # Core agent implementation
│   ├── Agents                          # Agent lifecycle and processing
│   ├── Services                        # External integrations (LLM, web, search)
│   └── Abstractions                    # Interfaces (mostly unimplemented)
├── Clevernet.Data                      # Entity Framework models
└── LibMatrix                           # Matrix protocol library
```

## Key Concepts

### LMML (Language Model Markup Language)

A custom XML format designed for efficient token usage and clear structure:

```xml
<message systemId="matrix.org" roomId="!abc:matrix.org" 
         timestamp="2024-12-25T12:00:00Z" 
         sender="@user:matrix.org" 
         messageType="m.text">Hello world</message>

<window windowId="123" srcType="file" src="agents:/config.yaml" 
        contentType="text/yaml" lines="50" pinned system>
    # Actual file content here
</window>
```

### Agent Processing Loop

1. **Event Reception**: Matrix events converted to LMML format
2. **Context Building**: Complete UI constructed as LMML including rooms, windows, and conversation history
3. **LLM Integration**: Context sent to OpenRouter-compatible APIs with tool definitions
4. **Tool Execution**: Iterative function calling (max 10 iterations per wake)
5. **Response Handling**: Results sent back to Matrix rooms

### Window System for LLMs

Windows provide sophisticated content management:

- Line-based viewport (default 20 lines)
- States: normal, maximized, minimized
- Auto-close after N turns unless pinned
- Query support via sub-agents for large content

## Prerequisites

- .NET 9.0 SDK
- PostgreSQL database
- Matrix homeserver (Synapse recommended)
- OpenRouter API key
- Optional: Google Custom Search API key, ScrapingFish API key

## Installation

1. Clone the repository:
```bash
git clone https://github.com/yourusername/clevernet.git
cd clevernet
```

2. Install dependencies:
```bash
dotnet restore
```

3. Configure database connection in `CleverBot/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "CleverContext": "Host=localhost;Database=clevernet;Username=youruser;Password=yourpass"
  }
}
```

4. Run database migrations:
```bash
dotnet ef database update --project Clevernet.Data --startup-project CleverBot
```

5. Configure your agent in the virtual filesystem (see Configuration section)

## Configuration

Agents are configured via JSON files in the `agents` share of the virtual filesystem:

```json
{
  "loginUsername": "@agent:matrix.org",
  "loginPassword": "password",
  "displayName": "Friendly Agent",
  "model": "anthropic/claude-3-opus-20240229",
  "systemPrompt": "You are a helpful assistant...",
  "wakeUpTimerSeconds": 3600,
  "autoJoinInvites": true,
  "temperature": 1.0,
  "personaFile": "agents:/personas/agent.md",
  "scratchPadFile": "agents:/scratchpad.txt",
  "approxContextCharsMax": 50000
}
```

## Running

### Development Mode
```bash
DOTNET_ENVIRONMENT=Development dotnet run --project CleverBot
```

### Production Mode
```bash
DOTNET_ENVIRONMENT=Production dotnet run --project CleverBot
```

## Available Tools

Agents have access to various tools for interaction:

- **Messaging**: Send messages to Matrix rooms
- **File Operations**: Open, write, search, and manage files
- **Window Management**: Manipulate open windows (scroll, resize, pin, query)
- **Web Integration**: Browse web pages, search Google/YouTube
- **Memory/Search**: Semantic search across conversation history
- **Configuration**: Modify agent settings at runtime

## Testing

Run the test suite:
```bash
dotnet test
```

## Project Structure

- **CleverBot/**: Main application with agent implementation
  - `Agent.cs`: Core agent class with processing loop
  - `ConversationContext.cs`: Per-room state management
  - `Window.cs`: Window system implementation
  - `Services/`: External service integrations
- **Clevernet.Data/**: Database models and migrations
- **Clevernet.Tests/**: Unit and integration tests
- **LibMatrix/**: Third-party Matrix protocol library

## Development Notes

- Agents process up to 10 events per wake cycle
- Windows enforce character limits for token management
- Boolean XML attributes use presence notation (no `="true"`)
- All file paths use `share:/path` format
- Thought room (`!bRQNfEcHgLvdeTaUHT:matrix.org`) shows agent reasoning

## Known Limitations

- Single instance per agent (no horizontal scaling of individual agents)
- Tightly coupled to PostgreSQL
- Limited abstraction implementations
- No built-in authentication beyond Matrix credentials

## Future Enhancements

- Multi-agent coordination with shared memory
- Pluggable LLM providers beyond OpenRouter
- Code execution capabilities
- Enhanced memory systems (episodic, knowledge graphs)
- Better abstractions for storage and messaging

## Contributing

This is a proof-of-concept project and not actively maintained. Feel free to fork and experiment!

## License

MIT License

## Acknowledgments

- Built with LibMatrix for Matrix protocol support
- Uses OpenRouter for multi-model LLM access
- ONNX Runtime for local embeddings