namespace EasyWorkTogether.Api.Models;

public record CreateChatMessageRequest(int ReceiverId, string? Content, string Type, string? FilePublicId);

public record ChatMessageResponse(int Id, int SenderId, int ReceiverId, string Content, string Type, string CreatedAt, FileAssetResponse? Attachment, bool IsMine = false);

public record ChatMessageListResponse(List<ChatMessageResponse> Messages);

public record ChatConversationResponse(FriendUserResponse User, ChatMessageResponse? LastMessage, int UnreadCount);

public record ChatConversationListResponse(List<ChatConversationResponse> Conversations);
