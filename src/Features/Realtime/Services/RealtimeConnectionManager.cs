namespace EasyWorkTogether.Api.Services;

public sealed class RealtimeConnectionManager
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, WebSocket>> _connections = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public IReadOnlyCollection<int> GetOnlineUserIds() => _connections.Keys.ToArray();

    public bool IsUserOnline(int userId)
    {
        return _connections.TryGetValue(userId, out var sockets) && sockets.Any(static entry => entry.Value.State == WebSocketState.Open);
    }

    public Task AddConnectionAsync(int userId, WebSocket socket)
    {
        var bucket = _connections.GetOrAdd(userId, static _ => new ConcurrentDictionary<Guid, WebSocket>());
        bucket[Guid.NewGuid()] = socket;
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(int userId, WebSocket socket)
    {
        if (!_connections.TryGetValue(userId, out var bucket))
            return Task.CompletedTask;

        foreach (var entry in bucket)
        {
            if (!ReferenceEquals(entry.Value, socket))
                continue;

            bucket.TryRemove(entry.Key, out _);
        }

        if (bucket.IsEmpty)
            _connections.TryRemove(userId, out _);

        return Task.CompletedTask;
    }

    public async Task SendToUserAsync(int userId, string eventName, object? data, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(userId, out var bucket) || bucket.IsEmpty)
            return;

        var payload = JsonSerializer.Serialize(new WebSocketEnvelope(eventName, data), _jsonOptions);
        var buffer = Encoding.UTF8.GetBytes(payload);

        var deadConnections = new List<Guid>();

        foreach (var entry in bucket)
        {
            try
            {
                if (entry.Value.State != WebSocketState.Open)
                {
                    deadConnections.Add(entry.Key);
                    continue;
                }

                await entry.Value.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                deadConnections.Add(entry.Key);
            }
        }

        foreach (var deadConnection in deadConnections)
            bucket.TryRemove(deadConnection, out _);

        if (bucket.IsEmpty)
            _connections.TryRemove(userId, out _);
    }


    public async Task BroadcastToUsersAsync(IEnumerable<int> userIds, string eventName, object? data, CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds.Distinct())
        {
            await SendToUserAsync(userId, eventName, data, cancellationToken);
        }
    }

    public async Task BroadcastAsync(string eventName, object? data, Func<int, bool>? predicate = null, CancellationToken cancellationToken = default)
    {
        foreach (var userId in GetOnlineUserIds())
        {
            if (predicate is not null && !predicate(userId))
                continue;

            await SendToUserAsync(userId, eventName, data, cancellationToken);
        }
    }
}
