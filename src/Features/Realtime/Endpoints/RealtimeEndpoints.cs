namespace EasyWorkTogether.Api.Endpoints;

public static class RealtimeEndpoints
{
    private static readonly JsonSerializerOptions SocketJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static void MapRealtimeEndpoints(this WebApplication app)
    {
        app.Map("/ws", HandleSocketAsync);
    }

    private static async Task HandleSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var realtime = context.RequestServices.GetRequiredService<RealtimeConnectionManager>();
        var chatService = context.RequestServices.GetRequiredService<ChatService>();

        var ticketText = context.Request.Query["ticket"].ToString();
        if (string.IsNullOrWhiteSpace(ticketText))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await using var conn = await context.RequestServices.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(context.RequestAborted);
        await using var tx = await conn.BeginTransactionAsync(context.RequestAborted);
        const string ticketSql = """
            SELECT wt.user_id
            FROM ws_tickets wt
            WHERE wt.ticket = @ticket AND wt.used_at IS NULL AND wt.expires_at > NOW()
            FOR UPDATE;
            """;
        await using var ticketCmd = new NpgsqlCommand(ticketSql, conn, tx);
        ticketCmd.Parameters.AddWithValue("ticket", ticketText);
        var userIdValue = await ticketCmd.ExecuteScalarAsync(context.RequestAborted);
        if (userIdValue is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var userId = Convert.ToInt32(userIdValue);
        await using (var consumeCmd = new NpgsqlCommand("UPDATE ws_tickets SET used_at = NOW() WHERE ticket = @ticket;", conn, tx))
        {
            consumeCmd.Parameters.AddWithValue("ticket", ticketText);
            await consumeCmd.ExecuteNonQueryAsync(context.RequestAborted);
        }
        await tx.CommitAsync(context.RequestAborted);

        SessionUser user;
        await using var userCmd = new NpgsqlCommand("SELECT id, email, name, created_at, avatar_url, public_user_id FROM users WHERE id = @id;", conn);
        userCmd.Parameters.AddWithValue("id", userId);
        await using var reader = await userCmd.ExecuteReaderAsync(context.RequestAborted);
        if (!await reader.ReadAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        user = new SessionUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : FormatPublicUserId(reader.GetString(5)));

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await realtime.AddConnectionAsync(user.Id, socket);
        await realtime.BroadcastAsync("user_online", new Dictionary<string, object?>
        {
            ["user_id"] = user.Id
        }, id => id != user.Id, context.RequestAborted);

        try
        {
            while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var incoming = await ReceiveStringAsync(socket, context.RequestAborted);
                if (incoming is null)
                    break;

                await ProcessEnvelopeAsync(user, incoming, socket, realtime, chatService, context.RequestAborted);
            }
        }
        finally
        {
            await realtime.RemoveConnectionAsync(user.Id, socket);
            await realtime.BroadcastAsync("user_offline", new Dictionary<string, object?>
            {
                ["user_id"] = user.Id
            }, id => id != user.Id, CancellationToken.None);

            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch
            {
                // ignore close failures
            }
        }
    }

    private static async Task ProcessEnvelopeAsync(
        SessionUser currentUser,
        string rawMessage,
        WebSocket socket,
        RealtimeConnectionManager realtime,
        ChatService chatService,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            if (!document.RootElement.TryGetProperty("event", out var eventProperty))
            {
                await SendSocketAsync(socket, "error", new MessageResponse("Missing event name"), cancellationToken);
                return;
            }

            var eventName = eventProperty.GetString()?.Trim().ToLowerInvariant();
            var data = document.RootElement.TryGetProperty("data", out var payloadProperty)
                ? payloadProperty
                : default;

            switch (eventName)
            {
                case "ping":
                    await SendSocketAsync(socket, "pong", new { timestamp = ToIsoString(DateTime.UtcNow) }, cancellationToken);
                    break;

                case "send_message":
                {
                    var payload = data.ValueKind == JsonValueKind.Undefined
                        ? null
                        : JsonSerializer.Deserialize<SendMessageSocketPayload>(data.GetRawText(), SocketJsonOptions);

                    if (payload is null)
                    {
                        await SendSocketAsync(socket, "error", new MessageResponse("Invalid send_message payload"), cancellationToken);
                        return;
                    }

                    var message = await chatService.CreateMessageAsync(currentUser, payload, cancellationToken);
                    await realtime.SendToUserAsync(currentUser.Id, "message_sent", new MessageSentSocketPayload(payload.ClientMessageId, message), cancellationToken);
                    await realtime.SendToUserAsync(payload.ReceiverId, "receive_message", message with { IsMine = false }, cancellationToken);
                    break;
                }

                case "read_conversation":
                {
                    if (data.ValueKind == JsonValueKind.Undefined || !data.TryGetProperty("user_id", out var userIdProperty) || !userIdProperty.TryGetInt32(out var otherUserId))
                    {
                        await SendSocketAsync(socket, "error", new MessageResponse("Invalid read_conversation payload"), cancellationToken);
                        return;
                    }

                    await chatService.MarkConversationReadAsync(currentUser.Id, otherUserId, cancellationToken);
                    await SendSocketAsync(socket, "conversation_read", new Dictionary<string, object?>
                    {
                        ["user_id"] = otherUserId
                    }, cancellationToken);
                    break;
                }

                default:
                    await SendSocketAsync(socket, "error", new MessageResponse($"Unsupported event: {eventName}"), cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendSocketAsync(socket, "error", new MessageResponse(ex.Message), cancellationToken);
        }
    }

    private static async Task<string?> ReceiveStringAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task SendSocketAsync(WebSocket socket, string eventName, object? data, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
            return;

        var payload = JsonSerializer.Serialize(new WebSocketEnvelope(eventName, data), SocketJsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
