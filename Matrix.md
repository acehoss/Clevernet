# Matrix and LibMatrix Notes

## Authentication
- Matrix requires a homeserver URL, username, and password for authentication
- LibMatrix uses a two-step authentication process:
  1. Get a remote homeserver client and login to get an access token
  2. Create an authenticated client using that token

```csharp
var remoteHomeserver = await _homeserverProvider.GetRemoteHomeserver(homeServer, proxy: null, useCache: true, enableServer: true);
var loginResponse = await remoteHomeserver.LoginAsync(username, password, "CleverBot");
var authenticatedHomeserver = await _homeserverProvider.GetAuthenticatedWithToken(homeServer, loginResponse.AccessToken);
```

## Event Handling
- Matrix uses a sync-based model where the client receives events through sync calls
- LibMatrix provides a `SyncHelper` class to manage the sync loop
- Events are handled by registering handlers with the sync helper:
  - `TimelineEventHandlers` for room messages and events
  - `InviteReceivedHandlers` for room invites
  - `SyncReceivedHandlers` for raw sync responses

## Search Capabilities
- Matrix provides a powerful search API at `/_matrix/client/v3/search`
  - Supports full text search across different categories (room events, users, etc.)
  - Can search message content, room metadata, and more
  - Example endpoint:
    ```
    POST /_matrix/client/v3/search
    {
        "search_categories": {
            "room_events": {
                "search_term": "query",
                "keys": ["content.body"]
            }
        }
    }
    ```
- LibMatrix's current search capabilities:
  - `SynapseAdminApiClient.SearchRoomsAsync()` - Admin API for room searching
  - `UserDirectoryResponse` - User directory search
  - `LocalRoomQueryFilter` - Room filtering
  - Note: Full text search endpoint not yet implemented in LibMatrix

## Room Management
- Rooms can be used as shares/drives for organizing content
- Room access control is handled at the room level
- Room aliases follow the format: `#roomname:server.domain`
- Room operations:
  - Join: `JoinRoomAsync()`
  - Leave: `LeaveAsync()`
  - Send Message: `SendMessageEventAsync()`
  - Get Messages: `GetManyMessagesAsync()`

## Message Types
- Standard message types:
  - `m.text` - Plain text messages
  - `m.file` - File attachments
  - `m.image` - Image attachments
  - `m.audio` - Audio attachments
  - `m.video` - Video attachments
- Message format options:
  - Plain text (default)
  - `org.matrix.custom.html` - HTML formatting
- Message content is stored in the `Body` field and is searchable
- Additional metadata (like filenames) should be included in the `Body` for searchability

## Typing Notifications
- Typing notifications are sent as state events
- Example:
  ```csharp
  await room.SendStateEventAsync("m.typing", new { typing = true, timeout = 1000 });
  ```

## LibMatrix SDK Notes
- Uses a provider-based architecture for homeserver connections
- Provides strongly-typed event content classes
- Handles sync loop and event distribution
- Includes helper classes for message building and formatting
- Thread handling is done through message relations

## Best Practices
1. Always check for null on optional properties
2. Handle message relations carefully
3. Log event information for debugging
4. Set appropriate timeouts for typing notifications
5. Validate room membership before sending messages

## Common Gotchas
1. Thread messages need only the thread root reference, not reply chains
2. Message relations can be complex - check both RelationType and InReplyTo
3. Remember to stop typing indicators after sending messages
4. Check sender ID to avoid responding to your own messages

## Useful Debug Information
- Log message relations for debugging thread issues:
  ```csharp
  _logger.LogInformation("Message has relations. RelationType: {RelationType}, EventId: {EventId}, ReplyTo: {ReplyTo}", 
      messageContent.RelatesTo.RelationType,
      messageContent.RelatesTo.EventId,
      messageContent.RelatesTo.InReplyTo?.EventId);
  ``` 