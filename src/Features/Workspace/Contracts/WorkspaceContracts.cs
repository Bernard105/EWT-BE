namespace EasyWorkTogether.Api.Models;

sealed class InviteSuggestionAggregate
{
    public int? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public record CreateWorkspaceRequest(string Name, string? DomainNamespace, string? IndustryVertical, string? WorkspaceLogoData);

public record InviteRequest(string Email, string? Role);

public record AcceptInvitationRequest(string? Code, int? InvitationId = null);

public record WorkspaceResponse(int Id, string Name, int OwnerId, string CreatedAt, string? UpdatedAt, string? DomainNamespace, string? IndustryVertical, string? WorkspaceLogoData);

public record WorkspaceUpdateResponse(int Id, string Name, int OwnerId, string UpdatedAt, string? DomainNamespace, string? IndustryVertical, string? WorkspaceLogoData);

public record WorkspaceListItem(int Id, string Name, string Role, string? DomainNamespace, string? IndustryVertical, string? WorkspaceLogoData);

public record WorkspaceListResponse(List<WorkspaceListItem> Workspaces);

public record WorkspaceMemberResponse(int Id, string Name, string? Email, string Role, string JoinedAt, string? AvatarUrl = null, string? PublicUserId = null, string? EmailHint = null);

public record WorkspaceMembersResponse(List<WorkspaceMemberResponse> Members);

public record WorkspaceInviteSuggestionResponse(int? UserId, string Email, string Name, int InteractionCount, string Reason);

public record WorkspaceInviteSuggestionListResponse(List<WorkspaceInviteSuggestionResponse> Suggestions);

public record WorkspaceInvitationListItem(int Id, string InviteeEmail, string? InviteeName, string Status, string Role, string ExpiresAt, string CreatedAt, string? RespondedAt, UserBasicResponse Inviter, string? InviteeEmailHint = null);

public record WorkspaceInvitationsResponse(List<WorkspaceInvitationListItem> Invitations);

public record PendingInvitationListItem(int Id, int WorkspaceId, string WorkspaceName, string InviteeEmail, string Role, string ExpiresAt, string CreatedAt, UserBasicResponse Inviter, string? InviteeEmailHint = null);

public record PendingInvitationsResponse(List<PendingInvitationListItem> Invitations);

public record InvitationResponse(int Id, string InviteeEmail, string Role, string ExpiresAt, string? InviteeEmailHint = null);

public record AcceptInvitationResponse(int WorkspaceId, string WorkspaceName, string Role);

public record UserBasicResponse(int Id, string Name);

public record UpdateWorkspaceMemberRoleRequest(string Role);

public record TransferWorkspaceOwnershipRequest(int NewOwnerUserId);
