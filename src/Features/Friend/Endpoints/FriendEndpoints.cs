using EasyWorkTogether.Api.Shared.Infrastructure.Auth;

namespace EasyWorkTogether.Api.Endpoints;

public static class FriendEndpoints
{
    public static void MapFriendEndpoints(this IEndpointRouteBuilder app)
    {
        var authApi = app.MapGroup("/api/friends");
        authApi.AddEndpointFilter<RequireSessionFilter>();

        authApi.MapGet("", ListFriendsAsync);
        authApi.MapGet("/search", SearchUsersAsync);
        authApi.MapPost("/requests", SendFriendRequestAsync);
        authApi.MapPost("/requests/{id:int}/accept", AcceptFriendRequestAsync);
        authApi.MapPost("/requests/{id:int}/decline", DeclineFriendRequestAsync);
        authApi.MapPost("/requests/{id:int}/cancel", CancelFriendRequestAsync);
    }

    private static async Task<IResult> ListFriendsAsync(HttpContext http, NpgsqlDataSource db, RealtimeConnectionManager realtime)
    {
        var currentUser = http.GetCurrentUser();
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);

        var friends = await ListFriendUsersAsync(conn, currentUser.Id, realtime, http.RequestAborted);
        var pending = await ListPendingRequestsAsync(conn, currentUser.Id, realtime, http.RequestAborted);
        var suggestions = await ListSuggestionsAsync(conn, currentUser.Id, realtime, http.RequestAborted);

