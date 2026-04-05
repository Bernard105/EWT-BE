using EasyWorkTogether.Api.Shared.Infrastructure.Auth;

namespace EasyWorkTogether.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var authApi = app.MapGroup("/api/notifications");
        authApi.AddEndpointFilter<RequireSessionFilter>();

        authApi.MapGet("", ListNotificationsAsync);
        authApi.MapPost("/read", MarkNotificationsReadAsync);
        authApi.MapPost("/{id:int}/read", MarkNotificationReadAsync);
    }

    private static async Task<IResult> ListNotificationsAsync(HttpContext http, [FromQuery] string? filter, [FromQuery] int? limit, NotificationService notifications)
    {
        var currentUser = http.GetCurrentUser();
        var unreadOnly = string.Equals(filter, "unread", StringComparison.OrdinalIgnoreCase);
        var response = await notifications.ListAsync(currentUser.Id, unreadOnly, limit ?? 50, http.RequestAborted);
        return Results.Ok(response);
    }

    private static async Task<IResult> MarkNotificationsReadAsync(HttpContext http, MarkNotificationsReadRequest request, NotificationService notifications)
    {
        var currentUser = http.GetCurrentUser();
        await notifications.MarkAsReadAsync(currentUser.Id, request.NotificationIds, http.RequestAborted);
        return Results.Ok(new MessageResponse("Notifications updated"));
    }

    private static async Task<IResult> MarkNotificationReadAsync(HttpContext http, int id, NotificationService notifications)
    {
        var currentUser = http.GetCurrentUser();
        await notifications.MarkAsReadAsync(currentUser.Id, new[] { id }, http.RequestAborted);
        return Results.Ok(new MessageResponse("Notification marked as read"));
    }
}
