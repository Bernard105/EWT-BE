namespace EasyWorkTogether.Api.Models;

public record CreateTaskRequest(
    string Title,
    string? Description,
    string? DueDate,
    string? DueAt,
    int? AssigneeId,
    int? StoryPoints,
    string? Priority,
    string? Status,
    bool? IsEmergency = null,
    int? SupportRequestedFrom = null);

public record UpdateTaskRequest(
    string? Title,
    string? Description,
    string? DueDate,
    string? DueAt,
    int? AssigneeId,
    int? StoryPoints,
    string? Priority,
    string? Status,
    bool? IsEmergency = null,
    int? SupportRequestedFrom = null);

public record VoteTaskStoryPointsRequest(int Points);

public record FileAssetResponse(int Id, string PublicId, string OriginalFileName, string ContentType, long Size, string Url, string DownloadUrl, bool IsImage);

public record TaskAttachmentResponse(int Id, string Kind, string UploadedAt, UserBasicResponse UploadedBy, FileAssetResponse Asset);

public record TaskActivityResponse(int Id, string ActivityType, string Message, string CreatedAt, UserBasicResponse? Actor);

public record TaskResponse(
    int Id,
    int WorkspaceId,
    string Sku,
    string Title,
    string? Description,
    string? DueDate,
    string? DueAt,
    int? StoryPoints,
    string Priority,
    string Status,
    int CreatedBy,
    UserBasicResponse? CreatedByUser,
    UserBasicResponse? Assignee,
    string CreatedAt,
    int StoryPointVoteCount,
    double? StoryPointVoteAverage,
    int? MyStoryPointVote,
    bool IsEmergency = false,
    UserBasicResponse? SupportRequestedFrom = null,
    string? CompletedAt = null,
    List<TaskAttachmentResponse>? Attachments = null,
    List<TaskActivityResponse>? Activities = null);

public record TaskListItem(
    int Id,
    string Sku,
    string Title,
    string? Description,
    string? DueDate,
    string? DueAt,
    int? StoryPoints,
    string Priority,
    string Status,
    int CreatedBy,
    UserBasicResponse? CreatedByUser,
    UserBasicResponse? Assignee,
    string CreatedAt,
    int StoryPointVoteCount,
    double? StoryPointVoteAverage,
    int? MyStoryPointVote,
    bool IsEmergency = false,
    UserBasicResponse? SupportRequestedFrom = null,
    string? CompletedAt = null);

public record TaskListResponse(List<TaskListItem> Tasks, int? NextCursor, bool HasMore);

public record TaskStatsResponse(int Total, int Pending, int InProgress, int Completed, int Overdue);

public record TaskInfo(
    int Id,
    int WorkspaceId,
    string Sku,
    string Title,
    string? Description,
    DateTime? DueAt,
    int? StoryPoints,
    string Priority,
    string Status,
    int CreatedBy,
    int? AssigneeId,
    bool IsEmergency = false,
    int? SupportRequestedFromId = null,
    DateTime? CompletedAt = null);

public record ImageUploadResponse(string Url, string OriginalFileName, string ContentType, long Size);
