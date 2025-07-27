# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Clevernet is an advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem. It uses LMML (Language Model Markup Language) as its primary data interchange format and is implemented in C#/.NET 9.

## Build and Development Commands

### Building the Project
```bash
# Build the entire solution
dotnet build

# Build in release mode
dotnet build -c Release

# Build a specific project
dotnet build CleverBot/CleverBot.csproj
```

### Running the Application
```bash
# Run the main CleverBot application
dotnet run --project CleverBot

# Run with specific environment (Development/Production)
DOTNET_ENVIRONMENT=Development dotnet run --project CleverBot
DOTNET_ENVIRONMENT=Production dotnet run --project CleverBot
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test project
dotnet test Clevernet.Tests/Clevernet.Tests.csproj
```

### Database Operations
```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project Clevernet.Data --startup-project CleverBot

# Update database to latest migration
dotnet ef database update --project Clevernet.Data --startup-project CleverBot

# Remove last migration
dotnet ef migrations remove --project Clevernet.Data --startup-project CleverBot
```

### Code Quality Commands
```bash
# Format code
dotnet format

# Analyze code
dotnet build -warnaserror
```

## High-Level Architecture

### Core System Flow
1. **Agent Lifecycle**: Agents authenticate to Matrix, load configuration, and enter a wake/sleep cycle
2. **Event Processing**: Matrix events are converted to LMML format and queued for processing
3. **Context Building**: Complete UI built as LMML including rooms, windows, and conversation history
4. **LLM Integration**: Context sent to OpenRouter-compatible APIs with tool definitions
5. **Tool Execution**: Iterative function calling (max 10 iterations per wake)

### Key Components

#### CleverBot (Main Application)
- **Agent.cs**: Core agent implementation with processing loop
- **ConversationContext.cs**: Per-room state management and LMML rendering
- **Window.cs**: Content window system for files, web pages, and search results
- **AgentParameters.cs**: Runtime configuration for agents

#### Services Layer
- **OpenRouterCompletionService.cs**: Multi-model LLM integration via OpenRouter
- **CleverFileSystem.cs**: Virtual filesystem backed by PostgreSQL
- **TextWebBrowser.cs**: Web scraping via ScrapingFish API
- **GoogleService.cs**: Search integration
- **AgentConfigurationService.cs**: Agent config management

#### LMML System
- **LmmlElement.cs**: Base class for all LMML elements with XML serialization
- Elements represent messages, events, windows, function calls/results
- Self-describing XML format optimized for LLM token efficiency

#### Data Layer (Clevernet.Data)
- PostgreSQL database via Entity Framework Core
- **File.cs**: Virtual filesystem storage
- **Ephemeron.cs**: Agent memory persistence

#### LibMatrix Integration
- Third-party Matrix protocol library
- Handles authentication, sync, room operations
- Provides strongly-typed event models

### Critical Design Patterns

1. **Everything is LMML**: All agent context provided as a single LMML message
2. **Window-Based UI**: Content managed through scrollable windows with lifecycle
3. **Instance-Specific Tools**: Each agent generates unique tool names (e.g., `SendMessage_abc123`)
4. **Per-Room Context**: Separate conversation state maintained for each Matrix room
5. **Thought Separation**: Agent reasoning goes to special "thoughts room"

### Key Abstractions to Understand

1. **Processing Loop** (Agent.ProcessWithClaude):
   - Build system prompt with persona
   - Render complete LMML context
   - Send to LLM with tools
   - Execute returned function calls
   - Iterate until done or limit reached

2. **Window System**:
   - Line-based viewport (default 20 lines)
   - States: normal, maximized, minimized
   - Auto-close after N turns unless pinned
   - Query support via sub-agents

3. **Memory Architecture**:
   - Working: Current windows and context
   - Short-term: Recent messages
   - Long-term: PostgreSQL ephemeris

## Important Implementation Details

- Agents process up to 10 events per wake cycle
- Windows enforce character limits for token management
- RAG uses local ONNX embeddings (all-MiniLM-L6-v2)
- Typing indicators show during processing
- Boolean XML attributes use presence notation (no `="true"`)
- All file paths use `share:/path` format
- Ephemeris room has special handling in event pipeline

## Configuration

Agents configured via JSON files in `agents` share with parameters like:
- `model`: LLM model identifier
- `systemPrompt`: Base instructions
- `personaFile`: Markdown file defining agent identity
- `wakeUpTimerSeconds`: Processing interval (60-10800)
- `approxContextCharsMax`: Token limit (~50K default)