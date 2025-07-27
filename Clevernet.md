# Clevernet Product Requirements Document

## Executive Summary

Clevernet is an advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem. The system uses LMML (Language Model Markup Language) as its primary data interchange format, providing a structured way for agents to process events, maintain state, and interact with users and external systems.

This document serves as both a specification for the current implementation and a blueprint for re-implementing Clevernet in other languages or platforms.

## System Overview

### Core Architecture

Clevernet consists of four main components:

1. **Agent Framework**: Autonomous AI agents with configurable personas, tools, and behaviors
2. **LMML Processing Engine**: XML-based markup language for structured agent communication
3. **Storage Layer**: PostgreSQL-based virtual filesystem and memory persistence
4. **Integration Layer**: Matrix protocol support, web browsing, and external service connections

### Key Features

- **Persistent Agents**: Agents maintain context and memory across sessions
- **Multi-Model Support**: Integration with various LLMs (Claude, GPT, Gemini, etc.) via OpenRouter
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
   - Execute tool calls iteratively (max 10 iterations)
   - Send responses back to Matrix

3. **Memory Management**:
   - Working memory: Current windows and active context
   - Short-term: Recent messages and open files
   - Long-term: PostgreSQL storage (files and ephemeris)

## LMML (Language Model Markup Language)

### Design Principles

- **Token Efficiency**: Uses camelCase attributes to minimize token usage
- **Self-Describing**: Elements contain all necessary context
- **Extensible**: New element types can be added without breaking compatibility
- **Human/AI Readable**: Optimized for both human comprehension and LLM processing

### Core Elements

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

<!-- Timestamp element (for wakeups) -->
<timestamp value="2024-12-25T12:00:00Z" wakeReason="timer" turnId="42" />

<!-- Window element for content display -->
<window windowId="123" srcType="file" src="agents:/config.yaml" 
        contentType="text/yaml" lines="50" chars="1024" 
        topLineNumber="1" bottomLineNumber="20">
  <content raw="yes">
    # Actual file content here
  </content>
</window>

<!-- Function call element -->
<functionCall id="fc123" function="SendMessage_abc123">
  <parameter name="roomId">!abc:matrix.org</parameter>
  <parameter name="content">Hello!</parameter>
</functionCall>

<!-- Function result element -->
<functionResult id="fc123">
  <result>Message sent successfully</result>
</functionResult>
```

### Special Attributes

- `raw`: Preserves exact content formatting (for code, logs) - boolean attribute
- `maximized`/`minimized`: Window display states - boolean attributes
- `pinned`: Prevents auto-close - boolean attribute
- `system`: System-generated windows - boolean attribute
- `sent`: Indicates a message was successfully sent - boolean attribute
- `willAutoCloseAfterTurn`: Window will close next turn - boolean attribute
- `autorefresh`/`refreshable`: Window refresh capabilities - boolean attributes
- Boolean attributes: Presence means true, absence means false (no `="true"` needed)

### LMML Parsing Rules

1. **Boolean Attributes**: Special value `"booleantrue"` converted to presence notation
2. **XML Escaping**: Applied to all non-raw content and attribute values
3. **Self-Closing Tags**: Supported for elements without content
4. **Nested Content**: Properly indented for readability
5. **Required Attributes**: Some elements enforce required attributes (e.g., timestamp)

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

#### Agent Flags
- `PreventParallelFunctionCalls`: Forces sequential tool execution
- `PreventFunctionCallsWithoutThoughts`: Requires thinking before tool use

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
   - Load ephemeris (persistent memory)

2. **Wake Cycle**:
   - Triggered by timer or pending events
   - Process up to 10 events per wake
   - Maintain separate context per room
   - Update ephemeris with important information
   - Send typing indicators during processing
   - Clear typing indicators when done

3. **Tool Execution**:
   - Agents have access to configured tools
   - Tools executed via function calling
   - Results integrated into conversation
   - Multiple iterations allowed per wake (max 10)
   - Each tool has instance-specific name

### Persona Window

The persona window is a critical component that defines the agent's identity and behavior. It appears in the system prompt before the chat interface:

```xml
<!-- In system prompt, after base prompt -->
<window windowId="persona" srcType="file" src="agents:/personas/helper.md" 
        contentType="text/markdown" lines="50" chars="2000" 
        pinned system maximized>
  <content raw>
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
  </content>
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
               agentUnderlyingModel="anthropic/claude-3-opus" 
               agentRunningOnSystem="matrix.org">
  
  <agentParameters wakeUpTimerSeconds="3600" />
  
  <chatSystem systemId="matrix.org" loggedInAs="@agent:matrix.org">
    <systemAdmin>@admin:matrix.org</systemAdmin>
    
    <!-- Each room the agent is in -->
    <room systemId="matrix.org" 
          roomId="!abc123:matrix.org" 
          roomName="General Chat"
          loggedInAs="@agent:matrix.org">
      <roomMember userId="@admin:matrix.org" admin="yes" />
      <roomMember userId="@agent:matrix.org" you="yes" />
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
        <content raw>
          <!-- Past conversation history -->
          <message systemId="matrix.org" roomId="!abc123:matrix.org" 
                   timestamp="2024-12-25T15:00:00Z" 
                   sender="@user:matrix.org" 
                   messageType="m.text">Hello agent!</message>
          
          <message systemId="matrix.org" roomId="!abc123:matrix.org" 
                   timestamp="2024-12-25T15:01:00Z" 
                   sender="@agent:matrix.org" 
                   messageType="m.text">Hello! How can I help you today?</message>
        </content>
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
              pinned="yes"
              system="yes">
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
            pinned="yes"
            maximized="yes"
            system="yes">
      <content raw="yes">
        TODO:
        - Respond to user question about Clevernet
        - Update documentation
        - Check for system updates
      </content>
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
    <content raw="yes">
      # Clevernet Product Requirements Document
      
      ## Executive Summary
      ...
    </content>
  </window>
