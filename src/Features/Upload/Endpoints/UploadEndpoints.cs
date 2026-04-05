using EasyWorkTogether.Api.Shared.Infrastructure.Auth;

namespace EasyWorkTogether.Api.Endpoints;

public static class UploadEndpoints
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".zip"
    };

    public static void MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        var authApi = app.MapGroup("/api");
        authApi.AddEndpointFilter<RequireSessionFilter>();

        authApi.MapPost("/uploads", UploadGenericAsync);
        authApi.MapPost("/tasks/{id:int}/attachments", UploadTaskAttachmentAsync);

        app.MapGet("/api/files/{publicId:guid}", DownloadFileAsync);
    }

    private static async Task<IResult> UploadGenericAsync(HttpContext http, [FromForm] IFormFile file, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var asset = await SaveFileAssetAsync(db, currentUser.Id, file, http.RequestAborted);
        return Results.Ok(asset);
    }

    private static async Task<IResult> UploadTaskAttachmentAsync(HttpContext http, int id, [FromForm] IFormFile file, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var taskInfo = await GetTaskInfoAsync(db, id);
        if (taskInfo is null)
            return Results.NotFound(new ErrorResponse("Task not found"));

        var role = await GetWorkspaceRoleAsync(db, taskInfo.WorkspaceId, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        await using var tx = await conn.BeginTransactionAsync(http.RequestAborted);

        var asset = await SaveFileAssetAsync(conn, tx, currentUser.Id, file, http.RequestAborted);

        const string attachSql = """
            INSERT INTO task_attachments (task_id, file_asset_id, uploaded_by, kind)
            VALUES (@task_id, @file_asset_id, @uploaded_by, 'completion_proof')
            RETURNING id, created_at;
            """;

        int attachmentId;
        DateTime attachmentCreatedAt;
        await using (var attachCmd = new NpgsqlCommand(attachSql, conn, tx))
        {
            attachCmd.Parameters.AddWithValue("task_id", id);
            attachCmd.Parameters.AddWithValue("file_asset_id", asset.Id);
            attachCmd.Parameters.AddWithValue("uploaded_by", currentUser.Id);

            await using var reader = await attachCmd.ExecuteReaderAsync(http.RequestAborted);
            await reader.ReadAsync(http.RequestAborted);
            attachmentId = reader.GetInt32(0);
            attachmentCreatedAt = reader.GetDateTime(1);
        }

        await InsertTaskActivityAsync(conn, tx, id, currentUser.Id, "attachment_uploaded", $"{currentUser.Name} uploaded proof: {file.FileName}", http.RequestAborted);
        await tx.CommitAsync(http.RequestAborted);

        return Results.Ok(new TaskAttachmentResponse(
            attachmentId,
            "completion_proof",
            ToIsoString(attachmentCreatedAt),
            new UserBasicResponse(currentUser.Id, currentUser.Name),
            asset));
    }

    private static async Task<IResult> DownloadFileAsync(Guid publicId, [FromQuery] bool? download, NpgsqlDataSource db)
    {
        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
            SELECT original_file_name, content_type, data
            FROM file_assets
            WHERE public_id = @public_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("public_id", publicId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("File not found"));

        var fileName = reader.GetString(0);
        var contentType = reader.GetString(1);
        var data = (byte[])reader[2];

        return download == true
            ? Results.File(data, contentType, fileName, enableRangeProcessing: true)
            : Results.File(data, contentType, enableRangeProcessing: true);
    }

    public static async Task<FileAssetResponse> SaveFileAssetAsync(NpgsqlDataSource db, int currentUserId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        var asset = await SaveFileAssetAsync(conn, tx, currentUserId, file, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return asset;
    }

    private static async Task<FileAssetResponse> SaveFileAssetAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int currentUserId, IFormFile file, CancellationToken cancellationToken)
    {
        ValidateUpload(file);
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();
        var publicId = Guid.NewGuid();

        const string sql = """
            INSERT INTO file_assets (public_id, original_file_name, content_type, size_bytes, data, created_by)
            VALUES (@public_id, @original_file_name, @content_type, @size_bytes, @data, @created_by)
            RETURNING id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("public_id", publicId);
        cmd.Parameters.AddWithValue("original_file_name", Path.GetFileName(file.FileName));
        cmd.Parameters.AddWithValue("content_type", string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        cmd.Parameters.AddWithValue("size_bytes", file.Length);
        cmd.Parameters.Add("data", NpgsqlDbType.Bytea).Value = bytes;
        cmd.Parameters.AddWithValue("created_by", currentUserId);

        var assetId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        return new FileAssetResponse(
            assetId,
            publicId.ToString(),
            Path.GetFileName(file.FileName),
            contentType,
            file.Length,
            $"/api/files/{publicId}",
            $"/api/files/{publicId}?download=1",
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateUpload(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("File is required.");

        if (file.Length > MaxUploadBytes)
            throw new InvalidOperationException("File exceeds 10MB limit.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Unsupported file type.");
    }

    private static async Task InsertTaskActivityAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int taskId, int userId, string activityType, string message, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO task_activities (task_id, user_id, activity_type, message)
            VALUES (@task_id, @user_id, @activity_type, @message);
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("task_id", taskId);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("activity_type", activityType);
        cmd.Parameters.AddWithValue("message", message);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
