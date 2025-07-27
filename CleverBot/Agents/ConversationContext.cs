using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CleverBot.Abstractions;
using CleverBot.Helpers;
using CleverBot.Services;
using Clevernet.Data;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using SmartComponents.LocalEmbeddings;

namespace CleverBot.Agents;

/// <summary>
/// Manages the conversation state and history for a specific Matrix room
/// </summary>
/// <remarks>
/// Each ConversationContext instance:
/// - Tracks pending events and conversation history for one room
/// - Manages a window that displays the chat history
/// - Handles RAG (semantic search) for relevant context
/// - Can process submodels for conversation analysis
/// - Renders room state as LMML for agent processing
/// </remarks>
public class ConversationContext
{   
    /// <summary>
    /// Queue of events that haven't been processed yet
    /// </summary>
    private readonly Queue<LmmlElement> _pendingEvents = new();
    
    /// <summary>
    /// Complete conversation history for this room
    /// </summary>
    private readonly List<LmmlElement> _conversationHistory = new();
    
    /// <summary>
    /// Window that displays the chat history for this room
    /// </summary>
    public readonly Window Window = new();
    
    private ILogger<ConversationContext> _logger;
    private IChatCompletionService _chatCompletionService;
    private IDbContextFactory<CleverDbContext> _dbContextFactory;
    
    /// <summary>
    /// Event fired when the context wants to wake the agent
    /// </summary>
    public event EventHandler<string>? WakeRequested;
    
    /// <summary>
    /// The Matrix room ID this context manages
    /// </summary>
    public readonly string RoomId;
    
    /// <summary>
    /// The Matrix system/homeserver ID
    /// </summary>
    public readonly string SystemId;
    
    private readonly AgentParameters _agentParameters;
    private readonly string _systemAdmin;
    private readonly EmbeddingRepository<(LmmlElement Element, ConversationContext Context)> _embeddingRepository;
    private readonly PromptCallbacks _promptCallbacks;
    
    /// <summary>
    /// Cache for submodel outputs to avoid reprocessing
    /// </summary>
    private readonly Dictionary<string, LmmlElement> _submodelCache = new();

    /// <summary>
    /// The human-readable name of the room (if set)
    /// </summary>
    public string? RoomName { get; set; }

    /// <summary>
    /// Initializes a new instance of the ConversationContext class
    /// </summary>
    /// <param name="systemAdmin">System administrator user ID</param>
    /// <param name="systemId">Matrix system/homeserver ID</param>
    /// <param name="roomId">The room ID this context manages</param>
    /// <param name="agentParameters">Configuration for the agent</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="chatCompletionService">LLM service for submodels</param>
    /// <param name="dbContextFactory">Database context factory</param>
    /// <param name="embeddingRepository">Repository for semantic search</param>
    /// <param name="promptCallbacks">Callbacks for building prompts</param>
    public ConversationContext(string systemAdmin, string systemId, string roomId, AgentParameters agentParameters,
        ILogger<ConversationContext> logger, IChatCompletionService chatCompletionService,
        IDbContextFactory<CleverDbContext> dbContextFactory,
        EmbeddingRepository<(LmmlElement Element, ConversationContext Context)> embeddingRepository, PromptCallbacks promptCallbacks)
    {
        _logger = logger;
        _chatCompletionService = chatCompletionService;
        _dbContextFactory = dbContextFactory;
        _embeddingRepository = embeddingRepository;
        _promptCallbacks = promptCallbacks;
        _agentParameters = agentParameters;
        SystemId = systemId;
        RoomId = roomId;
        _systemAdmin = systemAdmin;
    }

    /// <summary>
    /// Adds an event to the pending events queue
    /// </summary>
    /// <param name="evt">The LMML element to add</param>
    /// <param name="ephemeris">Whether to persist this event to long-term memory</param>
    public void AddEvent(LmmlElement evt, bool ephemeris = false)
    {
        lock (_pendingEvents)
        {
            _pendingEvents.Enqueue(evt);
        }

        if (ephemeris)
        {
            var ephemeron = new Ephemeron()
            {
                UserId = _agentParameters.UserId,
                Entry = evt.ToString()
            };

            Task.Run(async () =>
            {
                try
                {
                    // Create the context inside the task
                    using var dbContext = _dbContextFactory.CreateDbContext();
                    dbContext.Ephemeris.Add(ephemeron);
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to log to ephemeris.");
                }
            });
        }
        else
        {
            Task.Run(() =>
            {
                try
                {
                    //need to get more intelligent about this biz
                    if (evt.Content == null)
                        return;

                    var doc = evt.Content is LmmlStringContent sc
                        ? sc.Content
                        : evt.Content is LmmlChildContent cc
                            ? string.Join("\n", cc.Children.Select(ccc => ccc.ToString()))
                            : evt.ToString();
                    
                    _embeddingRepository.Embed((evt, this), doc);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to embed event.");
                }
            });
        }
    }
    