</chatInterface>
```

#### Thought Room

Agents have their thoughts and function calls sent to a special "thoughts room" (`!bRQNfEcHgLvdeTaUHT:matrix.org`). This room:
- Is invisible to the agent (not included in their context)
- Receives all agent thinking and function calls
- Allows administrators to monitor agent reasoning
- Helps with debugging and understanding agent behavior
- Shows typing indicators when agent is processing
- Receives both text thoughts and code-formatted function calls

The thoughts room is hardcoded in `MatrixHelper.ThoughtsRoomId` and agents are prevented from seeing it by checking room IDs in the event handlers.

Example thought room output:
```
Agent (@agent:matrix.org): I need to understand what the user is asking about Clevernet. Let me search for relevant documentation.

<functionCall id="fc1" function="FS_OpenFile_abc123">
  <parameter name="path">system:/guides/clevernet.md</parameter>
  <parameter name="line">1</parameter>
</functionCall>

<functionResult id="fc1">
  <result>Window opened with ID 456</result>
</functionResult>

Agent (@agent:matrix.org): Now I can see the Clevernet documentation. Let me formulate a response to help the user understand the system.

<functionCall id="fc2" function="SendMessage_abc123">
  <parameter name="roomId">!abc123:matrix.org</parameter>
  <parameter name="content">Clevernet is an advanced AI agent framework that enables persistent, context-aware AI agents to operate within the Matrix ecosystem...</parameter>
</functionCall>
```

#### Multi-Room Coordination

Agents can:
- Participate in multiple rooms simultaneously
- Maintain separate conversation context per room
- Share information between rooms via their memory (ephemeris)
- Process events from all rooms in a single wake cycle
- Send typing indicators to active rooms during processing

### Tool System Architecture

#### Instance-Specific Tool Names
Each agent instance generates unique tool names by appending an instance ID (e.g., `SendMessage_abc123`). This prevents tool name collisions when multiple agents are running and allows the system to route function calls to the correct agent instance.

#### Tool Execution Flow
1. Agent receives tool definitions with instance-specific names
2. LLM calls tools using these unique names
3. System routes calls back to the correct agent instance
4. Results are returned as LMML elements
5. Multiple tool calls can be executed in a single iteration

#### Tool Result Format
Tool results are typically returned as LMML strings, allowing structured data to flow back into the conversation context. For example:
```xml
<message systemId="matrix.org" roomId="!abc:matrix.org" 
         timestamp="2024-12-25T16:00:00Z" 
         sender="@agent:matrix.org" 
         messageType="m.text" sent>Message content</message>
