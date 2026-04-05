using EasyWorkTogether.Api.Shared.Infrastructure.Auth;

namespace EasyWorkTogether.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var adminApi = app.MapGroup("/api/admin");
        adminApi.AddEndpointFilter<RequireSessionFilter>();
        adminApi.AddEndpointFilter<RequireSystemAdminFilter>();

        adminApi.MapGet("/overview", GetOverviewAsync);
        adminApi.MapGet("/users", ListUsersAsync);
        adminApi.MapPut("/users/{id:int}/system-admin", SetSystemAdminAsync);
        adminApi.MapGet("/workspaces", ListWorkspacesAsync);
        adminApi.MapGet("/tasks", ListTasksAsync);
        adminApi.MapGet("/activity", ListActivityAsync);
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext http, NpgsqlDataSource db)
    {
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);

        const string statsSql = """
            SELECT
                (SELECT COUNT(*)::INT FROM users) AS total_users,
                (SELECT COUNT(*)::INT FROM users WHERE email_verified_at IS NOT NULL) AS verified_users,
                (SELECT COUNT(*)::INT FROM users WHERE is_system_admin = TRUE) AS system_admins,
                (SELECT COUNT(DISTINCT user_id)::INT FROM sessions WHERE expires_at > NOW()) AS active_sessions,
                (SELECT COUNT(*)::INT FROM workspaces) AS total_workspaces,
                (
                    SELECT COUNT(*)::INT
                    FROM workspaces w
                    WHERE w.updated_at >= NOW() - INTERVAL '30 days'
                       OR EXISTS (
                            SELECT 1
                            FROM tasks t
                            WHERE t.workspace_id = w.id
                              AND t.created_at >= NOW() - INTERVAL '30 days'
                       )
                ) AS active_workspaces_30d,
                (SELECT COUNT(*)::INT FROM tasks) AS total_tasks,
                (SELECT COUNT(*)::INT FROM tasks WHERE status = 'completed') AS completed_tasks,
                (SELECT COUNT(*)::INT FROM tasks WHERE status <> 'completed' AND due_at IS NOT NULL AND due_at < NOW()) AS overdue_tasks,
                (SELECT COUNT(*)::INT FROM tasks WHERE priority = 'urgent') AS urgent_tasks,
                (SELECT COUNT(*)::INT FROM workspace_invitations WHERE status = 'pending' AND expires_at > NOW()) AS pending_invitations,
                (SELECT COUNT(*)::INT FROM friend_requests WHERE status = 'pending') AS pending_friend_requests,
                (SELECT COUNT(*)::INT FROM chat_messages WHERE created_at >= NOW() - INTERVAL '7 days') AS messages_last_7d;
            """;

        AdminMetricSummaryResponse stats;
        await using (var cmd = new NpgsqlCommand(statsSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(http.RequestAborted))
        {
            await reader.ReadAsync(http.RequestAborted);
            stats = new AdminMetricSummaryResponse(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12));
        }

        var userGrowth = await ReadTimeseriesAsync(conn, http.RequestAborted, """
            WITH dates AS (
                SELECT generate_series((CURRENT_DATE - INTERVAL '13 days')::date, CURRENT_DATE::date, INTERVAL '1 day')::date AS day
            ), counts AS (
                SELECT created_at::date AS day, COUNT(*)::INT AS value
                FROM users
                WHERE created_at >= CURRENT_DATE - INTERVAL '13 days'
                GROUP BY created_at::date
            )
            SELECT TO_CHAR(d.day, 'YYYY-MM-DD'), COALESCE(c.value, 0)
            FROM dates d
            LEFT JOIN counts c ON c.day = d.day
            ORDER BY d.day;
            """);

        var workspaceGrowth = await ReadTimeseriesAsync(conn, http.RequestAborted, """
            WITH dates AS (
                SELECT generate_series((CURRENT_DATE - INTERVAL '13 days')::date, CURRENT_DATE::date, INTERVAL '1 day')::date AS day
            ), counts AS (
                SELECT created_at::date AS day, COUNT(*)::INT AS value
                FROM workspaces
                WHERE created_at >= CURRENT_DATE - INTERVAL '13 days'
                GROUP BY created_at::date
            )
            SELECT TO_CHAR(d.day, 'YYYY-MM-DD'), COALESCE(c.value, 0)
            FROM dates d
            LEFT JOIN counts c ON c.day = d.day
            ORDER BY d.day;
            """);

        var taskFlow = await ReadTaskFlowAsync(conn, http.RequestAborted, """
            WITH dates AS (
                SELECT generate_series((CURRENT_DATE - INTERVAL '13 days')::date, CURRENT_DATE::date, INTERVAL '1 day')::date AS day
            ), created_counts AS (
                SELECT created_at::date AS day, COUNT(*)::INT AS value
                FROM tasks
                WHERE created_at >= CURRENT_DATE - INTERVAL '13 days'
                GROUP BY created_at::date
            ), completed_counts AS (
                SELECT completed_at::date AS day, COUNT(*)::INT AS value
                FROM tasks
                WHERE completed_at IS NOT NULL
                  AND completed_at >= CURRENT_DATE - INTERVAL '13 days'
                GROUP BY completed_at::date
            )
            SELECT TO_CHAR(d.day, 'YYYY-MM-DD'), COALESCE(cc.value, 0), COALESCE(done.value, 0)
            FROM dates d
            LEFT JOIN created_counts cc ON cc.day = d.day
            LEFT JOIN completed_counts done ON done.day = d.day
            ORDER BY d.day;
            """);

        var statusDistribution = await ReadBreakdownAsync(conn, http.RequestAborted, """
            SELECT label, value
            FROM (
                SELECT 'Pending' AS label, COUNT(*) FILTER (WHERE status = 'pending')::INT AS value FROM tasks
                UNION ALL
                SELECT 'In progress', COUNT(*) FILTER (WHERE status = 'in_progress')::INT FROM tasks
                UNION ALL
                SELECT 'Completed', COUNT(*) FILTER (WHERE status = 'completed')::INT FROM tasks
                UNION ALL
                SELECT 'Overdue', COUNT(*) FILTER (WHERE status <> 'completed' AND due_at IS NOT NULL AND due_at < NOW())::INT FROM tasks
            ) s
            ORDER BY value DESC, label;
            """);

        var priorityDistribution = await ReadBreakdownAsync(conn, http.RequestAborted, """
            SELECT CASE priority
                    WHEN 'low' THEN 'Low'
                    WHEN 'medium' THEN 'Medium'
                    WHEN 'high' THEN 'High'
                    WHEN 'urgent' THEN 'Urgent'
                    ELSE INITCAP(priority)
                   END AS label,
                   COUNT(*)::INT AS value
            FROM tasks
            GROUP BY priority
            ORDER BY value DESC, label;
            """);

        var industryDistribution = await ReadBreakdownAsync(conn, http.RequestAborted, """
            SELECT COALESCE(NULLIF(BTRIM(industry_vertical), ''), 'Unspecified') AS label,
                   COUNT(*)::INT AS value
            FROM workspaces
            GROUP BY COALESCE(NULLIF(BTRIM(industry_vertical), ''), 'Unspecified')
            ORDER BY value DESC, label
            LIMIT 6;
            """);

        var topWorkspaces = await ReadTopWorkspacesAsync(conn, http.RequestAborted, """
            WITH member_counts AS (
                SELECT workspace_id, COUNT(*)::INT AS member_count,
                       COUNT(*) FILTER (WHERE role IN ('owner', 'admin'))::INT AS admin_count
                FROM workspace_members
                GROUP BY workspace_id
            ), task_counts AS (
                SELECT workspace_id,
                       COUNT(*)::INT AS task_count,
                       COUNT(*) FILTER (WHERE status = 'completed')::INT AS completed_count
                FROM tasks
                GROUP BY workspace_id
            ), invitation_counts AS (
                SELECT workspace_id, COUNT(*) FILTER (WHERE status = 'pending' AND expires_at > NOW())::INT AS pending_invitations
                FROM workspace_invitations
                GROUP BY workspace_id
            )
            SELECT w.id,
                   w.name,
                   owner.name,
                   COALESCE(mc.member_count, 0),
                   COALESCE(tc.task_count, 0),
                   COALESCE(ic.pending_invitations, 0),
                   CASE WHEN COALESCE(tc.task_count, 0) = 0 THEN 0 ELSE ROUND((COALESCE(tc.completed_count, 0)::numeric / tc.task_count::numeric) * 100)::INT END,
                   w.created_at
            FROM workspaces w
            JOIN users owner ON owner.id = w.owner_id
            LEFT JOIN member_counts mc ON mc.workspace_id = w.id
            LEFT JOIN task_counts tc ON tc.workspace_id = w.id
            LEFT JOIN invitation_counts ic ON ic.workspace_id = w.id
            ORDER BY COALESCE(tc.task_count, 0) DESC, COALESCE(mc.member_count, 0) DESC, w.id DESC
            LIMIT 6;
            """);

        var recentActivity = await ReadActivityAsync(conn, http.RequestAborted, 8);

        return Results.Ok(new AdminOverviewResponse(
            stats,
            userGrowth,
            workspaceGrowth,
            taskFlow,
            statusDistribution,
            priorityDistribution,
            industryDistribution,
            topWorkspaces,
            recentActivity));
    }

    private static async Task<IResult> ListUsersAsync(
        HttpContext http,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery(Name = "system_admin")] string? systemAdmin,
        [FromQuery(Name = "sort_by")] string? sortBy,
        NpgsqlDataSource db)
    {
        var normalizedPage = Math.Max(page ?? 1, 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? 12, 1, 100);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var orderBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "name" => "LOWER(u.name), u.id",
            "last_seen" => "last_seen_at DESC NULLS LAST, u.id DESC",
            "workspace_count" => "workspace_count DESC, u.id DESC",
            _ => "u.created_at DESC, u.id DESC"
        };

        var systemAdminFilter = (systemAdmin ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => (bool?)null
        };

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        var sql = $"""
            WITH workspace_counts AS (
                SELECT user_id,
                       COUNT(*)::INT AS workspace_count,
                       COUNT(*) FILTER (WHERE role = 'owner')::INT AS owned_workspace_count
                FROM workspace_members
                GROUP BY user_id
            ), created_tasks AS (
                SELECT created_by AS user_id, COUNT(*)::INT AS created_task_count
                FROM tasks
                GROUP BY created_by
            ), assigned_tasks AS (
                SELECT assignee_id AS user_id, COUNT(*)::INT AS assigned_task_count
                FROM tasks
                WHERE assignee_id IS NOT NULL
                GROUP BY assignee_id
            ), session_counts AS (
                SELECT user_id,
                       COUNT(*) FILTER (WHERE expires_at > NOW())::INT AS active_session_count,
                       MAX(created_at) AS last_seen_at
                FROM sessions
                GROUP BY user_id
            )
            SELECT u.id,
                   u.name,
                   u.email,
                   u.public_user_id,
                   u.is_system_admin,
                   u.created_at,
                   COALESCE(wc.workspace_count, 0) AS workspace_count,
                   COALESCE(wc.owned_workspace_count, 0) AS owned_workspace_count,
                   COALESCE(ct.created_task_count, 0) AS created_task_count,
                   COALESCE(at.assigned_task_count, 0) AS assigned_task_count,
                   COALESCE(sc.active_session_count, 0) AS active_session_count,
                   sc.last_seen_at,
                   COUNT(*) OVER()::INT AS total_count
            FROM users u
            LEFT JOIN workspace_counts wc ON wc.user_id = u.id
            LEFT JOIN created_tasks ct ON ct.user_id = u.id
            LEFT JOIN assigned_tasks at ON at.user_id = u.id
            LEFT JOIN session_counts sc ON sc.user_id = u.id
            WHERE (@query = '' OR u.name ILIKE @like_query OR u.email ILIKE @like_query OR COALESCE(u.public_user_id, '') ILIKE @like_query)
              AND (@system_admin_filter IS NULL OR u.is_system_admin = @system_admin_filter)
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", query?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("like_query", $"%{(query ?? string.Empty).Trim()}%");
        cmd.Parameters.AddWithValue("system_admin_filter", (object?)systemAdminFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", normalizedPageSize);
        cmd.Parameters.AddWithValue("offset", offset);

        var items = new List<AdminUserListItemResponse>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
        while (await reader.ReadAsync(http.RequestAborted))
        {
            total = reader.GetInt32(12);
            items.Add(new AdminUserListItemResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : FormatPublicUserId(reader.GetString(3)),
                reader.GetBoolean(4),
                ToIsoString(reader.GetDateTime(5)),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : ToIsoString(reader.GetDateTime(11))));
        }

        return Results.Ok(new AdminUsersListResponse(items, normalizedPage, normalizedPageSize, total));
    }

    private static async Task<IResult> SetSystemAdminAsync(HttpContext http, int id, SetSystemAdminRequest request, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        await using var tx = await conn.BeginTransactionAsync(http.RequestAborted);

        const string lockSql = """
            SELECT id, name, email, is_system_admin
            FROM users
            WHERE id = @id
            FOR UPDATE;
            """;

        int targetUserId;
        string targetName;
        string targetEmail;
        bool currentState;

        await using (var cmd = new NpgsqlCommand(lockSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
            if (!await reader.ReadAsync(http.RequestAborted))
                return Results.NotFound(new ErrorResponse("User not found"));

            targetUserId = reader.GetInt32(0);
            targetName = reader.GetString(1);
            targetEmail = reader.GetString(2);
            currentState = reader.GetBoolean(3);
        }

        if (currentState == request.IsSystemAdmin)
        {
            await tx.CommitAsync(http.RequestAborted);
            return Results.Ok(new MessageResponse(request.IsSystemAdmin
                ? "User already has system admin access"
                : "User already does not have system admin access"));
        }

        if (!request.IsSystemAdmin)
        {
            const string countSql = "SELECT COUNT(*)::INT FROM users WHERE is_system_admin = TRUE;";
            await using var countCmd = new NpgsqlCommand(countSql, conn, tx);
            var adminCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(http.RequestAborted));

            if (adminCount <= 1 && currentState)
                return Results.BadRequest(new ErrorResponse("The last system admin cannot be removed"));

            if (currentUser.Id == targetUserId && currentState && adminCount <= 2)
                return Results.BadRequest(new ErrorResponse("Keep at least one other system admin before removing your own access"));
        }

        const string updateSql = """
            UPDATE users
            SET is_system_admin = @is_system_admin,
                system_admin_granted_at = CASE WHEN @is_system_admin THEN COALESCE(system_admin_granted_at, NOW()) ELSE NULL END,
                updated_at = NOW()
            WHERE id = @id;
            """;

        await using (var updateCmd = new NpgsqlCommand(updateSql, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("id", targetUserId);
            updateCmd.Parameters.AddWithValue("is_system_admin", request.IsSystemAdmin);
            await updateCmd.ExecuteNonQueryAsync(http.RequestAborted);
        }

        await tx.CommitAsync(http.RequestAborted);

        return Results.Ok(new MessageResponse(request.IsSystemAdmin
            ? $"Granted system admin access to {targetName} ({targetEmail})"
            : $"Removed system admin access from {targetName} ({targetEmail})"));
    }

    private static async Task<IResult> ListWorkspacesAsync(
        HttpContext http,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery(Name = "sort_by")] string? sortBy,
        NpgsqlDataSource db)
    {
        var normalizedPage = Math.Max(page ?? 1, 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? 12, 1, 100);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var orderBy = (sortBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "members" => "member_count DESC, w.id DESC",
            "tasks" => "task_count DESC, w.id DESC",
            "name" => "LOWER(w.name), w.id",
            _ => "w.created_at DESC, w.id DESC"
        };

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        var sql = $"""
            WITH member_counts AS (
                SELECT workspace_id,
                       COUNT(*)::INT AS member_count,
                       COUNT(*) FILTER (WHERE role IN ('owner', 'admin'))::INT AS admin_count
                FROM workspace_members
                GROUP BY workspace_id
            ), task_counts AS (
                SELECT workspace_id,
                       COUNT(*)::INT AS task_count,
                       MAX(created_at) AS latest_task_at
                FROM tasks
                GROUP BY workspace_id
            ), invitation_counts AS (
                SELECT workspace_id,
                       COUNT(*) FILTER (WHERE status = 'pending' AND expires_at > NOW())::INT AS pending_invitations
                FROM workspace_invitations
                GROUP BY workspace_id
            )
            SELECT w.id,
                   w.name,
                   owner.name,
                   w.domain_namespace,
                   w.industry_vertical,
                   COALESCE(mc.member_count, 0) AS member_count,
                   COALESCE(mc.admin_count, 0) AS admin_count,
                   COALESCE(tc.task_count, 0) AS task_count,
                   COALESCE(ic.pending_invitations, 0) AS pending_invitations,
                   w.created_at,
                   w.updated_at,
                   tc.latest_task_at,
                   COUNT(*) OVER()::INT AS total_count
            FROM workspaces w
            JOIN users owner ON owner.id = w.owner_id
            LEFT JOIN member_counts mc ON mc.workspace_id = w.id
            LEFT JOIN task_counts tc ON tc.workspace_id = w.id
            LEFT JOIN invitation_counts ic ON ic.workspace_id = w.id
            WHERE (@query = '' OR w.name ILIKE @like_query OR COALESCE(w.domain_namespace, '') ILIKE @like_query OR COALESCE(w.industry_vertical, '') ILIKE @like_query)
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", query?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("like_query", $"%{(query ?? string.Empty).Trim()}%");
        cmd.Parameters.AddWithValue("limit", normalizedPageSize);
        cmd.Parameters.AddWithValue("offset", offset);

        var items = new List<AdminWorkspaceListItemResponse>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
        while (await reader.ReadAsync(http.RequestAborted))
        {
            total = reader.GetInt32(12);
            items.Add(new AdminWorkspaceListItemResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                ToIsoString(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : ToIsoString(reader.GetDateTime(10)),
                reader.IsDBNull(11) ? null : ToIsoString(reader.GetDateTime(11))));
        }

        return Results.Ok(new AdminWorkspacesListResponse(items, normalizedPage, normalizedPageSize, total));
    }

    private static async Task<IResult> ListTasksAsync(
        HttpContext http,
        [FromQuery(Name = "q")] string? query,
        [FromQuery(Name = "page")] int? page,
        [FromQuery(Name = "page_size")] int? pageSize,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "priority")] string? priority,
        [FromQuery(Name = "workspace_id")] int? workspaceId,
        NpgsqlDataSource db)
    {
        var normalizedPage = Math.Max(page ?? 1, 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? 12, 1, 100);
        var offset = (normalizedPage - 1) * normalizedPageSize;
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToLowerInvariant();
        var normalizedPriority = string.IsNullOrWhiteSpace(priority) ? string.Empty : priority.Trim().ToLowerInvariant();

        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        const string sql = """
            SELECT t.id,
                   t.sku,
                   t.title,
                   t.workspace_id,
                   w.name AS workspace_name,
                   t.status,
                   t.priority,
                   t.is_emergency,
                   t.created_at,
                   t.due_at,
                   t.completed_at,
                   creator.id,
                   creator.name,
                   assignee.id,
                   assignee.name,
                   COUNT(*) OVER()::INT AS total_count
            FROM tasks t
            JOIN workspaces w ON w.id = t.workspace_id
            JOIN users creator ON creator.id = t.created_by
            LEFT JOIN users assignee ON assignee.id = t.assignee_id
            WHERE (@query = '' OR t.title ILIKE @like_query OR COALESCE(t.sku, '') ILIKE @like_query OR w.name ILIKE @like_query)
              AND (@status = '' OR t.status = @status)
              AND (@priority = '' OR t.priority = @priority)
              AND (@workspace_id IS NULL OR t.workspace_id = @workspace_id)
            ORDER BY t.created_at DESC, t.id DESC
            LIMIT @limit OFFSET @offset;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("query", query?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("like_query", $"%{(query ?? string.Empty).Trim()}%");
        cmd.Parameters.AddWithValue("status", normalizedStatus);
        cmd.Parameters.AddWithValue("priority", normalizedPriority);
        cmd.Parameters.AddWithValue("workspace_id", (object?)workspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", normalizedPageSize);
        cmd.Parameters.AddWithValue("offset", offset);

        var items = new List<AdminTaskListItemResponse>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
        while (await reader.ReadAsync(http.RequestAborted))
        {
            total = reader.GetInt32(15);
            items.Add(new AdminTaskListItemResponse(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? $"TASK-{reader.GetInt32(0)}" : reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetBoolean(7),
                ToIsoString(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : ToIsoString(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : ToIsoString(reader.GetDateTime(10)),
                new UserBasicResponse(reader.GetInt32(11), reader.GetString(12)),
                reader.IsDBNull(13) ? null : new UserBasicResponse(reader.GetInt32(13), reader.GetString(14))));
        }

        return Results.Ok(new AdminTasksListResponse(items, normalizedPage, normalizedPageSize, total));
    }

    private static async Task<IResult> ListActivityAsync(HttpContext http, [FromQuery(Name = "limit")] int? limit, NpgsqlDataSource db)
    {
        var normalizedLimit = Math.Clamp(limit ?? 24, 1, 100);
        await using var conn = await db.OpenConnectionAsync(http.RequestAborted);
        var items = await ReadActivityAsync(conn, http.RequestAborted, normalizedLimit);
        return Results.Ok(new { items, limit = normalizedLimit });
    }

    private static async Task<List<AdminTimeseriesPointResponse>> ReadTimeseriesAsync(NpgsqlConnection conn, CancellationToken cancellationToken, string sql)
    {
        var items = new List<AdminTimeseriesPointResponse>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AdminTimeseriesPointResponse(reader.GetString(0), reader.GetInt32(1)));
        }

        return items;
    }

    private static async Task<List<AdminTaskFlowPointResponse>> ReadTaskFlowAsync(NpgsqlConnection conn, CancellationToken cancellationToken, string sql)
    {
        var items = new List<AdminTaskFlowPointResponse>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AdminTaskFlowPointResponse(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return items;
    }

    private static async Task<List<AdminBreakdownItemResponse>> ReadBreakdownAsync(NpgsqlConnection conn, CancellationToken cancellationToken, string sql)
    {
        var items = new List<AdminBreakdownItemResponse>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AdminBreakdownItemResponse(reader.GetString(0), reader.GetInt32(1)));
        }

        return items;
    }

    private static async Task<List<AdminTopWorkspaceResponse>> ReadTopWorkspacesAsync(NpgsqlConnection conn, CancellationToken cancellationToken, string sql)
    {
        var items = new List<AdminTopWorkspaceResponse>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AdminTopWorkspaceResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                ToIsoString(reader.GetDateTime(7))));
        }

        return items;
    }

    private static async Task<List<AdminActivityItemResponse>> ReadActivityAsync(NpgsqlConnection conn, CancellationToken cancellationToken, int limit)
    {
        const string sql = """
            SELECT type, title, description, occurred_at, entity_type, entity_id, actor_name, workspace_name
            FROM (
                SELECT 'user_registered' AS type,
                       'User registered' AS title,
                       COALESCE(u.name, u.email) || ' joined the platform' AS description,
                       u.created_at AS occurred_at,
                       'user' AS entity_type,
                       u.id AS entity_id,
                       u.name AS actor_name,
                       NULL::TEXT AS workspace_name
                FROM users u

                UNION ALL

                SELECT 'workspace_created',
                       'Workspace created',
                       w.name || ' was created by ' || owner.name,
                       w.created_at,
                       'workspace',
                       w.id,
                       owner.name,
                       w.name
                FROM workspaces w
                JOIN users owner ON owner.id = w.owner_id

                UNION ALL

                SELECT 'task_created',
                       'Task created',
                       t.title || ' was created in ' || w.name,
                       t.created_at,
                       'task',
                       t.id,
                       creator.name,
                       w.name
                FROM tasks t
                JOIN users creator ON creator.id = t.created_by
                JOIN workspaces w ON w.id = t.workspace_id

                UNION ALL

                SELECT 'task_completed',
                       'Task completed',
                       t.title || ' was completed in ' || w.name,
                       t.completed_at,
                       'task',
                       t.id,
                       COALESCE(assignee.name, creator.name),
                       w.name
                FROM tasks t
                JOIN users creator ON creator.id = t.created_by
                LEFT JOIN users assignee ON assignee.id = t.assignee_id
                JOIN workspaces w ON w.id = t.workspace_id
                WHERE t.completed_at IS NOT NULL

                UNION ALL

                SELECT 'workspace_invitation',
                       'Invitation sent',
                       'Invitation sent to ' || wi.invitee_email || ' for ' || w.name,
                       wi.created_at,
                       'workspace_invitation',
                       wi.id,
                       inviter.name,
                       w.name
                FROM workspace_invitations wi
                JOIN users inviter ON inviter.id = wi.inviter_id
                JOIN workspaces w ON w.id = wi.workspace_id

                UNION ALL

                SELECT 'friend_request',
                       'Connection request',
                       requester.name || ' sent a connection request to ' || receiver.name,
                       fr.created_at,
                       'friend_request',
                       fr.id,
                       requester.name,
                       NULL::TEXT
                FROM friend_requests fr
                JOIN users requester ON requester.id = fr.requester_id
                JOIN users receiver ON receiver.id = fr.receiver_id

                UNION ALL

                SELECT 'chat_message',
                       'Direct message sent',
                       sender.name || ' sent a message to ' || receiver.name,
                       cm.created_at,
                       'chat_message',
                       cm.id,
                       sender.name,
                       NULL::TEXT
                FROM chat_messages cm
                JOIN users sender ON sender.id = cm.sender_id
                JOIN users receiver ON receiver.id = cm.receiver_id
            ) events
            ORDER BY occurred_at DESC
            LIMIT @limit;
            """;

        var items = new List<AdminActivityItemResponse>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AdminActivityItemResponse(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ToIsoString(reader.GetDateTime(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return items;
    }
}
