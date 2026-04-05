namespace EasyWorkTogether.Api.Models;

public record MarkNotificationsReadRequest(List<int>? NotificationIds);

public record NotificationResponse(
    int Id,
    string Type,
    string Title,
    string Message,
    bool IsRead,
    string CreatedAt,
    string? ReadAt,
    UserBasicResponse? Actor,
    string? EntityType,
    int? EntityId,
    Dictionary<string, string?>? Data);

public record NotificationListResponse(List<NotificationResponse> Notifications, int UnreadCount);
