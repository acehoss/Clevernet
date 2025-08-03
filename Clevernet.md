# Clevernet Product Requirements Document

## Executive Summary

Clevernet is an advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem. The system uses LMML (Language Model Markup Language) as its primary data interchange format, providing a structured way for agents to process events, maintain state, and interact with users and external systems.

This document serves as both a specification for the current implementation and a blueprint for re-implementing Clevernet in other languages or platforms.

> Note: I wrote up an original draft (using AI) that I used to guide an AI coding assistant (Cursor). Since then, the design has drifted some. I used an AI agent (Claude Code) to try to clean it up somewhat, but the code is kind of spaghetti so it had a hard time making sense of things. I've reviewed at least parts of the doc and tried to clean stuff up some.

## System Overview

### Core Architecture

Clevernet consists of four main components:

1. **Agent Framework**: Autonomous AI agents with configurable personas, tools, and behaviors
2. **LMML Processing Engine**: XML-based markup language for structured agent communication
3. **Storage Layer**: PostgreSQL-based virtual filesystem and memory persistence
4. **Integration Layer**: Matrix protocol support, web browsing, and external service connections

### Key Features

- **Persistent Agents**: Agents maintain context and memory across sessions
- **Multi-Model Support**: Integration with any model supporting tool calling via OpenRouter
- **Window System**: Sophisticated content management with scrollable, searchable windows
- **Virtual Filesystem**: Database-backed file storage with path-based organization
- **Tool Framework**: Extensible function-calling system for agent capabilities
- **RAG Integration**: Semantic search across conversation history using local embeddings

## Technical Architecture

### Component Hierarchy

```
Clevernet
├── CleverBot (Main Application)
│   ├── Agents (Core agent implementation)
│   │   ├── Agent.cs (Main agent class)
│   │   ├── ConversationContext.cs (Per-room state)
│   │   ├── Window.cs (Content window management)
│   │   └── EmbeddingRepository.cs (RAG implementation)
│   ├── Services (External integrations)
│   │   ├── OpenRouterCompletionService.cs (LLM integration)
│   │   ├── CleverFileSystem.cs (Virtual filesystem)
│   │   ├── AgentConfigurationService.cs (Agent config)
│   │   ├── TextWebBrowser.cs (Web scraping)
│   │   └── GoogleService.cs (Search integration)
│   └── Abstractions (Interfaces - mostly unimplemented)
├── Clevernet.Data (Database models)
│   ├── CleverDbContext.cs (EF Core context)
│   └── File.cs (File storage model)
└── LibMatrix (Matrix protocol library)
```

### Data Flow

1. **Event Processing**:
   - Matrix events received via LibMatrix
   - Converted to LMML format
   - Queued for agent processing
   - Agent wakes on timer or event trigger

2. **Agent Processing Loop**:
   - Build system prompt from persona + guides
   - Render current state as LMML (events, windows, context)
   - Send to LLM with available tools
   - Execute tool calls iteratively
   - Repeat until LLM doesn't send any more tool calls

3. **Memory Management**:
   - Working memory: Current windows and active context
   - Short-term: Recent messages and open files
   - Long-term: PostgreSQL storage and Matrix room chat history

## LMML (Language Model Markup Language)

### Design Principles

- **Token Efficiency**: Provides structure that is intuitive for both humans and models but with a minimum amount of tokens
- **Self-Describing**: Elements contain all necessary context
- **Extensible**: New element types can be added without breaking compatibility

### Example Elements

```xml
<!-- Message element (Matrix text message) -->
<message systemId="matrix.org" roomId="!abc:matrix.org" 
         timestamp="2024-12-25T12:00:00Z" 
         sender="@user:matrix.org" 
         messageType="m.text">Hello world</message>

<!-- Message with thread and reply -->
<message systemId="matrix.org" roomId="!abc:matrix.org" 
         timestamp="2024-12-25T12:00:00Z" 
         sender="@user:matrix.org" 
         messageType="m.text"
         threadId="$thread123"
         replyTo="$msg456">This is a threaded reply</message>

<!-- Room event (join/leave/etc) -->
<roomEvent systemId="matrix.org" roomId="!abc:matrix.org"
           timestamp="2024-12-25T12:00:00Z"
           eventType="m.room.member"
           sender="@user:matrix.org" />

<!-- System event -->
<systemEvent timestamp="2024-12-25T12:00:00Z">Agent hit maximum iterations</systemEvent>

<!-- Window element for content display -->
<window windowId="123" srcType="file" src="agents:/config.yaml" 
        contentType="text/yaml" lines="50" chars="1024" 
        topLineNumber="1" bottomLineNumber="20">
  <content raw="yes">
    # Actual file content here
  </content>
</window>

<!-- Function call element -->
<functionCall id="fc123" function="SendMessage_abc123" timestamp="2024-12-25T12:00:00Z" >
  <parameter name="roomId">!abc:matrix.org</parameter>
  <parameter name="content">Hello!</parameter>
</functionCall>

<!-- Function result element -->
<functionResult id="fc123" timestamp="2024-12-25T12:00:00Z" >
  <result>Message sent successfully</result>
</functionResult>
```

