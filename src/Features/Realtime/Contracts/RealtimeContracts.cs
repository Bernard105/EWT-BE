namespace EasyWorkTogether.Api.Models;

public record WebSocketEnvelope(string Event, object? Data);

public record SendMessageSocketPayload(int ReceiverId, string? Content, string Type, string? FilePublicId, string? ClientMessageId);

public record MessageSentSocketPayload(string? ClientMessageId, ChatMessageResponse Message);