```

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
- Managing conversation context

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
  <content raw>
    # Lines 20-40 of the file content
  </content>
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
- A sub-agent (using a fast model like Gemini Flash) answers questions about content
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
  - Claude (Opus, Sonnet, Haiku)
  - GPT-4/GPT-3.5
  - Gemini Pro
  - Open models (Llama, Mixtral, etc.)
- **Features**:
  - Function calling
  - Streaming responses
  - Token management
  - Cost tracking

### Web Integration

- **Browser**: Headless browser via ScrapingFish API
- **Modes**: Markdown (preferred) or raw HTML
- **Search**: Google Custom Search API
- **YouTube**: Metadata and caption extraction

## Memory and Context Management

#### Processing State Management
The agent uses several mechanisms to manage processing state:
- **Processing Semaphore**: Ensures only one processing cycle runs at a time
- **Wakeup Reasons**: Tracks why the agent woke (timer, event, default mode)
- **Turn Counter**: Increments each non-continuation processing cycle
- **Iteration Limit**: Maximum 10 tool call iterations per wake to prevent loops

#### Wake Cycle Coordination
1. Agent wakes on timer (configurable interval) or events
2. Typing indicators sent to active rooms and thoughts room
3. Context built and sent to LLM
4. Tool calls processed iteratively
5. Windows auto-close counters decremented
6. Non-pinned windows un-maximized
7. Typing indicators cleared

### Conversation Context

Each room maintains:
- Message history (stored as LMML elements)
- Open windows
- Pending events queue (processed on wake)
- Thread tracking
- Agent-specific state
- Room name (cached from Matrix state)

### RAG System

- **Embedding Model**: Local ONNX model (all-MiniLM-L6-v2)
- **Chunking**: 512 chars with 50 char overlap
- **Search**: Semantic similarity across all messages
- **Filtering**: By room, user, time range

### Ephemeris (Long-term Memory)

- Stored as LMML entries in PostgreSQL
- Agent-specific memories
- Searchable and retrievable
- Used for cross-session context
- Appears as special "ephemeris" room in chat interface

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

#### Agent Memory Storage

Agents can store memories using the ephemeris tools:
- Memories are automatically persisted to database
- Survive agent restarts
- Can be searched semantically
- Help maintain context across conversations

## Security Considerations

### Access Control

- File ownership validation
- Matrix room membership checks
- Share-level permissions
- Agent-specific file access

### API Security

- Secure credential storage
- Token rotation support
- Rate limiting
- Error masking in responses

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

### Scaling Considerations

- Single instance per agent
- Horizontal scaling via multiple agents
- Database connection pooling
- Async/await throughout

## Implementation Guidelines

### Critical UI/UX Details

1. **Message Flow**:
   - All context sent as single message to LLM
   - System prompt includes persona and guides
   - Each room rendered with full context
   - Windows provide additional context
   - Agent thinks in response, then calls functions
   - Thoughts visible only in thought room

2. **Window Management in UI**:
   - Windows appear inline in the chat interface
   - Each window has unique ID and lifecycle
   - Windows can be scrolled, maximized, minimized
   - Auto-close after N turns unless pinned
   - System windows cannot be closed by agent

3. **Room Rendering**:
   - Each room shows member list with attributes
   - Chat history in scrollable window (50 lines default)
   - New events clearly separated
   - RAG results provide relevant context
   - Direct message rooms marked with `directMessage="yes"`

4. **Special Rooms**:
   - Ephemeris: Agent's persistent memory
   - Thoughts room: Hidden from agent, shows reasoning
   - System context: Internal events and errors

### For Re-implementation

1. **Start with Core**:
   - LMML parser/serializer
   - Basic Matrix client  
   - Simple file storage
   - Single LLM provider
   - **Chat Interface Builder**: Must construct complete UI as shown above

2. **Add Tools Incrementally**:
   - SendMessage first (critical for agent communication)
   - File operations
   - Window management
   - Web integration

3. **Memory Later**:
   - Start with in-memory context
   - Add persistence
   - Implement RAG last

4. **UI Construction Flow** (following Agent.cs):
   ```
   ProcessWithClaude()
   ├── BuildSystemPrompt()
   │   ├── Agent system prompt
   │   ├── Persona window (from file)
   │   └── Agent guide (system:/guides/clevernet.md)
   └── RenderLmmlString()
       └── RenderLmml()
           ├── Create <chatInterface> root
           ├── Add agent parameters
           ├── Create <chatSystem> with all rooms
           │   ├── For each room:
           │   │   └── ConversationContext.Render()
           │   │       ├── Room metadata and members
           │   │       ├── Chat history window
           │   │       ├── New events section
           │   │       └── RAG results footer
           │   └── System context room
           ├── Add system reminder
           └── Add all open windows
   ```

### Critical Implementation Details

1. **LMML Parsing**:
   - Must handle nested content
   - Preserve raw content exactly  
   - Support custom attributes
   - Validate element structure
   - Special handling for `raw="yes"` attribute

2. **Window Management**:
   - Track line numbers accurately
   - Handle content updates
   - Manage lifecycle properly
   - Enforce size limits
   - Viewport calculation: `topLineNumber` to `bottomLineNumber`
   - Content truncation with warning message

3. **Tool Execution**:
   - Validate all parameters
   - Handle errors gracefully
   - Return consistent formats
   - Support async operations
   - Tools receive instance-specific names (e.g., `SendMessage_abc123`)

4. **Context Building**:
   - Order matters (system prompt, then chat interface)
   - Each room rendered independently
   - Include relevant windows inline
   - Manage token limits (~50K chars default)
   - Preserve conversation flow
   - New events clearly marked in `<newEvents>` sections

5. **Conversation Context Per Room**:
   - Separate `ConversationContext` instance per room
   - Tracks pending events queue
   - Maintains conversation history
   - Handles threading and replies
   - Provides RAG search on room history
   - Window for chat history (pinned, system, auto-refresh)

6. **Agent Processing Loop**:
   ```csharp
   // Simplified flow from Agent.ProcessWithClaude()
   while (hasMoreWork && iterations < maxIterations) {
       // Build complete UI
       var systemPrompt = await BuildSystemPrompt();
       var chatInterface = await RenderLmmlString();
       
       // Send to LLM with tools
       var response = await LLM.Complete(systemPrompt, chatInterface, tools);
       
       // Process tool calls
       hasMoreWork = response.HasToolCalls();
       iterations++;
   }
   ```
   
## Future Enhancements

### Planned Features

1. **Multi-Agent Coordination**: 
   - Shared memory spaces
   - Agent-to-agent messaging
   - Task delegation

2. **Advanced Memory**:
   - Episodic memory
   - Fact extraction
   - Knowledge graphs

3. **Enhanced Tools**:
   - Code execution
   - Database queries
   - API integrations

4. **Improved Abstractions**:
   - Pluggable LLM providers
   - Swappable storage backends
   - Protocol adapters

### Experimental Features

1. **Submodels**: Different models for different tasks
2. **Agent Guides**: Hot-swappable behavior modules
3. **Default Mode Network**: Background processing
4. **Adaptive Context**: Dynamic window sizing

## Complete Example Chat Session

### Scenario: User asks agent to find and summarize a document

#### Turn 1: Initial Request

**What the agent receives:**
```xml
<chatInterface currentDatetime="2024-12-25 16:00:00 -0800" agentUnderlyingModel="anthropic/claude-3-opus">
  <chatSystem systemId="matrix.org" loggedInAs="@helper:matrix.org">
    <room roomId="!workroom:matrix.org" roomName="Work Room">
      <roomMember userId="@user:matrix.org" />
      <roomMember userId="@helper:matrix.org" you="yes" />
      
      <window windowId="room_work" srcType="chatHistory" lines="10" pinned system>
        <content raw>
          <!-- Empty history - new conversation -->
        </content>
      </window>
      
      <newEvents>
        <message systemId="matrix.org" roomId="!workroom:matrix.org" 
                 timestamp="2024-12-25T16:00:00Z" 
                 sender="@user:matrix.org" 
                 messageType="m.text">Can you find the project roadmap document and give me a summary?</message>
      </newEvents>
    </room>
  </chatSystem>
  <systemReminder>You must use functions to interact...</systemReminder>