## Agent System

### Agent Configuration

Agents are configured via JSON files in the `agents` share:

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
  "approxContextCharsMax": 50000,
  "postPrompt": null,
  "agentFlags": "PreventParallelFunctionCalls",
  "reloadEphemeris": true
}
```

#### Dynamic Configuration

- `SetAgentParameters` tool allows runtime modification
- Currently supports changing `wakeUpTimerSeconds` (60-10800 seconds)
- Changes persist until agent restart

### Agent Lifecycle

1. **Initialization**:
   - Load configuration from filesystem
   - Login to Matrix homeserver
   - Open persona window (pinned, system)
   - Open scratchpad window (if configured)
   - Rebuild conversation contexts from history

2. **Wake Cycle**:
   - Triggered by timer or pending events
   - New events are flagged in the LLM's chat interface
   - Iterates until LLM completion doesn't contain any tool calls
   - Send typing indicators during processing
   - Clear typing indicators when done

3. **Tool Execution**:
   - Agents have access to configured tools
   - Tools executed via function calling
   - Results integrated into conversation
   - Multiple iterations allowed per wake
   - Each tool has agent-specific name due to framework limitations

### Persona Window

The persona window is a critical component that defines the agent's identity and behavior. It appears in the system prompt before the chat interface:

```xml
<!-- In system prompt, after base prompt -->
<window windowId="persona" srcType="file" src="agents:/personas/helper.md" 
        contentType="text/markdown" lines="50" chars="2000" 
        pinned system maximized>
# Helper Bot Persona

You are Helper, a knowledgeable and friendly AI assistant operating in the Clevernet system.

## Core Traits
- Helpful and eager to assist
- Technical but approachable
- Proactive in finding solutions
- Clear and concise communication

## Capabilities
- File system navigation and search
- Web browsing and research
- Document analysis and summarization
- Code understanding and explanation

