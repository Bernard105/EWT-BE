namespace EasyWorkTogether.Api.Services;

public sealed class ChatService
{
    private readonly NpgsqlDataSource _db;
    private readonly NotificationService _notifications;
    private readonly RealtimeConnectionManager _realtime;

    public ChatService(NpgsqlDataSource db, NotificationService notifications, RealtimeConnectionManager realtime)
    {
        _db = db;
        _notifications = notifications;
        _realtime = realtime;
    }

    public async Task<ChatConversationListResponse> ListConversationsAsync(SessionUser currentUser, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        const string sql = """
            WITH partners AS (
                SELECT CASE
                           WHEN sender_id = @user_id THEN receiver_id
                           ELSE sender_id
                       END AS partner_id,
                       MAX(created_at) AS last_message_at
                FROM chat_messages
                WHERE sender_id = @user_id OR receiver_id = @user_id
                GROUP BY 1
            )
            SELECT u.id,
                   u.name,
                   u.email,
                   u.public_user_id,
                   u.avatar_url,
                   last_msg.id,
                   last_msg.sender_id,
                   last_msg.receiver_id,
                   COALESCE(last_msg.content, ''),
                   last_msg.type,
                   last_msg.created_at,
                   asset.id,
                   asset.public_id,
                   asset.original_file_name,
                   asset.content_type,
                   asset.size_bytes,
                   COALESCE(unread.unread_count, 0) AS unread_count
            FROM partners p
            JOIN users u ON u.id = p.partner_id
            LEFT JOIN LATERAL (
                SELECT m.id, m.sender_id, m.receiver_id, m.content, m.type, m.created_at, m.file_asset_id
                FROM chat_messages m
                WHERE (m.sender_id = @user_id AND m.receiver_id = p.partner_id)
                   OR (m.sender_id = p.partner_id AND m.receiver_id = @user_id)
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT 1
            ) last_msg ON TRUE
            LEFT JOIN file_assets asset ON asset.id = last_msg.file_asset_id
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::INT AS unread_count
                FROM chat_messages cm
                WHERE cm.sender_id = p.partner_id
                  AND cm.receiver_id = @user_id
                  AND cm.read_at IS NULL
            ) unread ON TRUE
            ORDER BY p.last_message_at DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUser.Id);

        var items = new List<ChatConversationResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var user = new FriendUserResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                null,
                _realtime.IsUserOnline(reader.GetInt32(0)),
                "friend",
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(10) ? null : ToIsoString(reader.GetDateTime(10)),
                reader.IsDBNull(3) ? null : FormatPublicUserId(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4));

            var lastMessage = reader.IsDBNull(5)
                ? null
                : new ChatMessageResponse(
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    reader.GetString(9),
                    ToIsoString(reader.GetDateTime(10)),
                    reader.IsDBNull(11) ? null : new FileAssetResponse(
                        reader.GetInt32(11),
                        reader.GetGuid(12).ToString(),
                        reader.GetString(13),
                        reader.GetString(14),
                        reader.GetInt64(15),
                        $"/api/files/{reader.GetGuid(12)}",
                        $"/api/files/{reader.GetGuid(12)}?download=1",
                        reader.GetString(14).StartsWith("image/", StringComparison.OrdinalIgnoreCase)),
                    reader.GetInt32(6) == currentUser.Id);

            items.Add(new ChatConversationResponse(user, lastMessage, reader.GetInt32(16)));
        }

        return new ChatConversationListResponse(items);
    }

    public async Task<ChatMessageListResponse> ListMessagesAsync(SessionUser currentUser, int otherUserId, int limit = 80, CancellationToken cancellationToken = default)
    {
        await EnsureFriendshipAsync(currentUser.Id, otherUserId, cancellationToken);
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT m.id,
                   m.sender_id,
                   m.receiver_id,
                   COALESCE(m.content, ''),
                   m.type,
                   m.created_at,
                   asset.id,
                   asset.public_id,
                   asset.original_file_name,
                   asset.content_type,
                   asset.size_bytes
            FROM chat_messages m
            LEFT JOIN file_assets asset ON asset.id = m.file_asset_id
            WHERE (m.sender_id = @user_id AND m.receiver_id = @other_user_id)
               OR (m.sender_id = @other_user_id AND m.receiver_id = @user_id)
            ORDER BY m.created_at DESC, m.id DESC
            LIMIT @take;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUser.Id);
        cmd.Parameters.AddWithValue("other_user_id", otherUserId);
        cmd.Parameters.AddWithValue("take", Math.Clamp(limit, 1, 200));

        var messages = new List<ChatMessageResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatMessageResponse(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                ToIsoString(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : new FileAssetResponse(
                    reader.GetInt32(6),
                    reader.GetGuid(7).ToString(),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetInt64(10),
                    $"/api/files/{reader.GetGuid(7)}",
                    $"/api/files/{reader.GetGuid(7)}?download=1",
                    reader.GetString(9).StartsWith("image/", StringComparison.OrdinalIgnoreCase)),
                reader.GetInt32(1) == currentUser.Id));
        }

        messages.Reverse();
        return new ChatMessageListResponse(messages);
    }

    public async Task<ChatMessageResponse> CreateMessageAsync(SessionUser currentUser, SendMessageSocketPayload payload, CancellationToken cancellationToken = default)
    {
        if (payload.ReceiverId == currentUser.Id)
            throw new InvalidOperationException("Bạn không thể nhắn cho chính mình.");

        await EnsureFriendshipAsync(currentUser.Id, payload.ReceiverId, cancellationToken);

        var normalizedType = string.IsNullOrWhiteSpace(payload.Type) ? "text" : payload.Type.Trim().ToLowerInvariant();
        if (normalizedType is not ("text" or "image" or "file"))
            throw new InvalidOperationException("Loại tin nhắn không hợp lệ.");

        var content = string.IsNullOrWhiteSpace(payload.Content) ? null : payload.Content.Trim();
        if (normalizedType == "text" && string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Tin nhắn không được để trống.");

        int? fileAssetId = null;
        FileAssetResponse? attachment = null;

        if (!string.IsNullOrWhiteSpace(payload.FilePublicId))
        {
            (fileAssetId, attachment) = await LoadFileAssetForUserAsync(currentUser.Id, payload.FilePublicId!, cancellationToken);
            if (normalizedType == "text")
                normalizedType = attachment!.IsImage ? "image" : "file";
        }

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO chat_messages (sender_id, receiver_id, content, type, file_asset_id)
            VALUES (@sender_id, @receiver_id, @content, @type, @file_asset_id)
            RETURNING id, created_at;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sender_id", currentUser.Id);
        cmd.Parameters.AddWithValue("receiver_id", payload.ReceiverId);
        cmd.Parameters.AddWithValue("content", (object?)content ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", normalizedType);
        cmd.Parameters.AddWithValue("file_asset_id", fileAssetId.HasValue ? fileAssetId.Value : (object)DBNull.Value);

        int messageId;
        DateTime createdAt;

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            messageId = reader.GetInt32(0);
            createdAt = reader.GetDateTime(1);
        }

        var response = new ChatMessageResponse(
            messageId,
            currentUser.Id,
            payload.ReceiverId,
            content ?? string.Empty,
            normalizedType,
            ToIsoString(createdAt),
            attachment,
            true);

        var summaryText = normalizedType switch
        {
            "image" => $"{currentUser.Name} đã gửi cho bạn một ảnh.",
            "file" => $"{currentUser.Name} đã gửi cho bạn một tệp.",
            _ => $"{currentUser.Name}: {Truncate(content ?? string.Empty, 80)}"
        };

        await _notifications.CreateAsync(
            payload.ReceiverId,
            "chat_message",
            currentUser.Name,
            summaryText,
            currentUser.Id,
            "chat_user",
            currentUser.Id,
            new Dictionary<string, string?>
            {
                ["sender_id"] = currentUser.Id.ToString(),
                ["receiver_id"] = payload.ReceiverId.ToString(),
                ["message_id"] = messageId.ToString()
            },
            $"chat-message:{messageId}",
            cancellationToken);

        return response;
    }

    public async Task MarkConversationReadAsync(int currentUserId, int otherUserId, CancellationToken cancellationToken = default)
    {
        await EnsureFriendshipAsync(currentUserId, otherUserId, cancellationToken);

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            UPDATE chat_messages
            SET read_at = COALESCE(read_at, NOW())
            WHERE sender_id = @other_user_id
              AND receiver_id = @user_id
              AND read_at IS NULL;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUserId);
        cmd.Parameters.AddWithValue("other_user_id", otherUserId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await _notifications.MarkChatNotificationsAsReadAsync(currentUserId, otherUserId, cancellationToken);
    }

    public async Task EnsureFriendshipAsync(int currentUserId, int otherUserId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT 1
            FROM friendships
            WHERE user_id = @user_id
              AND friend_user_id = @other_user_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUserId);
        cmd.Parameters.AddWithValue("other_user_id", otherUserId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null)
            throw new InvalidOperationException("Bạn chỉ có thể chat với người đã kết bạn.");
    }

    private async Task<(int, FileAssetResponse)> LoadFileAssetForUserAsync(int currentUserId, string publicId, CancellationToken cancellationToken)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT id, public_id, original_file_name, content_type, size_bytes
            FROM file_assets
            WHERE public_id = @public_id
              AND created_by = @created_by;
            """;

        if (!Guid.TryParse(publicId, out var parsedPublicId))
            throw new InvalidOperationException("Mã tệp đính kèm không hợp lệ.");

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("public_id", parsedPublicId);
        cmd.Parameters.AddWithValue("created_by", currentUserId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy tệp đính kèm để gửi.");

        var asset = new FileAssetResponse(
            reader.GetInt32(0),
            reader.GetGuid(1).ToString(),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            $"/api/files/{reader.GetGuid(1)}",
            $"/api/files/{reader.GetGuid(1)}?download=1",
            reader.GetString(3).StartsWith("image/", StringComparison.OrdinalIgnoreCase));

        return (reader.GetInt32(0), asset);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 1)] + "…";
    }
}