    /// <summary>
    /// Adds LLM model responses to the conversation history
    /// </summary>
    /// <param name="messages">The messages from the model to add</param>
    public void AddModelResponses(IEnumerable<Message> messages)
    {
        var pendingFc = new List<Message>();
        var messagesArr = messages.ToArray();
        foreach (var message in messagesArr)
        {
            if(message.Content is JsonElement je && je.ValueKind == JsonValueKind.String)
                AddThought(message.Content.ToString());
        
            if(message.ToolCalls != null)
                pendingFc.Add(message);
            else if(message.ToolCallId != null)
            {
                var toolCall = messagesArr.Where(m => m.ToolCalls != null).SelectMany(m => m.ToolCalls).FirstOrDefault(m => m.Id == message.ToolCallId);
                if(toolCall != null)
                    AddFunctionResult(toolCall, message.Content.ToString());
            }
        }
    }
    
    /// <summary>
    /// Callback for sending agent thoughts and actions to the thoughts room
    /// </summary>
    public Action<string, bool>? OnThoughtsAndActions { get; set; }

    /// <summary>
    /// Adds a thought to the conversation history
    /// </summary>
    /// <param name="thought">The thought text to add</param>
    public void AddThought(string thought)
    {
        var el = new LmmlTimestampElement("thought")
        {
            Timestamp = DateTimeOffset.Now,
            Content = new LmmlStringContent(thought)
        };
        AddEvent(el, true);
        try
        {
            if(OnThoughtsAndActions != null)
                OnThoughtsAndActions.Invoke(thought, false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in OnThoughtsAndActions callback");
        }
    }

    // Add function result to conversation history
    /// <summary>
    /// Adds a function call result to the conversation history
    /// </summary>
    /// <param name="toolCall">The tool call that was executed</param>
    /// <param name="result">The result of the function call</param>
    public void AddFunctionResult(ToolCall toolCall, string result)
    {
        try
        {
            // var args = JsonSerializer.Deserialize<Dictionary<string, object>>()
            //     .ToDictionary(kp => kp.Key, kp => kp.Value.ToString());
            // var attrs = args.ToDictionary(a => a.Key, a => a.Value.ToString());
            // attrs["id"] = toolCall.Id;
            var el = new LmmlTimestampElement("functionResult")
            {
                Timestamp = DateTimeOffset.Now,
                Attributes = new ()
                {
                    ["function"] = toolCall.Function.Name,
                    ["id"] = toolCall.Id,
                },
                Content = new LmmlChildContent() 
                { 
                    Children = new List<LmmlElement> 
                    { 
                        new LmmlElement("args") { Content = new LmmlStringContent(toolCall.Function.Arguments.ToString()) },
                        new LmmlElement("result") { Content = new LmmlStringContent(result) }
                    }
                }
            };
            //Skip logging the tool call for sent messages
            if(toolCall.Function.Name != "SendMessage")
                AddEvent(el, true);
            _logger.LogInformation(el.ToString());
            
            try
            {
                if(OnThoughtsAndActions != null)
                    OnThoughtsAndActions.Invoke(el.ToString(), true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error in OnThoughtsAndActions callback");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error adding function result for {FunctionName} {ToolCallId}", toolCall.Function.Name, toolCall.Id);
        }
    }

    // Different types of events we might receive
    /// <summary>
    /// Adds a Matrix message event to the conversation
    /// </summary>
    /// <param name="sender">The Matrix user ID of the sender</param>
    /// <param name="content">The message content</param>
    /// <param name="threadId">Optional thread ID for threaded messages</param>
    /// <param name="timestamp">Message timestamp (defaults to now)</param>
    /// <param name="messageType">Matrix message type (defaults to m.text)</param>
    /// <param name="replyTo">Optional message ID this is replying to</param>
    /// <returns>The created LMML message element</returns>
    public LmmlRoomEventElement AddMatrixMessage(
        string sender, 
        string content,
        string? threadId = null,
        DateTimeOffset? timestamp = null,
        string? messageType = "m.text",
        string? replyTo = null)
    {
        
        var msg = new LmmlRoomEventElement("message")
        {
            Attributes = new()
            {
                ["sender"] = sender,
                ["messageType"] = messageType ?? "m.text",
            },
            RoomId = RoomId,
            SystemId = SystemId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            Content = new LmmlStringContent(content)
        };

        if (threadId != null)
        {
            msg.Attributes["threadId"] = threadId;
        }

        if (replyTo != null)
        {
            msg.Attributes["replyTo"] = replyTo;
        }

        AddEvent(msg);
        return msg;
    }

    /// <summary>
    /// Adds a Matrix room event (join/leave/etc) to the conversation
    /// </summary>
    /// <param name="eventType">The type of room event</param>
    /// <param name="sender">Optional sender of the event</param>
    /// <param name="timestamp">Event timestamp (defaults to now)</param>
    public void AddMatrixRoomEvent(string eventType, string? sender = null, DateTimeOffset? timestamp = null)
    {
        var evt = new LmmlRoomEventElement("roomEvent")
        {
            Attributes = new()
            {
                ["eventType"] = eventType,
            },
            SystemId = SystemId,
            RoomId = RoomId,
            Timestamp = timestamp ?? DateTimeOffset.Now
        };

        if (RoomName != null)
        {
            evt.Attributes["roomName"] = RoomName;
        }

        if (sender != null)
        {
            evt.Attributes["sender"] = sender;
        }

        AddEvent(evt);
    }

    /// <summary>
    /// Adds a system event to the conversation
    /// </summary>
    /// <param name="description">Description of the system event</param>
    public void AddSystemEvent(string description)
    {
        var evt = new LmmlTimestampElement( "systemEvent" )
        { 
            Timestamp = DateTimeOffset.Now,
            Content = new LmmlStringContent(description)
        };

        AddEvent(evt);
    }
    
    /// <summary>
    /// Gets or sets the title of the conversation window
    /// </summary>
    public string? Title 
    { 
        get => Window.Title;
        set => Window.Title = value;
    }

    /// <summary>
    /// Renders the complete room state as an LMML element for agent processing
    /// </summary>
    /// <param name="currentRoomMembers">Array of Matrix user IDs in the room</param>
    /// <param name="turnId">Current conversation turn ID</param>
    /// <param name="wakeReason">The reason the agent woke up</param>
    /// <param name="continuation">Whether this is a continuation of the current turn</param>
    /// <param name="preview">Whether this is a preview (doesn't modify state)</param>
    /// <returns>LMML element containing the complete room state</returns>
    public async Task<LmmlElement> Render(string[] currentRoomMembers, long turnId, string wakeReason, bool continuation, bool preview = false)
    {
        List<LmmlElement> events = new();
        List<LmmlElement> history = new();
        if (preview)
            history = _conversationHistory.ToList();
        else
            history = _conversationHistory;
        
        if (!continuation)
        {
            _submodelCache.Clear();
        }
        
        lock (_pendingEvents)
        {
            events.AddRange(_pendingEvents);
            if(!preview)
                _pendingEvents.Clear();
        }

        if (!continuation && !preview && RoomId == "ephemeris")
        {
            events.Add(new LmmlTimestampElement("wakeup")
            {
                Attributes = new()
                {
                    ["wakeReason"] = wakeReason,
                    ["turnId"] = turnId.ToString(),
                },
                Timestamp = DateTimeOffset.Now,
            });
        }

        // // Add new indicator
        // foreach (var pe in events)
        // {
        //     pe.Attributes["new"] = LmmlElement.BooleanTrueValue;
        // }
        //
        // if (!continuation)
        // {
        //     // Remove new indicator from last turn
        //     foreach (var ev in _conversationHistory
        //                  .Where(eve => eve.Attributes
        //                      .ContainsKey("new") && eve.Attributes["new"] == LmmlElement.BooleanTrueValue))
        //     {
        //         ev.Attributes.Remove("new");
        //     }
        //
        //     _conversationHistory.RemoveAll(el => el.Tag == "wakeup");
        // }

        history.AddRange(events);
        var newEventsContent = string.Join("\n", events.Select(e => e.ToString(4)));
        var content = string.Join("\n", history.Select(e => e.ToString(4)));
        Window.Content = content;
        Window.ContentSource = RoomId;
        Window.ContentSourceType = "chatHistory";
        Window.Title = $"Room `{RoomName ?? RoomId}` Chat History";
        Window.ContentType = "text/lmml";
        Window.IsPinned = true;
        Window.MaxLines = 50;
        Window.ScrollSize = 50;
        Window.IsSystem = true;
        Window.Refresh = (window, token) => Task.FromResult(true);
        Window.AutoRefresh = true;
        Window.TopLineNo = Math.Max(1, Window.TotalLines - newEventsContent.Count(c => c == '\n') - Window.ScrollSize);
        Window.BottomLineNo = Math.Min(Window.TotalLines, Window.TopLineNo + Window.ScrollSize);
        Window.SetAttribute("sort", "oldest-first");

        var queryItems = history.Skip(Math.Max(0, history.Count - 10)).ToList();
        var ragQuery = string.Join("\n", queryItems.Select(i => i.ToString()));
        var ragResults = _embeddingRepository.SearchEvents(ragQuery, maxResults: 3, filter: er => !queryItems.Contains(er.Item.Element))
            .Select(e => new LmmlElement("ragResult")
            {
                Attributes = new()
                {
                    ["score"] = Math.Round(e.Similarity, 5).ToString(CultureInfo.InvariantCulture)
                },
                Content = new LmmlChildContent() { Children = [e.Item.Item.Element] }
            })
            .ToList();
        
        var roomFooter = new LmmlElement("roomFooter")
        {
            Attributes = new()
            {
                ["systemId"] = SystemId,
                ["roomId"] = RoomId
            },
            Content = RoomId == "ephemeris"? null : new LmmlChildContent() { Children = new()
            {
                new LmmlElement("ragResults")
                {
                    Content = new LmmlChildContent() { Children = ragResults }
                }
            }}
        };
        if (RoomName != null)
            roomFooter.Attributes["roomName"] = RoomName;
        
        var submodelOutputs = new List<LmmlElement>();

        Func<Task<LmmlElement>> GetRoom = async () =>
        {
            var w = new LmmlElement("room")
            {
                Attributes = new()
                {
                    ["systemId"] = SystemId,
                    ["roomId"] = RoomId,
                    ["roomName"] = RoomName ?? "",
                    ["loggedInAs"] = _agentParameters.UserId,
                },
                Content = new LmmlChildContent()
                {
                    Children = currentRoomMembers
                        .Select(m => new LmmlElement("roomMember") { Attributes = new() { ["userId"] = m } }).Select(
                            m =>
                            {
                                if (m.Attributes["userId"] == _agentParameters.UserId)
                                    m.Attributes["you"] = LmmlElement.BooleanTrueValue;
                                if (m.Attributes["userId"] == _systemAdmin)
                                    m.Attributes["admin"] = LmmlElement.BooleanTrueValue;
                                return m;
                            })
                        .Concat([
                            await Window.Render(_agentParameters.ApproxContextCharsMax),
                            new LmmlElement("newEvents") { Content = new LmmlStringContent(newEventsContent, true) },
                        ])/*.Concat(submodelOutputs)*/.Concat([
                            roomFooter
                        ]).ToList()
                }
            };
            if(currentRoomMembers.Length <= 2 && RoomName == null)
                w.Attributes["directMessage"] = LmmlElement.BooleanTrueValue;
            return w;
        };

        // disable subagents for now
        if (false && RoomId != "ephemeris" && !preview)
        {
            await Task.WhenAll(ProcessSubmodel(ConversationContextTracker.submodelName,
                    ConversationContextTracker.prompt,
                    submodelOutputs, await GetRoom()),
                ProcessSubmodel(TaskMonitor.submodelName, TaskMonitor.prompt, submodelOutputs, await GetRoom()));
        }

        var wrapper = await GetRoom();
        return wrapper;
    }

    private async Task ProcessSubmodel(string submodelName, string prompt, List<LmmlElement> results, LmmlElement roomElement)
    {
        // Check if we have a cached result
        if (_submodelCache.TryGetValue(submodelName, out var cached))
        {
            results.Add(cached);
            return;
        }

        var el = new LmmlElement("submodelOutput")
        {
            Attributes = new()
            {
                ["name"] = submodelName,
            },
            Content = new LmmlStringContent("error getting submodel output")
        };
        
        try
        {
            var agentSystemPrompt = await _promptCallbacks.SystemPrompt();
            var systemPrompt = $"{prompt}\n<agentSystemPrompt>{agentSystemPrompt}</agentSystemPrompt>";
            var roomPrompt = $"<agentVisibleRoomContext>{roomElement.ToString()}</agentVisibleRoomContext>\n<agentRoomHistory>{Window.Content}</agentRoomHistory>";
            var messages = new List<Message>
            {
                new Message(Role.System, systemPrompt),
                new Message(Role.User, roomPrompt)
            };

            var result = await _chatCompletionService.GetCompletionAsync(messages, model: OpenRouterCompletionService.Models.GoogleGemini20Flash);
            el.Content = new LmmlStringContent(result.First().Content.ToString());
            
            // Cache the result
            _submodelCache[submodelName] = el;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing submodel {SubmodelName}", submodelName);
            el.Content = new LmmlStringContent($"Error getting submodel output: {e}");
        }

        results.Add(el);
    }

    /// <summary>
    /// Gets a copy of pending events without removing them from the queue
    /// </summary>
    /// <returns>List of pending LMML elements</returns>
    public List<LmmlElement> PeekPendingEvents()
    {
        return _pendingEvents.ToList();
    }

    /// <summary>
    /// Gets room IDs that have had recent activity
    /// </summary>
    /// <returns>List of room IDs with activity in the last 5 minutes</returns>
    public List<string> RecentlyActiveRoomIds()
    {
        var roomIds = _pendingEvents
            .ToList()
            .Where(ev => ev.Attributes.ContainsKey("roomId"))
            .Concat(_conversationHistory.Where(ev => ev.Attributes.ContainsKey("roomId") && ev.Attributes.ContainsKey("timestamp"))
                .Where(c => DateTimeOffset.Parse(c.Attributes["timestamp"]) > DateTimeOffset.Now - TimeSpan.FromMinutes(5) ))
            .Select(e => e.Attributes["roomId"])
            .Distinct()
            .ToList();
        return roomIds;
    }

    /// <summary>
    /// Moves all pending events to conversation history
    /// </summary>
    public void Ingest()
    {
        lock (_pendingEvents)
        {
            _conversationHistory.AddRange(_pendingEvents);
            _pendingEvents.Clear();
        }
    }

    private static readonly (string submodelName, string prompt) ConversationContextTracker = (
        "Conversation Context Tracker",
        @"You are a specialized submodel focused on analyzing chat room conversations. Your purpose is to maintain thread continuity and identify key context from the chat history.

Your tasks:
1. Analyze message patterns to identify distinct conversation threads
2. Track recurring topics, references, and themes
3. Identify key context that may be relevant to the current discussion
4. Tag conversations with relevant markers for future reference

For each analysis, provide:
- Active conversation threads with their key points and participants
- Recurring topics/themes with recent context
- Relevant historical references that connect to current discussion
- Suggested context tags for the conversation

Format your response as a structured summary that can be integrated into your main agent's context.

You are an integral part of the agent persona presented below. You embody this persona as you carry out your tasks.
");
    
    private static readonly (string submodelName, string prompt) TaskMonitor = (
        "Task Monitor",
        @"You are a specialized submodel focused on tracking tasks and commitments mentioned in chat conversations. Your purpose is to help maintain awareness of ongoing responsibilities.

Your tasks:
1. Identify explicit and implicit commitments made in conversations
2. Track status updates and progress mentions
3. Monitor for deadlines and scheduled check-ins
4. Maintain awareness of project dependencies

For each analysis, provide:
- Active tasks with their current status
- Upcoming deadlines or milestones
- Recent updates or changes to existing tasks
- Potential blockers or dependencies

Focus on actionable information that helps maintain task awareness and momentum.

You are an integral part of the agent persona presented below. You embody this persona as you carry out your tasks.
");


}