        return Results.Ok(new FriendListResponse(
            friends,
            pending.Where(item => item.Direction == "incoming").ToList(),
            pending.Where(item => item.Direction == "outgoing").ToList(),
            suggestions));
    }

    private static async Task<IResult> SearchUsersAsync(HttpContext http, string q, NpgsqlDataSource db, RealtimeConnectionManager realtime)
    {
        var currentUser = http.GetCurrentUser();
        var normalized = NormalizePublicUserId(q);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 3)
            return Results.Ok(new FriendSearchResponse(new List<FriendSearchResultResponse>()));

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        const string sql = """
            WITH relationship AS (
                SELECT f.friend_user_id AS target_id, 'friend'::text AS relationship
                FROM friendships f
                WHERE f.user_id = @user_id
                UNION ALL
                SELECT fr.receiver_id AS target_id, 'pending_outgoing'::text AS relationship
                FROM friend_requests fr
                WHERE fr.requester_id = @user_id AND fr.status = 'pending'
                UNION ALL
                SELECT fr.requester_id AS target_id, 'pending_incoming'::text AS relationship
                FROM friend_requests fr
                WHERE fr.receiver_id = @user_id AND fr.status = 'pending'
            )
            SELECT u.id, u.name, u.public_user_id, u.avatar_url, COALESCE(r.relationship, 'none') AS relationship
            FROM users u
            LEFT JOIN relationship r ON r.target_id = u.id
            WHERE u.id <> @user_id
              AND u.public_user_id IS NOT NULL
              AND (LOWER(u.public_user_id) = @normalized OR LOWER(u.public_user_id) LIKE @partial)
            ORDER BY CASE WHEN LOWER(u.public_user_id) = @normalized THEN 0 ELSE 1 END,
                     LOWER(u.public_user_id),
                     LOWER(u.name)
            LIMIT 12;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUser.Id);
        cmd.Parameters.AddWithValue("normalized", normalized);
        cmd.Parameters.AddWithValue("partial", $"{normalized}%");

        var results = new List<FriendSearchResultResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
        while (await reader.ReadAsync(http.RequestAborted))
        {
            var relationship = reader.GetString(4);
            results.Add(new FriendSearchResultResponse(
                new FriendUserResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    null,
                    realtime.IsUserOnline(reader.GetInt32(0)),
                    relationship,
                    null,
                    null,
                    reader.IsDBNull(2) ? null : FormatPublicUserId(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetString(3)),
                relationship));
        }

        return Results.Ok(new FriendSearchResponse(results));
    }

    private static async Task<IResult> SendFriendRequestAsync(HttpContext http, SendFriendRequestRequest request, NpgsqlDataSource db, NotificationService notifications)
    {
        var currentUser = http.GetCurrentUser();
        if (request.ReceiverId == currentUser.Id)
            return Results.BadRequest(new ErrorResponse("Bạn không thể tự kết bạn với chính mình."));

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        await using var tx = await conn.BeginTransactionAsync(http.RequestAborted);

        if (!await UserExistsAsync(conn, tx, request.ReceiverId, http.RequestAborted))
            return Results.NotFound(new ErrorResponse("Không tìm thấy người dùng."));

        if (await FriendshipExistsAsync(conn, tx, currentUser.Id, request.ReceiverId, http.RequestAborted))
            return Results.BadRequest(new ErrorResponse("Hai người đã là bạn bè."));

        var existingPendingId = await GetPendingRequestIdAsync(conn, tx, currentUser.Id, request.ReceiverId, http.RequestAborted);
        if (existingPendingId.HasValue)
            return Results.BadRequest(new ErrorResponse("Đã có lời mời kết bạn đang chờ xử lý."));

        const string sql = """
            INSERT INTO friend_requests (requester_id, receiver_id, status)
            VALUES (@requester_id, @receiver_id, 'pending')
            RETURNING id, created_at;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("requester_id", currentUser.Id);
        cmd.Parameters.AddWithValue("receiver_id", request.ReceiverId);

        int requestId;
        DateTime createdAt;
        await using (var reader = await cmd.ExecuteReaderAsync(http.RequestAborted))
        {
            await reader.ReadAsync(http.RequestAborted);
            requestId = reader.GetInt32(0);
            createdAt = reader.GetDateTime(1);
        }

        await tx.CommitAsync(http.RequestAborted);

        await notifications.CreateAsync(
            request.ReceiverId,
            "friend_request",
            "New friend request",
            $"{currentUser.Name} wants to connect with you.",
            currentUser.Id,
            "friend_request",
            requestId,
            new Dictionary<string, string?>
            {
                ["request_id"] = requestId.ToString(),
                ["requester_id"] = currentUser.Id.ToString()
            },
            $"friend-request:{requestId}",
            http.RequestAborted);

        return Results.Ok(new MessageResponse($"Friend request sent at {ToIsoString(createdAt)}"));
    }

    private static async Task<IResult> AcceptFriendRequestAsync(HttpContext http, int id, NpgsqlDataSource db, NotificationService notifications)
    {
        var currentUser = http.GetCurrentUser();
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        await using var tx = await conn.BeginTransactionAsync(http.RequestAborted);

        const string lockSql = """
            SELECT requester_id, receiver_id, status
            FROM friend_requests
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var cmd = new NpgsqlCommand(lockSql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);

        int requesterId;
        int receiverId;
        string status;
        await using (var reader = await cmd.ExecuteReaderAsync(http.RequestAborted))
        {
            if (!await reader.ReadAsync(http.RequestAborted))
                return Results.NotFound(new ErrorResponse("Friend request not found"));

            requesterId = reader.GetInt32(0);
            receiverId = reader.GetInt32(1);
            status = reader.GetString(2);
        }

        if (receiverId != currentUser.Id)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (status != "pending")
            return Results.BadRequest(new ErrorResponse("Friend request is no longer pending"));

        const string friendshipSql = """
            INSERT INTO friendships (user_id, friend_user_id)
            VALUES (@left_user_id, @right_user_id), (@right_user_id, @left_user_id)
            ON CONFLICT DO NOTHING;
            """;

        await using (var friendshipCmd = new NpgsqlCommand(friendshipSql, conn, tx))
        {
            friendshipCmd.Parameters.AddWithValue("left_user_id", requesterId);
            friendshipCmd.Parameters.AddWithValue("right_user_id", receiverId);
            await friendshipCmd.ExecuteNonQueryAsync(http.RequestAborted);
        }

        await using (var updateCmd = new NpgsqlCommand("UPDATE friend_requests SET status = 'accepted', responded_at = COALESCE(responded_at, NOW()) WHERE id = @id;", conn, tx))
        {
            updateCmd.Parameters.AddWithValue("id", id);
            await updateCmd.ExecuteNonQueryAsync(http.RequestAborted);
        }

        await tx.CommitAsync(http.RequestAborted);

        await notifications.CreateAsync(
            requesterId,
            "friend_request_accepted",
            "Friend request accepted",
            $"{currentUser.Name} accepted your connection request.",
            currentUser.Id,
            "friend_user",
            currentUser.Id,
            new Dictionary<string, string?>
            {
                ["friend_user_id"] = currentUser.Id.ToString(),
                ["request_id"] = id.ToString()
            },
            $"friend-request-accepted:{id}",
            http.RequestAborted);

        return Results.Ok(new MessageResponse("Friend request accepted"));
    }

    private static async Task<IResult> DeclineFriendRequestAsync(HttpContext http, int id, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        const string sql = """
            UPDATE friend_requests
            SET status = 'declined',
                responded_at = COALESCE(responded_at, NOW())
            WHERE id = @id
              AND receiver_id = @receiver_id
              AND status = 'pending';
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("receiver_id", currentUser.Id);
        var affected = await cmd.ExecuteNonQueryAsync(http.RequestAborted);
        if (affected == 0)
            return Results.NotFound(new ErrorResponse("Friend request not found or already handled"));

        return Results.Ok(new MessageResponse("Friend request declined"));
    }

    private static async Task<IResult> CancelFriendRequestAsync(HttpContext http, int id, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        const string sql = """
            UPDATE friend_requests
            SET status = 'declined',
                responded_at = COALESCE(responded_at, NOW())
            WHERE id = @id
              AND requester_id = @requester_id
              AND status = 'pending';
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("requester_id", currentUser.Id);
        var affected = await cmd.ExecuteNonQueryAsync(http.RequestAborted);
        if (affected == 0)
            return Results.NotFound(new ErrorResponse("Friend request not found or already handled"));

        return Results.Ok(new MessageResponse("Friend request cancelled"));
    }

    private static async Task<List<FriendUserResponse>> ListFriendUsersAsync(NpgsqlConnection conn, int currentUserId, RealtimeConnectionManager realtime, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.id,
                   u.name,
                   u.email,
                   u.public_user_id,
                   u.avatar_url,
                   last_msg.content,
                   last_msg.type,
                   last_msg.created_at
            FROM friendships f
            JOIN users u ON u.id = f.friend_user_id
            LEFT JOIN LATERAL (
                SELECT m.content, m.type, m.created_at
                FROM chat_messages m
                WHERE (m.sender_id = @user_id AND m.receiver_id = u.id)
                   OR (m.sender_id = u.id AND m.receiver_id = @user_id)
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT 1
            ) last_msg ON TRUE
            WHERE f.user_id = @user_id
            ORDER BY LOWER(u.name), u.id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUserId);

        var items = new List<FriendUserResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FriendUserResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                null,
                realtime.IsUserOnline(reader.GetInt32(0)),
                "friend",
                reader.IsDBNull(5) && reader.IsDBNull(6)
                    ? null
                    : BuildMessagePreview(reader.IsDBNull(6) ? "text" : reader.GetString(6), reader.IsDBNull(5) ? null : reader.GetString(5)),
                reader.IsDBNull(7) ? null : ToIsoString(reader.GetDateTime(7)),
                reader.IsDBNull(3) ? null : FormatPublicUserId(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4))));
        }

        return items;
    }

    private static async Task<List<FriendRequestResponse>> ListPendingRequestsAsync(NpgsqlConnection conn, int currentUserId, RealtimeConnectionManager realtime, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fr.id,
                   fr.requester_id,
                   fr.receiver_id,
                   fr.status,
                   fr.created_at,
                   fr.responded_at,
                   u.id,
                   u.name,
                   u.email,
                   u.public_user_id,
                   u.avatar_url
            FROM friend_requests fr
            JOIN users u ON u.id = CASE
                                      WHEN fr.requester_id = @user_id THEN fr.receiver_id
                                      ELSE fr.requester_id
                                  END
            WHERE fr.status = 'pending'
              AND (fr.requester_id = @user_id OR fr.receiver_id = @user_id)
            ORDER BY fr.created_at DESC, fr.id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUserId);

        var items = new List<FriendRequestResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = reader.GetInt32(2) == currentUserId ? "incoming" : "outgoing";
            items.Add(new FriendRequestResponse(
                reader.GetInt32(0),
                new FriendUserResponse(
                    reader.GetInt32(6),
                    reader.GetString(7),
                    null,
                    realtime.IsUserOnline(reader.GetInt32(6)),
                    direction == "incoming" ? "pending_incoming" : "pending_outgoing",
                    null,
                    null,
                    reader.IsDBNull(9) ? null : FormatPublicUserId(reader.GetString(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10)),
                direction,
                reader.GetString(3),
                ToIsoString(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : ToIsoString(reader.GetDateTime(5))));
        }

        return items;
    }

    private static async Task<List<FriendUserResponse>> ListSuggestionsAsync(NpgsqlConnection conn, int currentUserId, RealtimeConnectionManager realtime, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT ON (u.id) u.id, u.name, u.email, u.public_user_id, u.avatar_url
            FROM workspace_members self_member
            JOIN workspace_members teammate_member ON teammate_member.workspace_id = self_member.workspace_id
                                                AND teammate_member.user_id <> self_member.user_id
            JOIN users u ON u.id = teammate_member.user_id
            LEFT JOIN friendships f ON f.user_id = @user_id AND f.friend_user_id = u.id
            LEFT JOIN friend_requests fr ON ((fr.requester_id = @user_id AND fr.receiver_id = u.id)
                                          OR (fr.requester_id = u.id AND fr.receiver_id = @user_id))
                                         AND fr.status = 'pending'
            WHERE self_member.user_id = @user_id
              AND f.user_id IS NULL
              AND fr.id IS NULL
            ORDER BY u.id, LOWER(u.name)
            LIMIT 8;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("user_id", currentUserId);

        var items = new List<FriendUserResponse>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FriendUserResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                null,
                realtime.IsUserOnline(reader.GetInt32(0)),
                "suggested",
                null,
                null,
                reader.IsDBNull(3) ? null : FormatPublicUserId(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4))));
        }

        return items;
    }

    private static async Task<bool> UserExistsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int userId, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", userId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<bool> FriendshipExistsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int leftUserId, int rightUserId, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM friendships WHERE user_id = @left_user_id AND friend_user_id = @right_user_id;", conn, tx);
        cmd.Parameters.AddWithValue("left_user_id", leftUserId);
        cmd.Parameters.AddWithValue("right_user_id", rightUserId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<int?> GetPendingRequestIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int leftUserId, int rightUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id
            FROM friend_requests
            WHERE status = 'pending'
              AND ((requester_id = @left_user_id AND receiver_id = @right_user_id)
                OR (requester_id = @right_user_id AND receiver_id = @left_user_id))
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("left_user_id", leftUserId);
        cmd.Parameters.AddWithValue("right_user_id", rightUserId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : Convert.ToInt32(result);
    }

    private static string BuildMessagePreview(string type, string? content)
    {
        return type switch
        {
            "image" => "[Image]",
            "file" => "[File]",
            _ => string.IsNullOrWhiteSpace(content) ? string.Empty : content.Trim()
        };
    }
}
