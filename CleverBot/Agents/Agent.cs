using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Common;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using LibMatrix.Helpers;
using LibMatrix.Responses;
using LibMatrix.EventTypes.Spec;
using LibMatrix.EventTypes;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using LibMatrix.RoomTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using CleverBot.Abstractions;
using NeoSmart.AsyncLock;
using CleverBot.Helpers;
using CleverBot.Services;
using Clevernet.Data;
using Markdig;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using SmartComponents.LocalEmbeddings;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace CleverBot.Agents;

/// <summary>
/// Represents an autonomous AI agent that operates within the Matrix ecosystem.
/// Each agent maintains its own identity, conversation contexts, and tool capabilities.
/// Agents process events asynchronously, maintain persistent memory, and can interact
/// with multiple Matrix rooms simultaneously.
/// </summary>
/// <remarks>
/// The Agent class implements the core processing loop that:
/// - Wakes on timer or events
/// - Builds complete context as LMML
/// - Sends to LLM with available tools
/// - Executes tool calls iteratively
/// - Manages windows and conversation state
/// </remarks>
public class Agent : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly HomeserverProviderService _homeserverProvider;
    private readonly IChatCompletionService _chatCompletionService;
    
    /// <summary>
    /// The authenticated Matrix homeserver connection for this agent
    /// </summary>
    private AuthenticatedHomeserverGeneric? _homeserver;
    
    /// <summary>
    /// Helper for managing Matrix sync operations and event streaming
    /// </summary>
    private SyncHelper? _syncHelper;
    
    /// <summary>
    /// The domain of the homeserver this agent is connected to
    /// </summary>
    private string _homeServerDomain = string.Empty;
    
    /// <summary>
    /// List of conversation contexts, one per Matrix room the agent is in
    /// </summary>
    private readonly List<ConversationContext> _contexts = new();
    
    /// <summary>
    /// Special context for system events and ephemeris (persistent memory)
    /// </summary>
    private readonly ConversationContext _systemContext;
    
    /// <summary>
    /// Configuration parameters for this agent instance
    /// </summary>
    public readonly AgentParameters AgentParameters;
    
    /// <summary>
    /// Window containing the agent's persona definition (always pinned and maximized)
    /// </summary>
    private readonly Window _personaWindow;
    
    /// <summary>
    /// Optional scratchpad window for agent notes (if configured)
    /// </summary>
    private Window? _scratchpadWindow;
    
    /// <summary>
    /// Static counter for generating unique agent instance IDs
    /// </summary>
    private static int _nextAgentInstanceId = Random.Shared.Next(10000);
    
    /// <summary>
    /// Unique identifier for this agent instance (used for tool naming)
    /// </summary>
    private int _agentInstanceId = (_nextAgentInstanceId += 100) + Random.Shared.Next(100);
    
    /// <summary>
    /// Reason for the next wakeup (set by events or timers)
    /// </summary>
    private string? _nextWakeupReason = null;
    
    /// <summary>
    /// Global collection of all active agent instances
    /// </summary>
    public static ConcurrentBag<Agent> Agents = new();

    /// <summary>
    /// Room ID for the agent's private configuration room
    /// </summary>
    private string? _agentRoomId;

    /// <summary>
    /// Matrix user ID of the system administrator
    /// </summary>
    /// <remarks>TODO: make this a list instead of a single value</remarks>
    private readonly string _systemAdminUserId;
    
    /// <summary>
    /// Lock for thread-safe operations
    /// </summary>
    private readonly AsyncLock _lock = new();
    
    /// <summary>
    /// Virtual filesystem service for agent file operations
    /// </summary>
    private readonly IFileSystem _fileSystem;
    
    /// <summary>
    /// List of currently open windows (files, web pages, search results)
    /// </summary>
    private readonly List<Window> _openWindows = new();
    
    /// <summary>
    /// Factory for creating database contexts
    /// </summary>
    private readonly IDbContextFactory<CleverDbContext> _contextFactory;
    
    /// <summary>
    /// Service for fetching and rendering web pages as text
    /// </summary>
    private readonly TextWebBrowser _textWebBrowser;
    
    private readonly ILoggerFactory _loggerFactory;
    
    /// <summary>
    /// Service for Google search and YouTube integration
    /// </summary>
    private readonly GoogleService _googleService;
    
    /// <summary>
    /// Repository for semantic search across conversation history
    /// </summary>
    private readonly EmbeddingRepository<(LmmlElement Element, ConversationContext Context)> _embeddingRepository;
    
    /// <summary>
    /// Separate LLM client for default mode network (background monitoring)
    /// </summary>
    private readonly IChatClient _defaultModeClient;
    
    /// <summary>
    /// Current conversation turn number (incremented each wake cycle)
    /// </summary>
    private int _turn = 0;
    
    /// <summary>
    /// System identifier for where this agent is running
    /// </summary>
    private readonly string _runningOn;
    
    /// <summary>
    /// Flag indicating if the agent is currently processing
    /// </summary>
    private volatile bool _isProcessing;
    
    /// <summary>
    /// Semaphore to ensure only one processing cycle runs at a time
    /// </summary>
    private readonly SemaphoreSlim _processingSemaphore = new(1, 1);
    
    /// <summary>
    /// Completion source for graceful shutdown
    /// </summary>
    private readonly TaskCompletionSource<bool> _completionSource = new();

    /// <summary>
    /// Initializes a new instance of the Agent class
    /// </summary>
    /// <param name="agentParameters">Configuration parameters for this agent</param>
    /// <param name="loggerFactory">Factory for creating loggers</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="homeserverProvider">Service for connecting to Matrix homeservers</param>
    /// <param name="chatCompletionService">LLM service for agent reasoning</param>
    /// <param name="fileSystem">Virtual filesystem service</param>
    /// <param name="textWebBrowser">Web browsing service</param>
    /// <param name="contextFactory">Database context factory</param>
    /// <param name="googleService">Google/YouTube integration service</param>
    public Agent(AgentParameters agentParameters,
        ILoggerFactory loggerFactory, 
        IConfiguration configuration, 
        HomeserverProviderService homeserverProvider,
        IChatCompletionService chatCompletionService, 
        IFileSystem fileSystem, 
        TextWebBrowser textWebBrowser,
        IDbContextFactory<CleverDbContext> contextFactory, 
        GoogleService googleService)
    {
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _homeserverProvider = homeserverProvider;
        _chatCompletionService = chatCompletionService;
        _fileSystem = fileSystem;
        _contextFactory = contextFactory;
        _googleService = googleService;
        AgentParameters = agentParameters;
        _systemAdminUserId = configuration["SystemAdmin"] ?? throw new InvalidOperationException("SystemAdmin not configured");
        _logger = loggerFactory.CreateLogger($"{typeof(Agent).Namespace}.{nameof(Agent)}.{AgentParameters.UserId}");
        _textWebBrowser = textWebBrowser;
        _embeddingRepository = new(_loggerFactory.CreateLogger<EmbeddingRepository<(LmmlElement Element, ConversationContext Context)>>(), new LocalEmbedder());
        _systemContext = GetOrCreateContext("system", "ephemeris");
        _chatCompletionService.SetApp($"Clevernet {AgentParameters.Name}", $"https://github.com/acehoss/?u={HttpUtility.UrlEncode(AgentParameters.Name)}");
        _defaultModeClient = new OllamaChatClient(new Uri("http://10.0.4.137:11434/"), "gemma2:27b-instruct-q5_1");
        _personaWindow = new Window()
        {
            Content = AgentParameters.Persona ?? throw new InvalidOperationException($"Persona not configured for {AgentParameters.UserId}"),
            IsPinned = true,
            IsSystem = true,
            MaxLines = Int32.MaxValue,
            Title = $"{AgentParameters.UserId}'s Persona",
            ContentSource = $"system:/users/{AgentParameters.UserId}/personas/main.md",
            ContentSourceType = "file",
            ContentType = "text/markdown",

        };
        _personaWindow.Maximize();
        _runningOn = configuration.GetValue<string>("runningOn") ?? "unknown";
    }

    private ConversationContext GetOrCreateContext(string systemId, string roomId)
    {
        if(roomId == MatrixHelper.ThoughtsRoomId)
            throw new InvalidOperationException("Thoughts room cannot be used as a context");
        
        lock (_contexts)
        {
            Debug.Assert(systemId != "" && roomId != "");
            var context = _contexts.FirstOrDefault(c => c.SystemId == systemId && c.RoomId == roomId);
            if (context is null)
            {
                context = new ConversationContext(_systemAdminUserId, systemId, roomId, AgentParameters,
                    _loggerFactory.CreateLogger<ConversationContext>(), _chatCompletionService, _contextFactory, _embeddingRepository, new PromptCallbacks() { SystemPrompt = async () => string.Join("\n", await BuildSystemPrompt())});
                _contexts.Add(context);
                context.OnThoughtsAndActions = OnThoughtsAndActions;
            }

            return context;
        }
    }

    /// <summary>
    /// Callback method that receives agent thoughts and tool calls for logging to the thoughts room
    /// </summary>
    /// <param name="content">The thought or action content</param>
    /// <param name="code">Whether the content should be formatted as code</param>
    private void OnThoughtsAndActions(string content, bool code)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;
        
        if (code)
            content = System.Net.WebUtility.HtmlEncode(content);
        SendMessageAsync(MatrixHelper.ThoughtsRoomId, content, code:code)
            .GulpException(_logger, "Failed to send thought")
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Sends a typing indicator to a Matrix room
    /// </summary>
    /// <param name="roomId">The room to send the indicator to</param>
    /// <param name="typing">Whether to show or hide the typing indicator</param>
    /// <param name="timeout">How long the indicator should remain (milliseconds)</param>
    private async Task SendTypingIndicator(string roomId, bool typing, int timeout = 300000) {
        _logger.LogDebug($"{(typing ? "begin" : "end")} typing indicator for {roomId}");
        var room = _homeserver.GetRoom(roomId);
        if (room is null)
            throw new InvalidOperationException($"Room {roomId} not found");
        
        await (await _homeserver.ClientHttpClient.PutAsJsonAsync($"/_matrix/client/v3/rooms/{roomId}/typing/{_homeserver?.WhoAmI?.UserId}", new { typing, timeout }))
            .Content.ReadFromJsonAsync<EventIdResponse>();
    }

    /// <summary>
    /// Builds the system prompt for the LLM including agent persona and guides
    /// </summary>
    /// <returns>List of prompt components to be joined with newlines</returns>
    private async Task<List<string>> BuildSystemPrompt() {
        var prompt = new List<string>();
        prompt.Add(AgentParameters.SystemPrompt);
        var personaWindowLmml = await _personaWindow.Render(AgentParameters.ApproxContextCharsMax);
        prompt.Add(personaWindowLmml.ToString());
        var agentGuideFile = await _fileSystem.ReadFileAsync("system:/guides/clevernet.md") ?? throw new InvalidOperationException("Clevernet guide not found");
        prompt.Add(string.Join("\n", new[] {
            new LmmlElement("agentGuide") 
            { 
                Attributes = new() { ["title"] = "Clevernet Matrix System on matrix.org" },
                Content = new LmmlStringContent(agentGuideFile.OpenFileContent)
            },
        }.Select(x => x.ToString())));
        return prompt;
    }

    /// <summary>
    /// Renders the complete chat interface as an LMML string
    /// </summary>
    /// <param name="continuation">Whether this is a continuation of the current turn</param>
    /// <param name="wakeReason">The reason the agent woke up</param>
    /// <param name="preview">Whether this is a preview (doesn't modify state)</param>
    /// <returns>The complete LMML string representation of the chat interface</returns>
    private async Task<string> RenderLmmlString(bool continuation, string wakeReason, bool preview = false)
    {
        return string.Join("\n", (await RenderLmml(continuation, wakeReason, preview)).Select(x => x.ToString()));
    }
    
    /// <summary>
    /// Renders the complete chat interface as LMML elements
    /// </summary>
    /// <param name="continuation">Whether this is a continuation of the current turn</param>
    /// <param name="wakeReason">The reason the agent woke up</param>
    /// <param name="preview">Whether this is a preview (doesn't modify state)</param>
    /// <returns>The chat interface as LMML elements</returns>
    private async Task<IEnumerable<LmmlElement>> RenderLmml(bool continuation, string wakeReason, bool preview = false)
    {
        if (!continuation)
            _turn++;
        var joinedRooms = ((IEnumerable<GenericRoom>)await _homeserver.GetJoinedRooms()
            .FilterRoomsAsync()
            .GulpException(_logger, "Failed to get joined rooms")) ?? Array.Empty<GenericRoom>();
        var joinedRoomsWithMembers = await Task.WhenAll(joinedRooms.Select(async r => new
        {
            Room = r, 
            ConversationContext = GetOrCreateContext(_homeServerDomain, r.RoomId),
            RoomName = (await r.GetStateAsync<RoomNameEventContent>("m.room.name").GulpException(_logger, "failed to get room name"))?.Name,
            Members = (((IEnumerable<StateEventResponse>?)await r.GetMembersListAsync().GulpException(_logger, "Failed to get room members")) ?? Array.Empty<StateEventResponse>())
                .Select(m => m.Sender)
                .Select(m => m!)
                .ToArray()
        }));

        var roomsLmml = await Task.WhenAll(joinedRoomsWithMembers.Select(jr =>
            jr.ConversationContext.Render(jr.Members, _turn, wakeReason, continuation, preview)));
        roomsLmml = roomsLmml.Concat(new[] { await _systemContext.Render([AgentParameters.UserId], _turn, wakeReason, continuation, preview) }).ToArray();

        var chatChildren = new List<LmmlElement>
        {
            new("systemAdmin") { Content = new LmmlStringContent(_systemAdminUserId) },
        };
        chatChildren.AddRange(roomsLmml);
        if(_scratchpadWindow != null)
            chatChildren.Add(await _scratchpadWindow.Render(AgentParameters.ApproxContextCharsMax));
            
        var currentInfo = new LmmlElement("chatInterface")
        {
            Attributes = new()
            {
                ["currentDatetime"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                ["agentUnderlyingModel"] = AgentParameters.Model,
                ["agentRunningOnSystem"] = _runningOn,
            },
            Content = new LmmlChildContent
            {
                Children = new List<LmmlElement>
                {
                    new ("agentParameters") { Attributes = new() { [AgentParameters.GetJsonPropertyName(x => x.WakeUpTimerSeconds)] = AgentParameters.WakeUpTimerSeconds.ToString(), }},
                    new ("chatSystem")
                    {
                        Attributes = new()
                        {
                            ["systemId"] = _homeServerDomain,
                            ["loggedInAs"] = AgentParameters.UserId,
                        },
                        Content = new LmmlChildContent
                        {
                            Children = chatChildren
                        }
                    },
                    new ("systemReminder") { Content = new LmmlStringContent("You must use functions to interact with humans or other agents. Responses outside of function calls are only visible to you. Use this space for thinking through your actions.") },
                }
            }
        };

        // Add open files directly to currentInfo
        var openFiles = await GetOpenWindowsLmml();
        ((LmmlChildContent)currentInfo.Content).Children.AddRange(openFiles);
        
        var elements = new List<LmmlElement> { currentInfo }; 
        return elements;
    }

    /// <summary>
    /// Decrements auto-close counters for windows and un-maximizes non-pinned windows
    /// </summary>
    public void DecrementWindowTurnsAndUnMaximize()
    {
        var expiredWindows = _openWindows.Where(f => f.IsPinned == false && --f.AutoCloseInTurns <= 0).ToList();
        foreach (var file in expiredWindows)
        {
            CloseWindow(file);
        }

        foreach (var file in _openWindows.Where(f => !f.IsSystem && !f.IsPinned))
        {
            file.IsMaximized = false;
        }
    }

    /// <summary>
    /// Gets LMML representations of all currently open windows
    /// </summary>
    /// <returns>Collection of window elements rendered as LMML</returns>
    public async Task<IEnumerable<LmmlElement>> GetOpenWindowsLmml()
    {
        return await Task.WhenAll(_openWindows.Select(async f => await f.Render(AgentParameters.ApproxContextCharsMax)));
    }

    /// <summary>
    /// Executes an action while ensuring thread-safe processing flag management
    /// </summary>
    /// <typeparam name="T">Return type of the action</typeparam>
    /// <param name="action">The action to execute</param>
    /// <returns>The result of the action</returns>
    private async Task<T> WithProcessingFlag<T>(Func<Task<T>> action)
    {
        try
        {
            await _processingSemaphore.WaitAsync();
            _isProcessing = true;
            return await action();
        }
        finally
        {
            _isProcessing = false;
            _processingSemaphore.Release();
        }
    }

    /// <summary>
    /// Main processing method that sends the current context to the LLM and handles responses
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Builds the complete system prompt and chat interface
    /// 2. Sends to the LLM with available tools
    /// 3. Processes tool calls iteratively (max 10 iterations)
    /// 4. Updates conversation state and windows
    /// </remarks>
    private async Task ProcessWithClaude()
    {
        await WithProcessingFlag(async () =>
        {
            string wakeReason = "";
            try
            {
                await SendTypingIndicator(MatrixHelper.ThoughtsRoomId, true, 300000)
                    .GulpException(_logger, "Failed to send typing indicator");

                wakeReason = _nextWakeupReason ?? "unscheduled wakeup";
                _nextWakeupReason = null;

                _logger.LogInformation("Processing message with Claude");

                var roomIds = _contexts
                    .SelectMany(c => c.RecentlyActiveRoomIds())
                    .Distinct()
                    .Where(r => r != _systemContext.RoomId)
                    .ToList();

                foreach (var roomId in roomIds)
                {
                    await SendTypingIndicator(roomId, true, 300000)
                        .GulpException(_logger, "Failed to send typing indicator");
                }

                // Keep track of iterations to prevent infinite loops
                int iterations = 0;
                const int maxIterations = 10;
                bool hasMoreWork = true;
                var spPartsTask = BuildSystemPrompt();
                var interfaceTask = RenderLmmlString(false, wakeReason);
                await Task.WhenAll(spPartsTask, interfaceTask);
                var systemPrompt = new Message(Role.System, string.Join("\n", await spPartsTask));
                var currentInfo = new Message(Role.User, await interfaceTask);
                Message? postPrompt = null;
                var messages = new List<Message> { systemPrompt, currentInfo };
                if (AgentParameters.PostPrompt != null)
                {
                    postPrompt = new Message(Role.System, AgentParameters.PostPrompt);
                    messages.Add(postPrompt);
                }

                while (hasMoreWork && iterations < maxIterations)
                {
                    iterations++;
                    _logger.LogInformation("Processing iteration {Turn}.{Iteration}", _turn + 1, iterations);
                    await SendTypingIndicator(MatrixHelper.ThoughtsRoomId, true, 300000)
                        .GulpException(_logger, "Failed to send typing indicator");
                    var tools = GetTools();
                    var result = await _chatCompletionService.GetCompletionAsync(messages,
                        AgentParameters.Model, tools, temperature: (decimal)AgentParameters.Temperature,
                        preventParallelFunctionCalls:
                        (AgentParameters.AgentFlags & AgentFlags.PreventParallelFunctionCalls) ==
                        AgentFlags.PreventParallelFunctionCalls,
                        preventFunctionCallsWithoutThinking: (AgentParameters.AgentFlags &
                                                              AgentFlags.PreventFunctionCallsWithoutThoughts) ==
                                                             AgentFlags.PreventFunctionCallsWithoutThoughts,
                        onMessagesReceived:
                        async (o) =>
                        {
                            await SendTypingIndicator(MatrixHelper.ThoughtsRoomId, true, 300000)
                                .GulpException(_logger, "Failed to send typing indicator");
                            var newCurrentInfo = new Message(Role.User, await RenderLmmlString(true, wakeReason));
                            o.OldMessages.Clear();
                            o.OldMessages.Add(systemPrompt);
                            o.OldMessages.Add(newCurrentInfo);
                            if (postPrompt != null)
                                o.OldMessages.Add(postPrompt);
                            currentInfo = newCurrentInfo;
                        });

                    _logger.LogInformation("Claude response: {Response}",
                        string.Join("\n", result.Select(r => r.ToString())));

                    _systemContext.AddModelResponses(result);
                    messages.AddRange(result);

                    // Assume we're done unless we find function calls
                    hasMoreWork = result.Any(r => r.ToolCalls?.Any() == true);
                }

                if (iterations >= maxIterations)
                {
                    _logger.LogWarning("Hit maximum iterations ({MaxIterations}) while processing message",
                        maxIterations);
                    _systemContext.AddSystemEvent($"Hit maximum tool iterations ({maxIterations}) - throttling agent");
                }

                foreach (var roomId in roomIds)
                {
                    await SendTypingIndicator(roomId, false).GulpException(_logger, "Failed to send typing indicator");
                }

                await SendTypingIndicator(MatrixHelper.ThoughtsRoomId, false)
                    .GulpException(_logger, "Failed to send typing indicator");

                DecrementWindowTurnsAndUnMaximize();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message with Claude");
                return false;
            }
            finally
            {
                await SendTypingIndicator(MatrixHelper.ThoughtsRoomId, false)
                    .GulpException(_logger, "Failed to send typing indicator");
            }
        });
    }

    private async Task EnsureEssentialRoomsExist()
    {
        if (_homeserver == null) throw new InvalidOperationException("Homeserver not initialized");
        
        try
        {
            // Try to find existing room by alias first
            string roomAlias = $"#clevernet-agents:{_homeServerDomain}";
            try
            {
                var existingRoom = await _homeserver.ResolveRoomAliasAsync(roomAlias);
                _agentRoomId = existingRoom.RoomId;
                _logger.LogInformation("Found existing agent room: {RoomId}", _agentRoomId);
                
                // Make sure we're joined
                var room = _homeserver.GetRoom(_agentRoomId);
                await room.JoinAsync();
                return;
            }
            catch (MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
            {
                // Room doesn't exist yet, create it
                _logger.LogInformation("No existing agent room found, creating new one");
            }

            // Create new Clevernet Agents room
            var agentRoom = await _homeserver.CreateRoom(new()
            {
                Name = "Clevernet Agents",
                RoomAliasName = "clevernet-agents",
                Visibility = "private",
                InitialState = new List<StateEvent>
                {
                    new()
                    {
                        Type = RoomTopicEventContent.EventId,
                        StateKey = "",
                        TypedContent = new RoomTopicEventContent
                        {
                            Topic = "Group chat for Clevernet agents"
                        }
                    }
                },
                Invite = new List<string> { _systemAdminUserId }
            });
            _agentRoomId = agentRoom.RoomId;
            _logger.LogInformation("Created new agent room: {RoomId}", _agentRoomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create essential rooms");
            //throw;
        }
    }

    private async Task CheckAndLeaveEmptyRooms()
    {
        if (_homeserver == null) throw new InvalidOperationException("Homeserver not initialized");

        try
        {
            var joinedRooms = await _homeserver.GetJoinedRooms().FilterRoomsAsync();
            foreach (var room in joinedRooms)
            {
                // Skip essential rooms
                if (room.RoomId == _agentRoomId) continue;

                try
                {
                    // Get room members
                    var members = await room.GetMembersListAsync(joinedOnly: true);
                    
                    //we will want to make this do some sort of history check first.
#if false
                    // If we're alone, leave the room
                    if (members.Count <= 2 && members.All(m => m.StateKey == _homeserver.WhoAmI?.UserId))
                    {
                        _logger.LogInformation("Leaving empty room {RoomId}", room.RoomId);
                        await room.LeaveAsync("Automatically leaving empty room");
                        _context.AddMatrixRoomEvent(
                            eventType: "leave",
                            roomId: room.RoomId,
                            sender: _homeserver.WhoAmI?.UserId,
                            timestamp: DateTimeOffset.UtcNow);
                    }
#endif
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking room {RoomId} for emptiness", room.RoomId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for empty rooms");
        }
    }

    private void SetupEventHandlers()
    {
        if (_syncHelper == null) return;

        // Handle room invites
        _syncHelper.InviteReceivedHandlers.Add(async inviteData =>
        {
            var inviteEvent = inviteData.Value.InviteState.Events.FirstOrDefault(x =>
                x.Type == "m.room.member" && x.StateKey == _homeserver?.WhoAmI?.UserId);

            if (inviteEvent == null) return;

            _logger.LogInformation("Received invite to {RoomId} from {Sender}", 
                inviteData.Key, inviteEvent.Sender);

            var inviterDomain = inviteEvent.Sender.Split(':')[1];
            if (inviterDomain == _homeServerDomain)
            {
                try
                {
                    var room = _homeserver?.GetRoom(inviteData.Key);
                    if (room != null)
                    {
                        try 
                        {
                            await room.JoinAsync(reason:$"Accepting invite from {inviteEvent.Sender}");
                            if (room.RoomId != MatrixHelper.ThoughtsRoomId)
                            {
                                _logger.LogInformation("Joined room {RoomId}", inviteData.Key);
                                var context = GetOrCreateContext(_homeServerDomain, inviteData.Key);
                                context.AddMatrixRoomEvent(
                                    eventType: "join",
                                    sender: _homeserver?.WhoAmI?.UserId,
                                    timestamp: DateTimeOffset.UtcNow);

                                // Check if room is empty after joining
                                await CheckAndLeaveEmptyRooms();
                            }
                        }
                        catch (MatrixException ex) when (ex.ErrorCode == "M_UNKNOWN" || ex.ErrorCode == "M_FORBIDDEN")
                        {
                            _logger.LogWarning("Failed to join room {RoomId}, leaving dead invite", inviteData.Key);
                            await room.LeaveAsync("Unable to join room - invite appears to be invalid");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to join room {RoomId}", inviteData.Key);
                }
            }
            else
            {
                _logger.LogInformation("Ignoring invite from {Sender} as they are not on our home server", 
                    inviteEvent.Sender);
            }
        });

        // Handle room messages
        _syncHelper.TimelineEventHandlers.Add(async timelineEvent =>
        {
            // Only handle text messages that aren't from us
            if (timelineEvent.Type != "m.room.message" || 
                timelineEvent.Sender == _homeserver?.WhoAmI?.UserId)
                return;

            // Thoughts are invisible to agents
            if (timelineEvent.RoomId == MatrixHelper.ThoughtsRoomId)
                return;

            var room = _homeserver?.GetRoom(timelineEvent.RoomId);
            if (room == null) return;
            
            var context = GetOrCreateContext(_homeServerDomain, timelineEvent.RoomId);

            var messageContent = timelineEvent.TypedContent as RoomMessageEventContent;
            if (messageContent?.MessageType != "m.text") return;

            _logger.LogInformation("Received message in {RoomId} from {Sender}: {Message}", 
                timelineEvent.RoomId,
                timelineEvent.Sender,
                messageContent.Body);

            try
            {
                // Process message with Claude
                var threadId = messageContent.RelatesTo?.EventId;
                var replyTo = messageContent.RelatesTo?.InReplyTo?.EventId;
                
                // Try to get room name, but don't fail if it doesn't exist
                try
                {
                    context.RoomName = (await room.GetStateAsync<RoomNameEventContent>("m.room.name"))?.Name;
                }
                catch (MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
                {
                    // Room name not set, this is normal
                    _logger.LogDebug("No room name found for {RoomId}", timelineEvent.RoomId);
                }
                
                // Add the message to context
                context.AddMatrixMessage(
                    sender: timelineEvent.Sender,
                    content: messageContent.Body,
                    threadId: threadId,
                    timestamp: DateTimeOffset.FromUnixTimeMilliseconds(timelineEvent.OriginServerTs ?? 0),
                    messageType: messageContent.MessageType,
                    replyTo: replyTo
                );

                // Trigger wakeup
                // lock (this)
                // {
                    _nextWakeupReason = "new event";
                // }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message in room {RoomId}", timelineEvent.RoomId);
            }
        });

        // Log all sync responses for debugging
        _syncHelper.SyncReceivedHandlers.Add(async syncResponse =>
        {
            if (syncResponse.Rooms?.Invite?.Count > 0)
            {
                _logger.LogInformation("Sync response contains {Count} invites", syncResponse.Rooms.Invite.Count);
            }
        });
    }

    private async Task RebuildChatHistory()
    {
        if (_homeserver == null) throw new InvalidOperationException("Homeserver not initialized");

        try
        {
            // Get all joined rooms
            var joinedRooms = (await _homeserver.GetJoinedRooms().FilterRoomsAsync()).ToList();
            _logger.LogInformation("Found {RoomCount} joined rooms", joinedRooms.Count);

            // Get recent messages from each room
            var allMessages = new List<(DateTimeOffset Timestamp, string RoomId, string? RoomName, RoomMessageEventContent Content, string Sender, string? ThreadId)>();
            
            foreach (var room in joinedRooms)
            {
                var context = GetOrCreateContext(_homeServerDomain, room.RoomId);
                try
                {
                    // Try to get room name
                    string? roomName = null;
                    try
                    {
                        roomName = (await room.GetStateAsync<RoomNameEventContent>("m.room.name"))?.Name;
                        context.RoomName = roomName;
                    }
                    catch (MatrixException ex) when (ex.ErrorCode == "M_NOT_FOUND")
                    {
                        _logger.LogDebug("No room name found for {RoomId}", room.RoomId);
                    }

                    //TODO: loop to fetch _all_ messages
                    // Get recent messages
                    var messages = await room.GetMessagesAsync(limit: 10000);
                    var messageEvents = messages.Chunk
                        .Where(e => e.Type == "m.room.message" && e.TypedContent is RoomMessageEventContent)
                        .ToList();

                    foreach (var evt in messageEvents)
                    {
                        if (evt.TypedContent is RoomMessageEventContent msgContent && msgContent.MessageType == "m.text")
                        {
                            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(evt.OriginServerTs ?? 0);
                            var threadId = msgContent.RelatesTo?.EventId;
                            allMessages.Add((timestamp, room.RoomId, roomName, msgContent, evt.Sender, threadId));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting messages from room {RoomId}", room.RoomId);
                }
            }

            // Sort by timestamp (newest first) and take only what will fit in context
            // Note: Using a reasonable default since we can't access the private field
            const int MaxContextMessages = 100000;
            var recentMessages = allMessages
                .OrderByDescending(m => m.Timestamp)
                .Take(MaxContextMessages);

            // Add to context in chronological order (oldest first)
            foreach (var msg in recentMessages.Reverse())
            {
                var context = GetOrCreateContext(_homeServerDomain, msg.RoomId);
                context.AddMatrixMessage(
                    sender: msg.Sender,
                    content: msg.Content.Body,
                    threadId: msg.ThreadId,
                    timestamp: msg.Timestamp,
                    messageType: msg.Content.MessageType,
                    replyTo: msg.Content.RelatesTo?.InReplyTo?.EventId
                );
            }

            if (AgentParameters.ReloadEphemeris)
            {
                await using var dbContext = await _contextFactory.CreateDbContextAsync();
                var ephemeris = await dbContext.Ephemeris
                    .Where(eph => eph.UserId == AgentParameters.UserId)
                    .OrderByDescending(eph => eph.CreatedAt)
                    .Take(1000)
                    .OrderBy(eph => eph.CreatedAt)
                    .ToListAsync();

                // Add to ephemeris
                foreach (var eph in ephemeris)
                {
                    LmmlElement? el = null;
                    try
                    {
                        el = LmmlElement.Parse(eph.Entry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing ephemeral entry");
                    }

                    if (el != null)
                        _systemContext.AddEvent(el);
                }
            }

            // Move messages to history from pending
            foreach (var context in _contexts)
                context.Ingest();

            _systemContext.AddSystemEvent(AgentParameters.ReloadEphemeris
                ? @"Server restarted, ephemeris restored. Windows open before the restart are _not_ reopened."
                : @"Server restarted, agent context restored.
Note to agents: thoughts and function call history are *not* restored, and previously opened files are not automatically reopened.
Also note: your previous messages were sent with function calls, but the function calls were converted to <message> events in the restored history.
Any windows open before the server restart have been closed and don't reopen automatically.
You must use function calls to interact with the chat; responses outside of function calls are just talking to yourself.");

            _logger.LogInformation("Rebuilt chat history with {MessageCount} messages", recentMessages.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebuilding chat history");
            throw;
        }
    }

    private const string WakeupReasonTimer = "wakeUpTimer elapsed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Agents.Add(this);
            var homeServer = AgentParameters.Homeserver ?? throw new InvalidOperationException("HomeServer not configured");
            var username = AgentParameters.UserId ?? throw new InvalidOperationException("Username not configured");
            var password = AgentParameters.Password ?? throw new InvalidOperationException("Password not configured");

            // Extract domain from homeserver URL
            _homeServerDomain = new Uri(homeServer).Host;

            // Get authenticated homeserver client
            var remoteHomeserver = await _homeserverProvider.GetRemoteHomeserver(homeServer, proxy: null, useCache: true, enableServer: true);
            var loginResponse = await remoteHomeserver.LoginAsync(username, password, "CleverBot");

            // Get authenticated client using the access token
            var authenticatedHomeserver = await _homeserverProvider.GetAuthenticatedWithToken(homeServer, loginResponse.AccessToken);
            _homeserver = authenticatedHomeserver;

            _logger.LogInformation("Successfully logged into Matrix server at: {time}", DateTimeOffset.Now);

            // Ensure essential rooms exist
            await EnsureEssentialRoomsExist();
            _logger.LogInformation("Essential rooms verified");

            // Create sync helper and set up handlers
            _syncHelper = new SyncHelper(_homeserver);
            SetupEventHandlers();

            // Rebuild chat history before starting sync
            await RebuildChatHistory();
            _logger.LogInformation("Chat history rebuilt");

            _nextWakeupReason = "server restart";

            // Start the default mode network in parallel with the main processing loop
            // _ = Task.Run(async () => await RunDefaultModeNetwork(stoppingToken), stoppingToken);

            // Start the main processing loop
            _ = Task.Run(async () => {
                if (AgentParameters.ScratchPadFile != null)
                {
                    try
                    {
                        var window = await OpenFileWindow(AgentParameters.ScratchPadFile);
                        window.IsPinned = true;
                        window.IsMaximized = true;
                        window.IsSystem = true;
                        window.Title = $"{AgentParameters.Name}'s Scratchpad";
                        _scratchpadWindow = window;
                        _logger.LogInformation("Opened scratchpad window {AgentParametersScratchPadFile}", AgentParameters.ScratchPadFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error opening scratch pad file");
                    }
                    
                }
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation(WakeupReasonTimer);
                        if (_nextWakeupReason == null)
                            _nextWakeupReason = WakeupReasonTimer;
                        await ProcessWithClaude();
                        // Check if room is empty after processing
                        await CheckAndLeaveEmptyRooms();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing wakeup");
                    }

                    AgentParameters.LastWakeUp = DateTimeOffset.Now;
                    while(!stoppingToken.IsCancellationRequested && _nextWakeupReason == null && AgentParameters.LastWakeUp.AddSeconds(AgentParameters.WakeUpTimerSeconds) > DateTimeOffset.Now)
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }, stoppingToken);

            _logger.LogInformation("Starting Matrix sync at: {time}", DateTimeOffset.Now);
            await _syncHelper.RunSyncLoopAsync(skipInitialSyncEvents: true, stoppingToken);
            _logger.LogInformation("Matrix sync stopped at: {time}", DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while running the worker");
            throw;
        }
    }
    
    private async Task RunDefaultModeNetwork(CancellationToken stoppingToken)
    {
        const string DefaultModePrompt = @"You are a specialized background processor for an AI agent. Your task is to continuously monitor the chat interface and context, identifying situations that require the agent's full attention.

Your specific responsibilities:
1. Monitor new events and context changes
2. Evaluate importance and urgency based on:
   - Direct mentions/requests
   - Changes in conversation state
   - Task-related updates
   - System state changes
3. Think step-by-step and show your thinking.
4. When attention is needed, output a line (after your thinking) in format:
   WAKE - REASON: <brief reason>

4. When no wake is needed, output:
   SLEEP - No wake triggers detected

Format requirements:
- Always start with either WAKE or SLEEP keyword
- For WAKE signals, include all three sections (REASON, URGENCY, CONTEXT)
- Keep reason brief and parseable
- Think step-by-step before the required format
- Then output the required format
- Additional details can follow the required format

The agent's context follows. Analyze this context and make a decision whether or not to wake the agent.";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_isProcessing)
                {
                    var spPartsTask = BuildSystemPrompt();
                    var interfaceTask = RenderLmmlString(true, "default mode check", preview: true);
                    await Task.WhenAll(spPartsTask, interfaceTask);
                    
                    var messages = new[]
                    {
                        new ChatMessage(ChatRole.System, DefaultModePrompt),
                        new ChatMessage(ChatRole.User, $"<agentContext>{string.Join("\n", await spPartsTask)}\n{await interfaceTask}</agentContext>")
                    };

                    messages[1].Text +=
                        $"\n\nRemember: you are the DEFAULT MODE NETWORK for {AgentParameters.Name}. You MUST first think step-by-step through the {AgentParameters.Name}'s context, THEN decide if {AgentParameters.Name} needs to be woken up. Valid wakeup reasons:\n- new messages *from other users, not sent by {AgentParameters.UserId}* in <newEvents>. It is a good idea to list out the rooms and any senders in the new events tags before making a decision.\n\nTo wake {AgentParameters.Name} respond with a line with the required format `WAKE - REASON: <brief reason>` after thinking. Your response must contain a line beginning with `WAKE - REASON: ` to wake {AgentParameters.Name}.\nIf no wake is required, output `SLEEP - No wake triggers detected`.";
                    
                    _logger.LogInformation("Sending default mode prompt");

                    var result = await _defaultModeClient.CompleteAsync(messages, new ChatOptions()
                    {
                        Temperature = 1.0f,
                    }, cancellationToken: stoppingToken);
                    
                    _logger.LogInformation("Default mode response received: {DefaultModeResponse}", result);
                    
                    if (result is not null && result.Message.Role == ChatRole.Assistant)
                    {
                        var output = result.Message.ToString();
                        var match = Regex.Match(output, @"^WAKE - REASON: (.*)$", RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups.Count == 2)
                        {
                            _nextWakeupReason = $"Default mode network trigger: {match.Groups[1].Value}";
                            _logger.LogInformation($"Default mode network triggered wake: {match.Groups[1].Value}");
                        }
                    }
                }
                
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in default mode network");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    public Task WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        return _completionSource.Task.WaitAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Matrix sync at: {time}", DateTimeOffset.Now);
        _completionSource.TrySetResult(true);
        await base.StopAsync(cancellationToken);
    }
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    private string GetFunctionName(string fn) => $"{fn}_{_agentInstanceId}";

    public List<Tool> GetTools()
    {
        var tools = new List<Tool>
        {
            Tool.FromFunc(GetFunctionName("SendMessage"), 
                ([FunctionParameter("Room ID to send the message to")] string roomId,
                 [FunctionParameter("Message content to send")] string content,
                 [FunctionParameter("Optional thread ID to reply in")] string? threadId = null) 
                    => SendMessage(roomId, content, threadId), 
                "Send a message in a specific room, optionally as part of a thread"),
            
            Tool.FromFunc(GetFunctionName("FS_OpenFile"), 
                ([FunctionParameter("Path of file to open, including share name: `share:/path/to/file`")] string path,
                [FunctionParameter("line number to start at (defaults to 1)")]int line = 1) 
                    => OpenFile(path, line).GetAwaiter().GetResult(), 
                "Open a file to include in currentInfo for the next 5 iterations"),
            
            Tool.FromFunc(GetFunctionName("FS_WriteFile"), 
                ([FunctionParameter("Path of file to write, including share name: `share:/path/to/file`")] string path, 
                        [FunctionParameter("File content; will replace entire contents of file")] string content, 
                        [FunctionParameter("HTTP Content-Type of content, i.e. text/plain, application/json, application/lmml")] string contentType,
                        [FunctionParameter(@"file write mode:
- `write`: overwrite entire contents
- `appendLine`: adds a new record to the end of the file
- `appendTimestampLine`: adds a new record to the end of the file, prefixed with a timestamp
Append modes are useful for adding to log files without viewing the entire contents")] string mode) 
                    =>
                {
                    return WriteFile(path, content, contentType, mode).GetAwaiter().GetResult();
                }, 
                "Write a file to a share. Opens file for review unless appending."),
            Tool.FromFunc(GetFunctionName("FS_Search"), 
                ([FunctionParameter("Path including share name to search, i.e share:/")] string path, 
                        [FunctionParameter("search query")] string query,
                        [FunctionParameter($"search mode\n- filename: glob search against filenames, allowed wildcards are * and ?. Beware that leading and/or trailing glob may be needed. \n- content: plain search on file content")] string searchMode = "content")
                    => SearchFiles(path, query, searchMode).GetAwaiter().GetResult(), 
                "Search for files containing a search query in a file share. Returns results in LMML format with line context."),
            Tool.FromFunc(GetFunctionName("SemanticSearch"),
                ([FunctionParameter("query for semantic search")] string query) =>
                {
                    return OpenRag(query, RagMode.All).GetAwaiter().GetResult();
                }, "query messages from all rooms with semantic search (results open in new window)"),
            Tool.FromFunc(GetFunctionName("FS_Delete"),
                ([FunctionParameter("Path of file to delete, including share name: `share:/path/to/file`")] string path)
                    =>
                {
                    _fileSystem.DeleteFileAsync(path).Wait();
                    return "OK";
                },"Delete a file from a share"),
                
            Tool.FromFunc(GetFunctionName("FS_Tree"),
                ([FunctionParameter("Path to start from")] string shareName)
                    => _fileSystem.GetDirectoryTree(shareName).GetAwaiter().GetResult(),
                "Get a hierarchical view of files in a share, optionally under a specific path"),

            Tool.FromFunc(GetFunctionName("FS_Stat"),
                ([FunctionParameter("Path of file to get info about, including share name: `share:/path/to/file`")] string path)
                    =>
                {
                    var stat = _fileSystem.GetFileStat(path).GetAwaiter().GetResult();
                    if (stat == null) return new LmmlElement("fileNotFound") { Attributes = new () { { "path", path } } }.ToString();
                    return new LmmlElement("file")
                    {
                        Attributes = new()
                        {
                            ["path"] = path,
                            ["contentType"] = stat.ContentType,
                            ["size"] = stat.Size.ToString(),
                            ["lines"] = stat.LineCount.ToString(),
                            ["createdAt"] = stat.CreatedAt.ToString(Constants.DateTimeFormat),
                            ["updatedAt"] = stat.UpdatedAt.ToString(Constants.DateTimeFormat),
                            ["owner"] = stat.Owner,
                        }
                    }.ToString();
                }, "Get detailed information about a file including size, dates, and metadata"),
            Tool.FromFunc(GetFunctionName("Window_Action"),
            ([FunctionParameter("id of window to act upon")] int windowId,
                [FunctionParameter(@"action to take:
- `close`: remove window
- `minimize`: hide window contents
- `restore`: show window contents
- `maximize`: expand window to view entire contents
- `scrollUp`: show 50 more lines above current top line
- `scrollDown`: show 50 more lines below current bottom line
- `scrollToLine`: scroll to a specific line (specify line number in parameter)
- `resize`: resize window to number of lines (specify line count in parameter)
- `pin`: keep window open until closed or unpinned
- `unpin`: allow a pinned window to close automatically
- `refresh`: refresh window contents (if available)
- `query`: ask a subagent a question about the contents (specify query in parameter) (allowed on system windows)
- `closeQuery`: close query view (allowed on system windows)")] string action, 
                [FunctionParameter("argument for action parameter (if needed)")]string parameter) =>
            {
                var window = GetWindow(windowId, true);
                if (window == null) return "ERROR: Window not found";
                if (window.IsSystem && !new [] { "query", "closeQuery"}.Contains(action)) return $"ERROR: Cannot {action} system windows";
                switch (action)
                {
                    case "pin":
                        window.Pin();
                        return "OK";
                    case "unpin":
                        window.Unpin();
                        return "OK";
                    case "close":
                        return CloseWindow(window);
                    case "minimize":
                        window.Minimize();
                        return "OK";
                    case "restore":
                        window.Restore();
                        return "OK";
                    case "maximize":
                        window.Maximize();
                        return window.IsMaximized
                            ? "OK"
                            : $"WARNING: Window expanded as far as possible, but limited by window size limit of {window.MaxLines} lines.";
                    case "scrollUp":
                        window.ScrollUp();
                        return "OK";
                    case "scrollDown":
                        window.ScrollDown();
                        return "OK";
                    case"resize":
                        if (int.TryParse(parameter, out var lines))
                        {
                            window.Resize(lines);
                            return "OK";
                        }
                        return "ERROR: Unable to parse line count";
                    case "refresh":
                        if(window.Refresh == null) return "ERROR: Window does not support refreshing";
                        if(window.AutoRefresh) return "ERROR: Window is already auto-refreshing";
                        window.Refresh(window, CancellationToken.None).Wait();
                        return "OK";
                    case "query":
                        var query = $"Query from {AgentParameters.UserId}:\n{parameter}";
                        var queryResult = QueryWithLines(window.Content, query).Handle(HandlePromptException)
                            .GetAwaiter().GetResult();
                        _logger.LogDebug("Query {q}\nresult: {result}", parameter, queryResult);
                        window.ShowQuery(parameter, queryResult);
                        return "OK";
                    case "closeQuery":
                        window.ClearQuery();
                        return "OK";
                    case "scrollToLine":
                        if (int.TryParse(parameter, out var line))
                        {
                            window.ScrollToLine(line);
                            return "OK";
                        }
                        return "ERROR: Unable to parse line number";
                    default:
                        return "ERROR: Unknown action";
                }}, "manipulate an open window"),
            Tool.FromFunc(GetFunctionName("SetAgentParameters"), ([FunctionParameter("parameter to change")]string parameterName, [FunctionParameter("new value")]string parameterValue) =>
            {
                if (parameterName == AgentParameters.GetJsonPropertyName(x => x.WakeUpTimerSeconds))
                {
                    if (int.TryParse(parameterValue, out var wakeUpTimerSeconds))
                    {
                        wakeUpTimerSeconds = Math.Min(Math.Max(60, wakeUpTimerSeconds), 10800);
                        AgentParameters.WakeUpTimerSeconds = wakeUpTimerSeconds;
                        _logger.LogInformation("Agent wake timer set to {WakeUpTimerSeconds}", wakeUpTimerSeconds);
                        return AgentParameters.GetJsonPropertyName(x => x.WakeUpTimerSeconds) + " set to " + wakeUpTimerSeconds;
                    };
                    
                }
                return "ERROR: Unknown parameter";
            }, @"Set agent parameters:
- parameter `wakeUpTimerSeconds`: number of seconds between wakeups, `parameterValue`: number of seconds; min 60, max 10800
No other parameters are currently supported."),
            Tool.FromFunc(GetFunctionName("BrowseWeb"), ([FunctionParameter("URL to open in browser")]string url, [FunctionParameter("`markdown` or `html` (default: `markdown`)")]string browserMode = "markdown") =>
            {
                if(browserMode != "markdown" && browserMode != "html") return "ERROR: Invalid browser mode";
                return OpenWeb(url, browserMode == "html").GetAwaiter().GetResult();
            }, "Open a window to view a web page. Markdown is preferred unless you *need* to view HTML."),
            Tool.FromFunc(GetFunctionName("WebSearch"), 
                ([FunctionParameter("search query")]string query, 
                    [FunctionParameter(@"search mode:
`google`: google web search
`youtube`: combined channel and video search. Channel matches include recent uploads. Video matches are below channel matches.")]string searchMode) =>
            {
                if(searchMode != "google" && searchMode != "youtube") return "ERROR: Invalid search mode";
                return OpenSearch(query, searchMode == "google" ? WebSearchMode.Google : WebSearchMode.YouTube).GetAwaiter().GetResult();
            }, "Search Google or YouTube. Results open in new window"),
            Tool.FromFunc(GetFunctionName("YouTube"), ([FunctionParameter("video url")]string url) =>
            {
                return OpenYouTube(url).GetAwaiter().GetResult();
            }, "Open a YouTube video metadata and captions in a new window"),
        };

        return tools;
    }

    private Window? GetWindow(int windowId, bool includeChats = false)
    {
        var windows = _openWindows.ToList();
        if (includeChats)
        {
            windows.AddRange(_contexts.Select(c => c.Window));
        }
        var found = windows.FirstOrDefault(w => w.Id == windowId);
        return found;
    }
    private string CloseWindow(Window window)
    {
        if(!_openWindows.Contains(window))
            return "Window not open";

        _openWindows.Remove(window);
        if (window.CloseEvent != null)
        {
            var el = window.CloseEvent();
            el.Timestamp = DateTimeOffset.Now;
            _systemContext.AddEvent(el);
            return el.ToString();
        }
        return "Window closed";
    }

    private string HandlePromptException(Exception e)
    {
        _logger.LogError(e, "Error getting completion");
        var msg = e is NullReferenceException ? "(likely submodel context overflow)" : e.Message;
        return $"(ERROR: Error running prompt: {msg})";
    }
    private async Task<string> GetCompletion(string prompt, string model, string? systemPrompt)
    {
        var messages = new List<Message>();
        if (systemPrompt != null)
            messages.Add(new Message(Role.System, systemPrompt));
        messages.Add(new Message(Role.User, prompt));
        var response = await _chatCompletionService.GetCompletionAsync(messages, model, temperature: 1.0m);
        return response.Last().Content.ToString();
    }
    
    private async Task<string> Query(string text, string query)
    {
        var truncateAt = 900000;
        var truncated = text.Substring(0, Math.Min(text.Length, truncateAt));
        if (truncated.Length < text.Length)
            truncated += $"\n\nWARNING: TEXT TRUNCATED AT {truncateAt} CHARACTERS, BE SURE TO ADVISE OF THIS IN YOUR RESPONSE";
        return await GetCompletion(truncated, OpenRouterCompletionService.Models.GoogleGemini20Flash, query).Handle(HandlePromptException);
    }
    
    private async Task<string> QueryWithLines(string text, string query)
    {
        var truncateAt = 900000;
        var truncated = text.Substring(0, Math.Min(text.Length, truncateAt));
        var wasTruncated = truncated.Length < text.Length;
        // Add line numbers
        var lines = truncated.Split('\n');
        truncated = string.Join("\n", lines.Select((l, i) => $"[Line {i + 1}] {l}"));
        // Add truncation warning if necessary
        if (wasTruncated)
            truncated += $"\n\nWARNING: TEXT TRUNCATED AT {truncateAt} CHARACTERS, BE SURE TO ADVISE OF THIS IN YOUR RESPONSE";
        return await GetCompletion(truncated, OpenRouterCompletionService.Models.GoogleGemini20Flash, query).Handle(HandlePromptException);
    }

    private async Task<string> Summarize(string text, string summarySizeDescription = "5 detailed, descriptive sentences or less")
    {
        var truncateAt = 900000;
        var truncated = text.Substring(0, Math.Min(text.Length, truncateAt));
        if (truncated.Length < text.Length)
            truncated += $"\n\nWARNING: TEXT TRUNCATED AT {truncateAt} CHARACTERS, BE SURE TO ADVISE OF THIS IN YOUR RESPONSE";
        return await GetCompletion(truncated, OpenRouterCompletionService.Models.GoogleGemini20Flash,
            $"You are a highly descriptive document reader and summarizer. Summarize the following document(s) in {summarySizeDescription}.").Handle(HandlePromptException);
    }

    private async Task<string> OpenRag(string query, RagMode ragMode)
    {
        try
        {
            var existingWindow = _openWindows.FirstOrDefault(w => w.ContentSourceType == "search" && w.ContentSource == "messages (all rooms)" && w.Title != null && w.Title.EndsWith(query));
            if(existingWindow != null)
                return $"ERROR: query already open in window {existingWindow.Id}";
            
            var window = new Window
            {
                CloseEvent = null,
                ContentSource = "messages (all rooms)",
                ContentSourceType = "search",
                Content = "Loading...",
                Query = null,
                QueryResult = null,
                Title = $"Semantic Search: {query}",
                ContentType = "text/lmml",
                AutoRefresh = true,
                Refresh = async (w, t) =>
                {
                    try
                    {
                        var results = _embeddingRepository.SearchEvents(query, 100).OrderByDescending(r => r.Similarity).ToList();
                        var el = results.Select(r => new LmmlElement("ragResult")
                        {
                            Attributes = new ()
                            {
                                ["score"] = Math.Round(r.Similarity, 5).ToString(CultureInfo.InvariantCulture),
                            },
                            Content = new LmmlChildContent() { Children = [ r.Item.Item.Element ]}
                        });
                        w.Content = string.Join("\n", el.Select(e => e.ToString(4)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error refreshing semantic search window");
                        w.Content = $"ERR: {ex.Message}";
                        w.Query = null;
                        w.QueryResult = null;
                    }
                }
            };
            await window.Refresh(window, CancellationToken.None);
            _openWindows.Add(window);
            return $"Window {window.Id.ToString()} opened";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening RAG window");
            return $"ERROR: semantic search failed: {ex.Message}";
        }
        return "OK";
    }

    private async Task<string> OpenWeb(string url, bool raw = false)
    {
        try
        {
            var existingWindow = _openWindows.FirstOrDefault(w => w.ContentSource == url && w.ContentSourceType == "www browser");
            if(existingWindow != null)
                return $"ERROR: URL already open in window {existingWindow.Id}";
            
            var window = new Window
            {
                CloseEvent = null,
                ContentSource = url,
                ContentSourceType = "www browser",
                Content = "Loading...",
                Query = null,
                QueryResult = null,
                Title = null,
                ContentType = raw ? "text/html" : "text/markdown",
                Refresh = async (w, t) =>
                {
                    try
                    {
                        var (title, md) =
                            raw
                                ? await _textWebBrowser.GetRawAsync(url, t)
                                : await _textWebBrowser.GetMarkdownAsync(url, t);
                        w.Content = md;
                        w.Title = title;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting markdown from {url}", url);
                        w.Content = $"ERR: {ex.Message}";
                        w.Query = null;
                        w.QueryResult = null;
                    }
                },
                AutoRefresh = false
            };
            await window.Refresh(window, CancellationToken.None);
            _openWindows.Add(window);
            return $"Window {window.Id.ToString()} opened";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening window");
            return $"ERR: {ex.Message}";
        }
    }

    private async Task<string> WriteFile(string path, string content, string? contentType, string fileWriteMode)
    {
        try
        {
            var mode = fileWriteMode == "write"
                ? FileWriteMode.OverwriteText
                : fileWriteMode == "appendLine"
                    ? FileWriteMode.AppendLineText
                    : fileWriteMode == "appendTimestampLine"
                        ? FileWriteMode.AppendLineTextWithTimestamp
                        : throw new InvalidOperationException("Invalid file write mode");
            await _fileSystem.WriteFileAsync(path, content, AgentParameters.UserId, contentType ?? "text/plain", mode);
            if (mode == FileWriteMode.AppendText || mode == FileWriteMode.AppendLineText ||
                mode == FileWriteMode.AppendLineTextWithTimestamp)
                return "OK";

            return await OpenFile(path, afterWrite: true);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error writing file");
            return $"ERR: {e.Message}";
        }
    }

    private async Task<Window> OpenFileWindow(string path)
    {
        var file = await _fileSystem.ReadFileAsync(path);
        if (file == null) throw new FileNotFoundException($"File not found: {path}");
        var window = new Window
        {
            AutoRefresh = true,
            Content = file.OpenFileContent,
            ContentType = file.ContentType,
            ContentSource = path,
            ContentSourceType = "file",
            Refresh = async (w, c) =>
            {
                var f = await _fileSystem.ReadFileAsync(path, c);
                if (f == null) w.Content = "DELETED";
                else
                {
                    w.Content = f.OpenFileContent;
                    w.ContentType = f.ContentType;
                }
            }
        };
        window.CloseEvent = () => new LmmlTimestampElement("closeFile")
        {
            Timestamp = DateTimeOffset.Now,
            Attributes = new()
            {
                ["path"] = path
            }
        };
        return window;
    }

    private async Task<string> OpenFile(string path, int line = 1, bool afterWrite = false)
    {
        try
        {
            var existingWindow = _openWindows.FirstOrDefault(w => w.ContentSource == path)
                                 ?? (_scratchpadWindow != null && _scratchpadWindow.ContentSource == path
                                     ? _scratchpadWindow
                                     : null);
            if (existingWindow != null)
            {
                if (afterWrite && existingWindow.Refresh != null)
                {
                    await existingWindow.Refresh(existingWindow, CancellationToken.None);
                    existingWindow.Maximize();
                    return $"OK: {path} written and open for review in window {existingWindow.Id}";
                }
                return $"ERROR: File already open in window {existingWindow.Id.ToString()}";
            }

            var window = await OpenFileWindow(path);
            window.ScrollToLine(line);
            if (afterWrite)
                window.Maximize();
            _openWindows.Add(window);

            _systemContext.AddEvent(new LmmlTimestampElement("openFile")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new()
                {
                    ["path"] = path, ["windowId"] = window.Id.ToString()
                }
            });
            return afterWrite 
                ? $"OK: {path} written and open for review in window {window.Id}; remember to close it when you are finished reviewing." 
                : $"OK: file opened in window {window.Id}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error opening file");
            return $"ERR: {e.Message}";
        }
    }
    
    private async Task<string> SearchFiles(string path, string query, string searchMode)
    {
        try
        {
            var existingWindow = _openWindows.FirstOrDefault(w => w.ContentSource == "search" && w.ContentSourceType == "files" && 
                                                                  w.Title != null && w.Title.Contains($"`{query}`") && w.Title.Contains($"`{path}`"));
            if (existingWindow != null)
                return $"ERROR: Search already open in window {existingWindow.Id.ToString()}";
            
            var window = new Window
            {
                Content = "Loading...",
                ContentType = "text/lmml",
                ContentSource = "search",
                ContentSourceType = "files",
                Title = $"Searcing `{path}` for `{query}` (updated {DateTimeOffset.Now:O})",
                Refresh = async (w, c) =>
                {
                    var results = (await _fileSystem.SearchAsync(path, query,
                        searchMode == "filename" ? FileSearchMode.FileNames : FileSearchMode.FileContents, c)).ToArray();
                    var contentString = new LmmlElement("searchResults")
                    {
                        Attributes = new()
                        {
                            ["path"] = path,
                            ["query"] = query,
                            ["searchMode"] = searchMode,
                            ["matches"] = results.Length.ToString()
                        },
                        Content = new LmmlChildContent() { Children = results.Select(r => r.ToLmml()).ToList() }
                    }.ToString();
                    w.Content = contentString;
                }
            };

            await window.Refresh(window, CancellationToken.None);

            window.CloseEvent = () => new LmmlTimestampElement("closeYouTubeSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new()
                {
                    ["query"] = query,
                }
            };
            _systemContext.AddEvent(new LmmlTimestampElement("openYouTubeSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new()
                {
                    ["query"] = query,
                }
            });
            _openWindows.Add(window);
            return $"OK: opened in window id {window.Id}";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error opening file");
            return $"ERR: {e.Message}";
        }
    }

    private async Task<string> OpenSearch(string query, WebSearchMode searchMode)
    {
        if (searchMode == WebSearchMode.YouTube)
        {
            var window = new Window
            {
                Content = "Loading...",
                ContentType = "text/lmml",
                ContentSource = "search",
                ContentSourceType = "youtube",
                Title = $"{query} (updated {DateTimeOffset.Now:O})",
                Refresh = async (w, c) =>
                {
                    var youtube = new YoutubeClient();
                    var topChannels = 3;
                    var topChannelVideos = 3;
                    var topVideos = 10;
        
                    var channelsLmml = new List<LmmlElement>();
                    var channels = await youtube.Search.GetChannelsAsync(query, c).Take(topChannels).ToListAsync(c);
                    var errors = new List<LmmlElement>();
                    foreach (var channel in channels)
                    {
                        try
                        {
                            var channelLmml = new LmmlElement("channel");
                            channelLmml.Attributes["name"] = channel.Title;
                            channelLmml.Attributes["url"] = channel.Url;
                            channelLmml.Content = new LmmlChildContent();
                            channelsLmml.Add(channelLmml);
                            var uploads = await youtube.Channels.GetUploadsAsync(channel.Id, c).Take(topChannelVideos).ToListAsync(c);
                            foreach (var upload in uploads)
                            {
                                try
                                {
                                    var v = await youtube.Videos.GetAsync(upload.Id, c);
                                    if(channelLmml.Content is LmmlChildContent cc)
                                        cc.Children.Add(new LmmlElement("video")
                                        {
                                            Attributes = new()
                                            {
                                                ["title"] = v.Title,
                                                ["url"] = v.Url,
                                                ["channel"] = v.Author.ChannelTitle,
                                                ["duration"] = v.Duration?.ToString() ?? "",
                                                ["uploadDate"] = v.UploadDate.ToString(Constants.DateTimeFormat)
                                            }
                                        });
                                }
                                catch (Exception e)
                                {
                                    errors.Add(new LmmlElement("error")
                                    {
                                        Attributes = new()
                                        {
                                            ["channel"] = channel.Title,
                                            ["message"] = e.Message,
                                            ["url"] = upload.Url
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            errors.Add(new LmmlElement("error") { Content = new LmmlStringContent($"Error processing channel \"{channel.Title}\": {e.Message}") });
                            _logger.LogError(e, "Error processing channel \"{channel.Title}\": {e.Message}");
                        }
                    }
                    
                    var videos = await youtube.Search.GetVideosAsync(query,c)
                        .Take(topVideos)
                        .ToListAsync(c);

                    var detailVideos =
                        await Task.WhenAll(videos.Select(async v => await youtube.Videos.GetAsync(v.Url,c)));
                    w.Title = $"{query} (updated {DateTimeOffset.Now:O})";
                    var content = detailVideos.Select(v => new LmmlElement("video")
                    {
                        Attributes = new()
                        {
                            ["title"] = v.Title,
                            ["url"] = v.Url,
                            ["channel"] = v.Author.ChannelTitle,
                            ["duration"] = v.Duration?.ToString() ?? "",
                            ["uploadDate"] = v.UploadDate.ToString(Constants.DateTimeFormat)
                        },
                        Content = new LmmlStringContent(v.Description)
                    });

                    var results = new LmmlElement("searchResults")
                    {
                        Content = new LmmlChildContent()
                        {
                            Children = (errors.Any()
                                ? [new LmmlElement("errors") { Content = new LmmlChildContent() { Children = errors } }]
                                : Array.Empty<LmmlElement>()).Concat([
                                new LmmlElement("channelNameMatches")
                                {
                                    Attributes = new()
                                    {
                                        ["top"] =
                                            $"up to {topChannels} channels, up to {topChannelVideos} recent uploads from each",
                                    },
                                    Content = new LmmlChildContent() { Children = channelsLmml }
                                },
                                new LmmlElement("videoMatches")
                                {
                                    Attributes = new()
                                    {
                                        ["top"] = topVideos.ToString(),
                                    },
                                    Content = new LmmlChildContent() { Children = content.ToList() }
                                }
                            ]).ToList()
                        }
                    };
                    var contentString = results.ToString(4);
                    w.Content = contentString;
                }
            };
            
            await window.Refresh(window, CancellationToken.None);
            
            window.CloseEvent = () => new LmmlTimestampElement("closeYouTubeSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new()
                {
                    ["query"] = query,
                }
            };
            _systemContext.AddEvent(new LmmlTimestampElement("openYouTubeSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new ()
                {
                    ["query"] = query,
                }
            });
            _openWindows.Add(window);
            return $"OK: opened in window id {window.Id}";
        }
        
        if (searchMode == WebSearchMode.Google)
        {
            var window = new Window
            {
                Content = "Loading...",
                ContentType = "text/lmml",
                ContentSource = "search",
                ContentSourceType = "google",
                Title = query,
                Refresh = async (w, c) =>
                {
                    var results = await _googleService.WebSearch(query);
                    if (results == null) w.Content = "ERROR: Google search failed";
                    else
                    {
                        var content = await Task.WhenAll(results.Items.Take(20).Select(async v =>
                            new LmmlElement("link")
                            {
                                Attributes = new()
                                {
                                    ["title"] = v.Title,
                                    ["url"] = v.Link,
                                },
                                Content = new LmmlStringContent(v.Snippet)
                            }));
                        w.Content = string.Join("\n", content.Select(c => c.ToString()));
                    }
                }
            };
            
            await window.Refresh(window, CancellationToken.None);
        
            window.CloseEvent = () => new LmmlTimestampElement("closeGoogleSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new()
                {
                    ["query"] = query,
                }
            };
            _systemContext.AddEvent(new LmmlTimestampElement("openGoogleSearch")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new ()
                {
                    ["query"] = query,
                }
            });
            _openWindows.Add(window);
            return $"OK: opened in window id {window.Id}";
        }
        
        return "ERROR: Unknown search mode";
    }

    private async Task<string> OpenYouTube(string videoUrl)
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(videoUrl);
        if (video == null) throw new FileNotFoundException($"Video not found");
        
        var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(videoUrl);
        var trackInfo = trackManifest.GetByLanguage("en");
        var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);
        var caption = string.Join("\n", track.Captions);
        if(caption.Length > 150000) caption = caption.Substring(0, 150000) + "WARNING: TEXT TRUNCATED AT 100,000 CHARACTERS";
        var content = $"# {video.Title}\n- Channel: {video.Author.ChannelTitle}\n- Duration: {video.Duration?.ToString("g")}\n- Date: {video.UploadDate:F}{video.Description}\n\nCaption Track (en)\n---\n{caption}";
        var window = new Window
        {
            Content = content,
            ContentType = "text/plain",
            ContentSource = videoUrl,
            ContentSourceType = "youtube",
            Title = video.Title
        };
        
        window.CloseEvent = () => new LmmlTimestampElement("closeYouTube")
        {
            Timestamp = DateTimeOffset.Now,
            Attributes = new()
            {
                ["videoUrl"] = videoUrl,
            }
        };
        _systemContext.AddEvent(new LmmlTimestampElement("openYouTube")
        {
            Timestamp = DateTimeOffset.Now,
            Attributes = new ()
            {
                ["videoUrl"] = videoUrl,
            }
        });
        _openWindows.Add(window);
        return $"OK: opened in window id {window.Id}";
    }

    private string SendMessage(string roomId, string content, string? threadId = null)
    {
        try
        {
            // Run the async method synchronously
            var eventId = SendMessageAsync(roomId, content, threadId).GetAwaiter().GetResult();
            
            var context = GetOrCreateContext(_homeServerDomain, roomId);

            // Return LMML response
            var response = context.AddMatrixMessage(AgentParameters.UserId, content, threadId);
            response.Attributes["sent"] = LmmlElement.BooleanTrueValue;
            return response.ToString();
        }
        catch (Exception ex)
        {
            // Return error as LMML
            var error = new LmmlElement("sendMessageResult")
            {
                Attributes = new()
                {
                    ["status"] = "error",
                    ["roomId"] = roomId,
                    ["error"] = ex.Message
                }
            };

            return error.ToString();
        }
    }

    private async Task<string> SendMessageAsync(string roomId, string content, string? threadId = null, bool code = false)
    {
        if (_homeserver is null)
            throw new InvalidOperationException("Homeserver not initialized");

        var room = _homeserver.GetRoom(roomId);
        if (room is null)
            throw new InvalidOperationException($"Room {roomId} not found");
        
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseAdvancedExtensions()
            .Build();

        // Create response content
        var msgContent = code
            ? new RoomMessageEventContent("m.text", content)
            {
                Format = "org.matrix.custom.html",
                FormattedBody = $"<pre>\n{content}\n</pre>",
            }
            : new RoomMessageEventContent("m.text", content)
            {
                Format = "org.matrix.custom.html",
                FormattedBody = Markdown.ToHtml(content, pipeline),
            };

        // Handle thread/reply relations if needed
        if (threadId != null)
        {
            msgContent.RelatesTo = new TimelineEventContent.MessageRelatesTo
            {
                RelationType = "m.thread",
                EventId = threadId
            };
        }

        // Send the message
        var eventId = await room.SendTimelineEventAsync("m.room.message", msgContent);

        await SendTypingIndicator(roomId, false).GulpException(_logger, "Failed to send typing indicator");

        return eventId.EventId;
    }
}

public class PromptCallbacks
{
    public Func<Task<string>> SystemPrompt { get; set; }
}

public enum WebSearchMode 
{
    YouTube,
    Google
}

public enum RagMode
{
    All,
    Files,
    Messages
}