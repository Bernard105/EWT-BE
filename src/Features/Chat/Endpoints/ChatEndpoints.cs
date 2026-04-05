using EasyWorkTogether.Api.Shared.Infrastructure.Auth;

namespace EasyWorkTogether.Api.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var authApi = app.MapGroup("/api/chat");
        authApi.AddEndpointFilter<RequireSessionFilter>();

        authApi.MapGet("/conversations", ListConversationsAsync);
        authApi.MapGet("/messages/{userId:int}", ListMessagesAsync);
        authApi.MapPost("/messages", CreateMessageAsync);
        authApi.MapPost("/conversations/{userId:int}/read", MarkConversationReadAsync);
    }

    private static async Task<IResult> ListConversationsAsync(HttpContext http, ChatService chatService)
    {
        var currentUser = http.GetCurrentUser();
        var response = await chatService.ListConversationsAsync(currentUser, http.RequestAborted);
        return Results.Ok(response);
    }

    private static async Task<IResult> ListMessagesAsync(HttpContext http, int userId, [FromQuery] int? limit, ChatService chatService)
    {
        var currentUser = http.GetCurrentUser();
        var response = await chatService.ListMessagesAsync(currentUser, userId, limit ?? 80, http.RequestAborted);
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateMessageAsync(HttpContext http, CreateChatMessageRequest request, ChatService chatService, RealtimeConnectionManager realtime)
    {
        var currentUser = http.GetCurrentUser();
        var message = await chatService.CreateMessageAsync(currentUser, new SendMessageSocketPayload(request.ReceiverId, request.Content, request.Type, request.FilePublicId, null), http.RequestAborted);

        await realtime.SendToUserAsync(request.ReceiverId, "receive_message", message with { IsMine = false }, http.RequestAborted);
        await realtime.SendToUserAsync(currentUser.Id, "message_sent", new MessageSentSocketPayload(null, message), http.RequestAborted);

        return Results.Ok(message);
    }

    private static async Task<IResult> MarkConversationReadAsync(HttpContext http, int userId, ChatService chatService)
    {
        var currentUser = http.GetCurrentUser();
        await chatService.MarkConversationReadAsync(currentUser.Id, userId, http.RequestAborted);
        return Results.Ok(new MessageResponse("Conversation marked as read"));
    }
}