</chatInterface>
```

**Thought room sees:**
```
Helper (@helper:matrix.org): The user is asking me to find a project roadmap document and summarize it. I'll need to search for this document first.

<functionCall id="fc1" function="FS_Search_helper123">
  <parameter name="path">agents:/</parameter>
  <parameter name="query">roadmap</parameter>
  <parameter name="searchMode">filename</parameter>
</functionCall>
```

#### Turn 1: Iteration 2 (After Search)

**What the agent receives (updated):**
```xml
<chatInterface ...>
  <!-- Previous content plus: -->
  <window windowId="search_123" srcType="search" src="agents:/" contentType="text/lmml">
    <content raw>
      <searchResult path="agents:/docs/project-roadmap-2024.md" score="1.0">
        <match line="1">project-roadmap-2024.md</match>
      </searchResult>
      <searchResult path="agents:/archive/old-roadmap.md" score="0.8">
        <match line="1">old-roadmap.md</match>
      </searchResult>
    </content>
  </window>
</chatInterface>
```

**Thought room sees:**
```
Helper (@helper:matrix.org): Found the roadmap! The most recent one is project-roadmap-2024.md. Let me open it to read the contents.

<functionCall id="fc2" function="FS_OpenFile_helper123">
  <parameter name="path">agents:/docs/project-roadmap-2024.md</parameter>
