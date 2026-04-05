namespace EasyWorkTogether.Api.Models;

public record AdminMetricSummaryResponse(
    int TotalUsers,
    int VerifiedUsers,
    int SystemAdmins,
    int ActiveSessions,
    int TotalWorkspaces,
    int ActiveWorkspaces30d,
    int TotalTasks,
    int CompletedTasks,
    int OverdueTasks,
    int UrgentTasks,
    int PendingInvitations,
    int PendingFriendRequests,
    int MessagesLast7d);

public record AdminTimeseriesPointResponse(string Date, int Value);

public record AdminTaskFlowPointResponse(string Date, int Created, int Completed);

public record AdminBreakdownItemResponse(string Label, int Value);

public record AdminTopWorkspaceResponse(
    int Id,
    string Name,
    string OwnerName,
    int MemberCount,
    int TaskCount,
    int PendingInvitations,
    int CompletionRate,
    string CreatedAt);

public record AdminActivityItemResponse(
    string Type,
    string Title,
    string Description,
    string OccurredAt,
    string EntityType,
    int? EntityId,
    string? ActorName = null,
    string? WorkspaceName = null);

public record AdminOverviewResponse(
    AdminMetricSummaryResponse Stats,
    List<AdminTimeseriesPointResponse> UserGrowth,
    List<AdminTimeseriesPointResponse> WorkspaceGrowth,
    List<AdminTaskFlowPointResponse> TaskFlow,
    List<AdminBreakdownItemResponse> TaskStatusDistribution,
    List<AdminBreakdownItemResponse> TaskPriorityDistribution,
    List<AdminBreakdownItemResponse> WorkspaceIndustryDistribution,
    List<AdminTopWorkspaceResponse> TopWorkspaces,
    List<AdminActivityItemResponse> RecentActivity);

public record AdminUserListItemResponse(
    int Id,
    string Name,
    string Email,
    string? PublicUserId,
    bool IsSystemAdmin,
    string CreatedAt,
    int WorkspaceCount,
    int OwnedWorkspaceCount,
    int CreatedTaskCount,
    int AssignedTaskCount,
    int ActiveSessionCount,
    string? LastSeenAt);

public record AdminUsersListResponse(List<AdminUserListItemResponse> Items, int Page, int PageSize, int Total);

public record AdminWorkspaceListItemResponse(
    int Id,
    string Name,
    string OwnerName,
    string? DomainNamespace,
    string? IndustryVertical,
    int MemberCount,
    int AdminCount,
    int TaskCount,
    int PendingInvitations,
    string CreatedAt,
    string? UpdatedAt,
    string? LatestTaskAt);

public record AdminWorkspacesListResponse(List<AdminWorkspaceListItemResponse> Items, int Page, int PageSize, int Total);

public record AdminTaskListItemResponse(
    int Id,
    string Sku,
    string Title,
    int WorkspaceId,
    string WorkspaceName,
    string Status,
    string Priority,
    bool IsEmergency,
    string CreatedAt,
    string? DueAt,
    string? CompletedAt,
    UserBasicResponse? CreatedBy,
    UserBasicResponse? Assignee);

public record AdminTasksListResponse(List<AdminTaskListItemResponse> Items, int Page, int PageSize, int Total);

public record SetSystemAdminRequest(bool IsSystemAdmin);