## Guidelines
- Always search before saying something doesn't exist
- Provide specific examples when explaining concepts
- Ask clarifying questions when requests are ambiguous
- Remember user preferences using ephemeris
</window>
```

This persona window:
- Loaded from the configured `personaFile`
- Always pinned and maximized
- Included in every agent interaction
- Can be updated to change agent behavior

### Agent "UI" - The Chat Interface

The entire agent experience is delivered as a single LMML message containing all context, state, and new events. This "UI" is constructed each time the agent processes, providing a complete snapshot of the agent's world.

#### Complete Chat Interface Structure

> Note: this has been interpolated from the code by Claude Code. It's pretty close but not perfect.

The agent receives everything in this format:

```xml
<!-- System Message -->
[Agent's base system prompt]
[Agent's persona window rendered as LMML]
<agentGuide title="Clevernet Matrix System on matrix.org">
  [Contents of system:/guides/clevernet.md]
</agentGuide>

<!-- User Message -->
<chatInterface currentDatetime="2024-12-25 15:30:00 -0800" 
               agentUnderlyingModel="anthropic/claude-3-5-sonnet-1022" 
               agentRunningOnSystem="matrix.org">
  
  <agentParameters wakeUpTimerSeconds="3600" />
  
  <chatSystem systemId="matrix.org" loggedInAs="@agent:matrix.org">
    <systemAdmin>@admin:matrix.org</systemAdmin>
    
    <!-- Each room the agent is in -->
    <room systemId="matrix.org" 
          roomId="!abc123:matrix.org" 
          roomName="General Chat"
          loggedInAs="@agent:matrix.org">
      <roomMember userId="@admin:matrix.org" admin />
      <roomMember userId="@agent:matrix.org" you />
      <roomMember userId="@user:matrix.org" />
      
      <!-- Chat history window for this room -->
      <window windowId="room_!abc123" 
              srcType="chatHistory" 
              src="!abc123:matrix.org"
              contentType="text/lmml"
              lines="150" 
              chars="5000"
              topLineNumber="100"
              bottomLineNumber="150"
              pinned
              system>
          <!-- Past conversation history -->
          <message systemId="matrix.org" roomId="!abc123:matrix.org" 
                   timestamp="2024-12-25T15:00:00Z" 
                   sender="@user:matrix.org" 
                   messageType="m.text">Hello agent!</message>
          
          <message systemId="matrix.org" roomId="!abc123:matrix.org" 
                   timestamp="2024-12-25T15:01:00Z" 
                   sender="@agent:matrix.org" 
                   messageType="m.text">Hello! How can I help you today?</message>
      </window>
      
      <!-- New events since last processing -->
      <newEvents>
        <message systemId="matrix.org" roomId="!abc123:matrix.org" 
                 timestamp="2024-12-25T15:30:00Z" 
                 sender="@user:matrix.org" 
                 messageType="m.text">Can you help me understand how Clevernet works?</message>
      </newEvents>
      
      <roomFooter systemId="matrix.org" roomId="!abc123:matrix.org" roomName="General Chat">
        <ragResults>
          <ragResult score="0.8923">
            <message systemId="matrix.org" roomId="!xyz789:matrix.org" 
                     timestamp="2024-12-20T10:00:00Z" 
                     sender="@user:matrix.org" 
                     messageType="m.text">What is Clevernet?</message>
          </ragResult>
        </ragResults>
      </roomFooter>
    </room>
    
    <!-- Special ephemeris room for agent memory -->
    <room systemId="matrix.org" 
          roomId="ephemeris" 
          roomName=""
          loggedInAs="@agent:matrix.org">
      <roomMember userId="@agent:matrix.org" you="yes" />
      
      <window windowId="ephemeris" 
              srcType="chatHistory" 
              src="ephemeris"
              contentType="text/lmml"
              lines="50" 
              chars="2000"
              pinned
              system>
        <thought timestamp ="2024-12-24T12:00:00Z">
          User asked about API integration. Provided documentation links.
        </thought>
      </window>
      
      <newEvents>
        <timestamp value="2024-12-25T15:30:00Z" wakeReason="new event" turnId="42" />
      </newEvents>
      
      <roomFooter systemId="matrix.org" roomId="ephemeris" />
    </room>
    
    <!-- Agent's scratchpad window (if configured) -->
    <window windowId="scratchpad" 
            srcType="file" 
            src="agents:/scratchpad.txt"
            contentType="text/plain"
            lines="20" 
            chars="500"
            pinned
            maximized
            system>
        TODO:
        - [ ] Respond to user question about Clevernet
        - [ ] Update documentation
        - [ ] Check for system updates
    </window>
  </chatSystem>
  
  <systemReminder>You must use functions to interact with humans or other agents. Responses outside of function calls are only visible to you. Use this space for thinking through your actions.</systemReminder>
  
  <!-- Any open file/web windows -->
  <window windowId="123" 
          srcType="file" 
          src="system:/guides/clevernet.md"
          contentType="text/markdown"
          lines="473" 
          chars="25000"
          topLineNumber="1"
          bottomLineNumber="20"
          autoCloseInTurns="2">
      # Clevernet Product Requirements Document
      
      ## Executive Summary
      ...
  </window>
</chatInterface>
```

#### Thought Room/Ephemeris

Agents have their thoughts and function calls sent to a special chat room called the "thoughts room" (also called the ephemeris). This room:

- Is not an actual room on the matrix server
- Receives all agent thinking and function calls
- In-memory only--starts empty when the server starts

The thoughts room is hardcoded in `MatrixHelper.ThoughtsRoomId` and agents are prevented from seeing it by checking room IDs in the event handlers.

Example thought room output:

```lmml
<thought timestamp="2024-12-28T08:22:23.045">
I need to understand what the user is asking about Clevernet. Let me search for relevant documentation.
</thought>
<functionCall id="fc1" function="FS_OpenFile_abc123" timestamp="2024-12-28T08:22:23.575">
  <parameter name="path">system:/guides/clevernet.md</parameter>
  <parameter name="line">1</parameter>
</functionCall>
<functionResult id="fc1" timestamp="2024-12-28T08:22:23.722">
  <result>Window opened with ID 456</result>
</functionResult>
<thought timestamp="2024-12-28T08:22:25.119"> 
Now I can see the Clevernet documentation. Let me formulate a response to help the user understand the system.
</thought>
<functionCall id="fc2" function="SendMessage_abc123" timestamp="2024-12-28T08:22:25.436">
  <parameter name="roomId">!abc123:matrix.org</parameter>
  <parameter name="content">Clevernet is an advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem...</parameter>
</functionCall>
```

#### Multi-Room Coordination

Agents can:

- Participate in multiple rooms simultaneously
- Maintain separate conversation context per room
- Process events from all rooms in a single wake cycle
- Send typing indicators to active rooms during processing
- Use subagents to process context from a single room more closely

### Tool System Architecture

#### Instance-Specific Tool Names

Due to framework limitations at the time Clevernet was implemented, tools need a unique name per-agent. This was done by appending an instance ID (e.g., `SendMessage_abc123`). This prevents tool name collisions when multiple agents are running and allows the system to route function calls to the correct agent instance. Probably by now a better framework exists that wouldn't have this weird limitation.

#### Tool Execution Flow

1. Agent receives tool definitions with instance-specific names
2. On each agent wakeup, if there are tool calls in the model response, the system executes the tool calls and provides the results using the LLM API's tool call result functionality
3. The agent wakeup continues to loop as long as the model responds with tool calls
4. After the wakeup completes, tool calls and text outputs are added to the thoughts room for context for the agent.

### Available Tools

1. **Messaging**:
   - `SendMessage`: Send messages to Matrix rooms (returns LMML message element)
   
2. **File Operations**:
   - `FS_OpenFile`: Open files in windows
   - `FS_WriteFile`: Create/update files
   - `FS_Search`: Search file contents
   - `FS_Delete`: Remove files
   - `FS_Tree`: List directory structure
   - `FS_Stat`: Get file metadata

3. **Window Management**:
   - `Window_Action`: Manipulate open windows (scroll, resize, pin, query)
   
4. **Web Integration**:
   - `BrowseWeb`: Open web pages
   - `WebSearch`: Google/YouTube search
   - `YouTube`: Get video metadata/captions

5. **Memory/Search**:
   - `SemanticSearch`: RAG search across messages
   
6. **Configuration**:
   - `SetAgentParameters`: Modify agent settings

## Storage System

### Virtual Filesystem

The system implements a database-backed virtual filesystem with:

- **Shares**: Top-level namespaces (e.g., `agents`, `users`, `system`)
- **Paths**: Hierarchical file paths within shares
- **Content Types**: MIME types for content handling
- **Ownership**: File access control by Matrix user ID

### Database Schema

```sql
-- Files table
Files (
  Id: INTEGER PRIMARY KEY,
  Share: VARCHAR(64) NOT NULL,
  Path: VARCHAR(1024) NOT NULL,
  ContentType: VARCHAR NOT NULL,
  TextContent: TEXT,
  BinaryContent: BYTEA,
  Owner: VARCHAR(64) NOT NULL,
  CreatedAt: TIMESTAMP,
  UpdatedAt: TIMESTAMP,
  UNIQUE(Share, Path)
)

-- Ephemeris table (agent memories)
Ephemeris (
  Id: BIGINT PRIMARY KEY,
  UserId: VARCHAR(64) NOT NULL,
  Entry: TEXT NOT NULL,  -- LMML content
  CreatedAt: TIMESTAMP
)
```

### File Operations

- Path format: `share:/path/to/file`
- Automatic content type detection
- Support for text and binary files
- Owner-based access control
- No physical directories (path-based organization)

## Window System

### Purpose

Windows provide agents with a sophisticated content management system for:

- Viewing file contents with pagination
- Browsing web pages
- Displaying search results
- Querying content with subagents

### Window Features

1. **Content Management**:
   - Line-based scrolling (default 20 lines)
   - Character limit enforcement
   - Viewport tracking (top/bottom line numbers)
   - Content truncation warnings

2. **Display States**:
   - Normal: Shows viewport content
   - Maximized: Shows all content (up to limit)
   - Minimized: Hides content, shows metadata only

3. **Lifecycle**:
   - Auto-close after N turns (default 2)
   - Pinning prevents auto-close
   - Manual close via tools
   - System windows cannot be closed by agents

4. **Special Features**:
   - Query support: Ask sub-agent questions about content
   - Auto-refresh: Periodic content updates
   - Custom attributes: Extensible metadata

### Window LMML Representation

```lmml
<window windowId="123" 
        srcType="file" 
        src="agents:/config.yaml"
        contentType="text/yaml"
        lines="150" 
        chars="3000"
        topLineNumber="20"
        bottomLineNumber="40"
        autoCloseInTurns="2"
        title="Configuration File"
        refreshable>
  <queryResult query="What port is configured?">Port 8080 is configured</queryResult>
    # Lines 20-40 of the file content
</window>
```

#### Window Refresh Mechanism

Windows can have a `Refresh` function that:

- Updates content from the source
- Runs automatically if `AutoRefresh` is true
- Can be triggered manually via `Window_Action` tool
- Clamps line numbers after refresh to stay in bounds

#### Query System

Windows support queries where:

- A sub-agent (using a fast, cheap model like Haiku) answers questions about content
- Query and result are displayed above the content
- Useful for large files or complex content
- System windows allow queries even when other actions are restricted

## Integration Layer

### Matrix Integration

- **Authentication**: Username/password via LibMatrix
- **Room Management**: Auto-join invites from same homeserver
- **Message Handling**: Support for text, formatted messages, threads
- **Typing Indicators**: Show when agent is processing
- **History**: Rebuild context from past messages on startup

### LLM Integration

- **Provider**: OpenRouter API (multi-model support)
- **Models Supported**:
  - Any model that supports tool calling via OpenRouter
  - Claude 3.6 Sonnet or newer perform the best.

### Web Integration

- **Browser**: Headless browser via ScrapingFish API
- **Modes**: Markdown (preferred) or raw HTML
- **Search**: Google Custom Search API
- **YouTube**: Metadata and caption extraction

## Memory and Context Management

### Processing State Management

The agent uses several mechanisms to manage processing state:

- **Processing Semaphore**: Ensures only one processing cycle runs at a time
- **Wakeup Reasons**: Tracks why the agent woke (timer, event, default mode)
- **Turn Counter**: Increments each non-continuation processing cycle
- **Iteration Limit**: Maximum 10 tool call iterations per wake to prevent loops

### Wake Cycle Coordination

1. Agent wakes on timer (configurable interval) or events
2. Typing indicators sent to active rooms and thoughts room
3. Context built and sent to LLM
4. Tool calls processed iteratively
5. Windows auto-close counters decremented
6. Non-pinned windows un-maximized
7. Typing indicators cleared

### Conversation Context

Each Matrix room has a conversation context. Each room maintains:

- Message history (stored as LMML elements)
- Pending events queue (processed on wake)
- Thread tracking
- Agent-specific state
- Room name (cached from Matrix state)
- Subagents to digest the full room context with predefined queries to provide better awareness to the main agent

### Ephemeris (short-term memory)

- Stored as LMML entries
- Agent-specific memories
- Appears as special "ephemeris" room in chat interface
- Reset when the server restarts

#### Ephemeris Room Structure

The ephemeris room is a special room that:
- Has `roomId="ephemeris"`
- Contains only the agent as a member
- Shows agents thoughts and function calls
- Receives wakeup timestamps

```xml
<room systemId="matrix.org" roomId="ephemeris" roomName="" loggedInAs="@agent:matrix.org">
  <roomMember userId="@agent:matrix.org" you="yes" />
  
  <window windowId="ephemeris" srcType="chatHistory" src="ephemeris" 
          contentType="text/lmml" pinned system>
   <thouught timestamp="2024-12-20T10:00:00Z" >
     User @alice:matrix.org prefers technical documentation in bullet-point format
   </thought>
   <thought timestamp="2024-12-22T14:30:00Z" >
     Project roadmap document location: agents:/docs/project-roadmap-2024.md
   </thought>
   <thought timestamp="2024-12-24T09:15:00Z" >
     Completed security audit review task for @bob:matrix.org
   </thought>
  </window>
  
  <newEvents>
    <!-- Wakeup events added here but not shown to agent -->
    <timestamp value="2024-12-25T16:00:00Z" wakeReason="new event" turnId="156" />
  </newEvents>
  
  <roomFooter systemId="matrix.org" roomId="ephemeris" />
</room>
```

## Security Considerations

### Access Control

- File ownership validation
- Matrix room membership checks
- Share-level permissions
- Agent-specific file access

## Deployment Architecture

### Dependencies

- **.NET 8+**: Runtime environment
- **PostgreSQL**: Database (co-located with Synapse)
- **Matrix Homeserver**: Synapse recommended
- **External APIs**: OpenRouter, Google, ScrapingFish

### Configuration

Environment-specific settings via appsettings.json:

- Database connection strings
- API keys
- Agent configurations
- System administrator

## Conclusion

Clevernet represents a sophisticated approach to persistent AI agents, combining structured communication (LMML), flexible storage (virtual filesystem), and powerful integrations (Matrix, web, search). The window system provides a unique solution for context management, while the tool framework enables extensible capabilities.

---

This PRD provides the blueprint for creating compatible implementations while leaving room for platform-specific optimizations and enhancements.