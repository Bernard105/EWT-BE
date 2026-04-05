namespace EasyWorkTogether.Api.Models;

public record SendFriendRequestRequest(int ReceiverId);

public record FriendSearchRequest(string Query);

public record FriendUserResponse(int Id, string Name, string? Email, bool IsOnline, string Relationship, string? LastMessagePreview, string? LastMessageAt, string? PublicUserId = null, string? AvatarUrl = null);

public record FriendRequestResponse(int Id, FriendUserResponse User, string Direction, string Status, string CreatedAt, string? RespondedAt);

public record FriendListResponse(List<FriendUserResponse> Friends, List<FriendRequestResponse> PendingIncoming, List<FriendRequestResponse> PendingOutgoing, List<FriendUserResponse> Suggestions);

public record FriendSearchResultResponse(FriendUserResponse User, string Relationship);

public record FriendSearchResponse(List<FriendSearchResultResponse> Results);
