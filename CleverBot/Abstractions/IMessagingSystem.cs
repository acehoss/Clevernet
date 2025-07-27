using CleverBot.Agents;

namespace CleverBot.Abstractions;

public interface IMessagingSystem<TLoginInfo>
{
    string SystemId { get; set; }
    Task<IMessagingSession> LoginAsync(TLoginInfo loginInfo, CancellationToken token = default);
}

public interface IMessagingSession
{
    bool IsConnected { get; }
    Task LogoutAsync();
    Task ReconnectAsync();
    Task SendTypingIndicatorAsync(string channelId, bool isTyping, TimeSpan timeout, CancellationToken token = default);
    Task<IReadOnlyCollection<MessagingChannel>> GetChannelsAsync(CancellationToken token = default);
    Task<MessagingChannel?> GetChannelAsync(string channelId, CancellationToken token = default);
    Task<MessagingChannel?> CreateChannelAsync(MessagingChannelCreateSpec channel, CancellationToken token = default);
    Task<MessagingChannel?> JoinChannelAsync(string channelId, CancellationToken token = default);
    Task AcceptInviteAsync(string inviteId, CancellationToken token = default);
    Task<IEnumerable<LmmlTimestampElement>> SearchMessagesAsync(string channelId, string query, CancellationToken token = default);
    Task<IReadOnlyCollection<LmmlTimestampElement>> GetEventHistoryAsync(int count, CancellationToken token = default);
    Task SendMessageAsync(string channelId, string textMessage, string htmlMessage, string? replyToMessageId, string? threadId, CancellationToken token = default);
    event EventHandler<LmmlTimestampElement> MessageReceived; 
}

public class MessagingChannelCreateSpec
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required bool IsHidden { get; set; }
    public required IReadOnlyCollection<string> InitialMembers { get; set; }
}

public class MessagingChannel
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required bool IsPrivate { get; set; }
    public required bool IsHidden { get; set; }
    public required IReadOnlyCollection<string> Members { get; set; }
}