</functionCall>
```

#### Turn 1: Iteration 3 (After Opening File)

**What the agent receives (updated):**
```xml
<chatInterface ...>
  <!-- Previous content plus: -->
  <window windowId="file_456" srcType="file" src="agents:/docs/project-roadmap-2024.md" 
          contentType="text/markdown" lines="150" chars="5000" 
          topLineNumber="1" bottomLineNumber="20">
    <content raw>
# Project Roadmap 2024

## Q1 Goals
- Launch beta version
- Onboard 10 pilot customers
- Complete security audit

## Q2 Goals  
- Public launch
- API v2 release
- Mobile app development
[... more content ...]
    </content>
  </window>
</chatInterface>
```

**Thought room sees:**
```
Helper (@helper:matrix.org): Perfect! I can see the roadmap. Let me read through it and prepare a summary for the user. The document outlines quarterly goals for 2024.

<functionCall id="fc3" function="SendMessage_helper123">
  <parameter name="roomId">!workroom:matrix.org</parameter>
  <parameter name="content">I found the project roadmap document! Here's a summary:

**Project Roadmap 2024**

**Q1 Goals:**
- Launch beta version
- Onboard 10 pilot customers  
- Complete security audit

**Q2 Goals:**
- Public launch
- API v2 release
- Mobile app development

**Q3 Goals:**
- Scale to 1000 customers
- International expansion
- Enterprise features

**Q4 Goals:**
- AI integration
- Advanced analytics
- Partner ecosystem

The roadmap focuses on moving from beta to public launch in the first half of the year, then scaling and adding advanced features in the second half. Would you like me to elaborate on any specific quarter or goal?</parameter>
</functionCall>
```

**User sees in Matrix room:**
```
Helper: I found the project roadmap document! Here's a summary:

**Project Roadmap 2024**

**Q1 Goals:**
- Launch beta version
- Onboard 10 pilot customers  
- Complete security audit

[... rest of summary ...]
```

### Key Points Illustrated

1. **Single Message Context**: Everything is provided in one LMML message
2. **Iterative Processing**: Agent can call multiple tools per wake
3. **Window Management**: Search and file windows appear inline
4. **Thought Separation**: Agent reasoning only visible in thought room
5. **Clean User Experience**: Users only see the final response

## Conclusion

Clevernet represents a sophisticated approach to persistent AI agents, combining structured communication (LMML), flexible storage (virtual filesystem), and powerful integrations (Matrix, web, search). The window system provides a unique solution for context management, while the tool framework enables extensible capabilities.

The current implementation in C#/.NET is production-ready but tightly coupled to specific technologies. Future implementations should focus on maintaining the core concepts (LMML, windows, tools) while improving modularity through proper abstractions.

Key success factors for any implementation:
- Robust LMML processing
- Efficient window management  
- Reliable Matrix integration
- Flexible tool system
- Persistent memory management

This PRD provides the blueprint for creating compatible implementations while leaving room for platform-specific optimizations and enhancements.