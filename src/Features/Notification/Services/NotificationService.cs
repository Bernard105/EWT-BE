namespace EasyWorkTogether.Api.Services;

public sealed class NotificationService
{
    private readonly NpgsqlDataSource _db;
    private readonly RealtimeConnectionManager _realtime;

    public NotificationService(NpgsqlDataSource db, RealtimeConnectionManager realtime)
    {
        _db = db;
        _realtime = realtime;
    }

    public async Task<NotificationResponse?> CreateAsync(
        int userId,
        string type,
        string title,
        string message,
        int? actorUserId = null,
        string? entityType = null,
        int? entityId = null,
        Dictionary<string, string?>? data = null,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        const string sql = """
            INSERT INTO notifications (user_id, actor_user_id, type, title, message, entity_type, entity_id, data_json, dedupe_key)
            VALUES (@user_id, @actor_user_id, @type, @title, @message, @entity_type, @entity_id, @data_json, @dedupe_key)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("actor_user_id", actorUserId.HasValue ? actorUserId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("type", type);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("message", message);
        cmd.Parameters.AddWithValue("entity_type", (object?)entityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("entity_id", entityId.HasValue ? entityId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("data_json", data is null ? (object)DBNull.Value : JsonSerializer.Serialize(data));
        cmd.Parameters.AddWithValue("dedupe_key", string.IsNullOrWhiteSpace(dedupeKey) ? (object)DBNull.Value : dedupeKey.Trim());

        var notificationId = await cmd.ExecuteScalarAsync(cancellationToken);
        if (notificationId is null)
            return null;

        var notification = await GetByIdAsync(Convert.ToInt32(notificationId), cancellationToken);
        if (notification is not null)
        {
            await _realtime.SendToUserAsync(userId, "notification_created", notification, cancellationToken);
        }

        return notification;
    }

    public async Task<NotificationListResponse> ListAsync(int userId, bool unreadOnly = false, int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);

        var sql = """
            SELECT n.id,
                   n.type,
                   n.title,
                   n.message,
                   n.is_read,
                   n.created_at,
                   n.read_at,
                   n.entity_type,
                   n.entity_id,
                   n.data_json,
                   actor.id AS actor_id,
                   actor.name AS actor_name
            FROM notifications n
            LEFT JOIN users actor ON actor.id = n.actor_user_id
            WHERE n.user_id = @user_id
            """;

        if (unreadOnly)
            sql += "\nAND n.is_read = FALSE";

        sql += "\nORDER BY n.created_at DESC\nLIMIT @take;";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("take", Math.Clamp(limit, 1, 200));

        var items = new List<NotificationResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapNotification(reader));
        }

        return new NotificationListResponse(items, await GetUnreadCountAsync(userId, cancellationToken));
    }

    public async Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT COUNT(*)::INT
            FROM notifications
            WHERE user_id = @user_id AND is_read = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", userId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null ? 0 : Convert.ToInt32(result);
    }

    public async Task MarkAsReadAsync(int userId, IEnumerable<int>? notificationIds = null, CancellationToken cancellationToken = default)
    {
        var ids = notificationIds?.Distinct().ToArray() ?? [];

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        var updatedIds = new List<int>();

        if (ids.Length == 0)
        {
            const string sql = """
                UPDATE notifications
                SET is_read = TRUE,
                    read_at = COALESCE(read_at, NOW())
                WHERE user_id = @user_id
                  AND is_read = FALSE
                RETURNING id;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("user_id", userId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                updatedIds.Add(reader.GetInt32(0));
        }
        else
        {
            const string sql = """
                UPDATE notifications
                SET is_read = TRUE,
                    read_at = COALESCE(read_at, NOW())
                WHERE user_id = @user_id
                  AND id = ANY(@ids)
                  AND is_read = FALSE
                RETURNING id;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("user_id", userId);
            cmd.Parameters.Add("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = ids;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                updatedIds.Add(reader.GetInt32(0));
        }

        if (updatedIds.Count > 0)
        {
            await _realtime.SendToUserAsync(userId, "notifications_read", new Dictionary<string, object?>
            {
                ["notification_ids"] = updatedIds
            }, cancellationToken);
        }
    }

    public async Task MarkChatNotificationsAsReadAsync(int userId, int actorUserId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            UPDATE notifications
            SET is_read = TRUE,
                read_at = COALESCE(read_at, NOW())
            WHERE user_id = @user_id
              AND actor_user_id = @actor_user_id
              AND type = 'chat_message'
              AND is_read = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("actor_user_id", actorUserId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<NotificationResponse?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT n.id,
                   n.type,
                   n.title,
                   n.message,
                   n.is_read,
                   n.created_at,
                   n.read_at,
                   n.entity_type,
                   n.entity_id,
                   n.data_json,
                   actor.id AS actor_id,
                   actor.name AS actor_name
            FROM notifications n
            LEFT JOIN users actor ON actor.id = n.actor_user_id
            WHERE n.id = @id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", notificationId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapNotification(reader);
    }

    private static NotificationResponse MapNotification(NpgsqlDataReader reader)
    {
        Dictionary<string, string?>? data = null;
        if (!reader.IsDBNull(9))
        {
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, string?>>(reader.GetString(9));
            }
            catch
            {
                data = null;
            }
        }

        UserBasicResponse? actor = null;
        if (!reader.IsDBNull(10))
        {
            actor = new UserBasicResponse(reader.GetInt32(10), reader.GetString(11));
        }

        return new NotificationResponse(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            ToIsoString(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : ToIsoString(reader.GetDateTime(6)),
            actor,
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            data);
    }
}
