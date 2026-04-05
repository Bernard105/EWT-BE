namespace EasyWorkTogether.Api.Shared.Utils;

public static class AppSupport
{
public static string ComputeSha256(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

public static string BuildAvatarLabel(string? name, string? email)
{
    var parts = (name ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Take(2)
        .Select(part => char.ToUpperInvariant(part[0]))
        .ToArray();

    if (parts.Length > 0)
        return new string(parts);

    var fallback = string.IsNullOrWhiteSpace(email) ? "U" : email.Trim()[0].ToString();
    return fallback.ToUpperInvariant();
}


public static string NormalizeHumanName(string? value) =>
    string.Join(" ", (value ?? string.Empty)
        .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

public static string NormalizeWorkspaceName(string? value) =>
    string.Join(" ", (value ?? string.Empty)
        .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

public static string? ValidateHumanName(string? value, string label = "Name")
{
    var normalized = NormalizeHumanName(value);

    if (string.IsNullOrWhiteSpace(normalized))
        return $"{label} is required";

    if (normalized.Length < 2)
        return $"{label} must be at least 2 characters";

    if (normalized.Length > 60)
        return $"{label} must be 60 characters or fewer";

    if (!Regex.IsMatch(normalized, @"^[\p{L}\p{M}][\p{L}\p{M}\s'.-]+$"))
        return $"{label} contains invalid characters";

    return null;
}

public static string? ValidateWorkspaceName(string? value, string label = "Workspace name")
{
    var normalized = NormalizeWorkspaceName(value);

    if (string.IsNullOrWhiteSpace(normalized))
        return $"{label} is required";

    if (normalized.Length < 2)
        return $"{label} must be at least 2 characters";

    if (normalized.Length > 80)
        return $"{label} must be 80 characters or fewer";

    if (!Regex.IsMatch(normalized, @"^[\p{L}\p{M}\d][\p{L}\p{M}\d\s&.,'()/_-]+$"))
        return $"{label} contains invalid characters";

    return null;
}

public static string? ValidateStrongPassword(string? value, string label = "Password")
{
    var password = value ?? string.Empty;

    if (string.IsNullOrWhiteSpace(password))
        return $"{label} is required";

    if (password.Length < 8)
        return $"{label} must be at least 8 characters";

    if (password.Length > 64)
        return $"{label} must be 64 characters or fewer";

    if (!password.Any(char.IsLower))
        return $"{label} must include at least one lowercase letter";

    if (!password.Any(char.IsUpper))
        return $"{label} must include at least one uppercase letter";

    if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        return $"{label} must include at least one special character";

    return null;
}


public static string[] SupportedIndustryVerticals() =>
[
    "Technology & SaaS",
    "Financial Services",
    "Healthcare",
    "Education",
    "Retail & E-commerce",
    "Manufacturing",
    "Real Estate & Construction",
    "Logistics & Transportation",
    "Media & Entertainment",
    "Telecommunications",
    "Professional Services",
    "Energy & Utilities",
    "Agriculture",
    "Government & Public Sector",
    "Nonprofit",
    "Other"
];

public static string? NormalizeDomainNamespace(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var trimmed = value.Trim().ToLowerInvariant();
    var builder = new StringBuilder(trimmed.Length);
    var lastWasDash = false;

    foreach (var ch in trimmed)
    {
        if (char.IsLetterOrDigit(ch))
        {
            builder.Append(ch);
            lastWasDash = false;
            continue;
        }

        if ((ch == '-' || ch == '_' || ch == ' ' || ch == '.') && builder.Length > 0 && !lastWasDash)
        {
            builder.Append('-');
            lastWasDash = true;
        }
    }

    while (builder.Length > 0 && builder[^1] == '-')
        builder.Length--;

    var normalized = builder.ToString();
    if (string.IsNullOrWhiteSpace(normalized))
        return null;

    if (normalized.Length < 2 || normalized.Length > 50)
        throw new ArgumentException("Domain namespace must be between 2 and 50 characters");

    return normalized;
}

public static string? NormalizeIndustryVertical(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var match = SupportedIndustryVerticals().FirstOrDefault(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
    if (match is null)
        throw new ArgumentException("Industry vertical is invalid");

    return match;
}

public static string? NormalizeWorkspaceLogoData(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var trimmed = value.Trim();

    if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
    {
        if (trimmed.Length > 4_000_000)
            throw new ArgumentException("Workspace logo is too large");

        return trimmed;
    }

    if (trimmed.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        return trimmed;

    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
        (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
    {
        return trimmed;
    }

    throw new ArgumentException("Workspace logo must be an uploaded image URL or a valid image data URL");
}

public static string? NormalizeProfileAvatarUrl(string? value)
{
    if (value is null)
        return null;

    if (string.IsNullOrWhiteSpace(value))
        return null;

    var trimmed = value.Trim();

    if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
    {
        if (trimmed.Length > 4_000_000)
            throw new ArgumentException("Avatar image is too large");

        return trimmed;
    }

    if (trimmed.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase) ||
        trimmed.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
    {
        return trimmed;
    }

    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
        (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
    {
        return trimmed;
    }

    throw new ArgumentException("Avatar must be an uploaded image URL or a valid image data URL");
}

public static int GetSessionTtlDays(IConfiguration? configuration = null)
{
    var configured = configuration?["SESSION_TTL_DAYS"] ?? Environment.GetEnvironmentVariable("SESSION_TTL_DAYS");
    return int.TryParse(configured, out var ttlDays) && ttlDays is >= 1 and <= 90 ? ttlDays : 30;
}

public static DateTime GetPersistentSessionExpiryUtc(IConfiguration? configuration = null) =>
    DateTime.UtcNow.AddDays(GetSessionTtlDays(configuration));

public static async Task CleanupExpiredSessionsAsync(NpgsqlDataSource db, CancellationToken cancellationToken = default)
{
    await using var conn = await db.OpenConnectionAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand("DELETE FROM sessions WHERE expires_at <= NOW();", conn);
    await cmd.ExecuteNonQueryAsync(cancellationToken);
}

public static string FormatPublicUserId(string? value)
{
    var normalized = NormalizePublicUserId(value);
    return string.IsNullOrWhiteSpace(normalized) ? string.Empty : $"#{normalized}";
}

public static string? MaskEmailAddress(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var parts = value.Trim().Split('@', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
        return value.Trim();

    var local = parts[0];
    var domain = parts[1];
    if (local.Length <= 2)
        return $"{local[0]}***@{domain}";

    return $"{local[0]}{new string('*', Math.Max(2, local.Length - 2))}{local[^1]}@{domain}";
}

public static string NormalizePublicUserId(string? value)
{
    var normalized = Regex.Replace((value ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant(), "[^a-z0-9._-]", string.Empty);
    return normalized.Length > 24 ? normalized[..24] : normalized;
}

public static async Task<string> GenerateUniquePublicUserIdAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string? seed, CancellationToken cancellationToken = default)
{
    var normalizedSeed = NormalizePublicUserId(seed);
    if (normalizedSeed.Length < 3)
        normalizedSeed = "ewt";

    if (normalizedSeed.Length > 12)
        normalizedSeed = normalizedSeed[..12];

    for (var attempt = 0; attempt < 40; attempt++)
    {
        var suffix = RandomNumberGenerator.GetInt32(1000, 999999);
        var candidate = $"{normalizedSeed}{suffix}";
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM users WHERE LOWER(public_user_id) = @candidate LIMIT 1;", conn, tx);
        cmd.Parameters.AddWithValue("candidate", candidate);
        var exists = await cmd.ExecuteScalarAsync(cancellationToken);
        if (exists is null)
            return candidate;
    }

    return $"ewt{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
}

public static DateTime? ParseTaskDueAtOrThrow(string? dueAtValue, string? dueDateValue = null)
{
    if (!string.IsNullOrWhiteSpace(dueAtValue))
    {
        if (!DateTimeOffset.TryParse(dueAtValue.Trim(), out var parsedDueAt))
            throw new ArgumentException("due_at must be a valid ISO datetime");

        return parsedDueAt.UtcDateTime;
    }

    if (!string.IsNullOrWhiteSpace(dueDateValue))
    {
        if (!DateOnly.TryParse(dueDateValue.Trim(), out var parsedDueDate))
            throw new ArgumentException("due_date must be yyyy-MM-dd");

        return DateTime.SpecifyKind(parsedDueDate.ToDateTime(new TimeOnly(23, 59, 0)), DateTimeKind.Utc);
    }

    return null;
}

public static string BuildTaskSkuBase(string? title)
{
    var normalized = Regex.Replace((title ?? string.Empty).Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "-")
        .Trim('-');

    if (string.IsNullOrWhiteSpace(normalized))
        normalized = "TASK";

    if (normalized.Length > 36)
        normalized = normalized[..36].Trim('-');

    return $"TASK-{normalized}";
}

public static async Task<string> GenerateUniqueTaskSkuAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int workspaceId, string title)
{
    const string sequenceSql = """
        SELECT COUNT(*)::INT
        FROM tasks
        WHERE workspace_id = @workspace_id;
        """;

    await using (var countCmd = new NpgsqlCommand(sequenceSql, conn, tx))
    {
        countCmd.Parameters.AddWithValue("workspace_id", workspaceId);
        var existingCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync() ?? 0);
        if (existingCount == 0)
            return "SKU-001";
    }

    var baseSku = BuildTaskSkuBase(title);
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    const string sql = """
        SELECT sku
        FROM tasks
        WHERE workspace_id = @workspace_id AND sku LIKE @pattern;
        """;

    await using (var cmd = new NpgsqlCommand(sql, conn, tx))
    {
        cmd.Parameters.AddWithValue("workspace_id", workspaceId);
        cmd.Parameters.AddWithValue("pattern", $"{baseSku}%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
                existing.Add(reader.GetString(0));
        }
    }

    if (!existing.Contains(baseSku))
        return baseSku;

    for (var suffix = 2; suffix < 10_000; suffix++)
    {
        var candidate = $"{baseSku}-{suffix:000}";
        if (!existing.Contains(candidate))
            return candidate;
    }

    return $"{baseSku}-{Guid.NewGuid():N}"[..Math.Min(64, baseSku.Length + 9)];
}

public static string NormalizeInvitationRole(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return "Team Member";

    var normalized = string.Join(" ", value
        .Trim()
        .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    if (normalized.Length < 2 || normalized.Length > 80)
        throw new ArgumentException("Role must be between 2 and 80 characters");

    return normalized;
}

public static string BuildInviteSuggestionReason(HashSet<string> sources)
{
    if (sources.Contains("invited_you"))
        return "Từng mời bạn vào workspace";

    if (sources.Contains("invited_by_you"))
        return "Bạn từng mời vào workspace";

    return "Từng làm việc cùng workspace";
}


public static async Task RecalculateTaskStoryPointsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int taskId)
{
    const string sql = """
        WITH vote_summary AS (
            SELECT COUNT(*)::int AS vote_count,
                   ROUND(AVG(points)::numeric, 0)::int AS rounded_story_points
            FROM task_story_point_votes
            WHERE task_id = @task_id
        )
        UPDATE tasks t
        SET story_points = CASE WHEN vs.vote_count = 0 THEN NULL ELSE vs.rounded_story_points END,
            updated_at = NOW()
        FROM vote_summary vs
        WHERE t.id = @task_id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    cmd.Parameters.AddWithValue("task_id", taskId);
    await cmd.ExecuteNonQueryAsync();
}

public static async Task<TaskResponse?> GetTaskResponseAsync(NpgsqlDataSource db, int taskId, int currentUserId)
{
    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT t.id, t.workspace_id, t.sku, t.title, t.description, t.due_date, t.due_at, t.story_points, t.priority, t.status, t.created_by,
               creator.id AS creator_id, creator.name AS creator_name,
               assignee.id AS assignee_id, assignee.name AS assignee_name,
               t.created_at,
               COALESCE(v.vote_count, 0) AS vote_count,
               v.average_points,
               uv.points AS my_vote_points
        FROM tasks t
        LEFT JOIN users creator ON creator.id = t.created_by
        LEFT JOIN users assignee ON assignee.id = t.assignee_id
        LEFT JOIN (
            SELECT task_id, COUNT(*)::int AS vote_count, ROUND(AVG(points)::numeric, 2) AS average_points
            FROM task_story_point_votes
            GROUP BY task_id
        ) v ON v.task_id = t.id
        LEFT JOIN task_story_point_votes uv ON uv.task_id = t.id AND uv.user_id = @current_user_id
        WHERE t.id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", taskId);
    cmd.Parameters.AddWithValue("current_user_id", currentUserId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return null;

    var dueDateValue = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5);
    var dueAtValue = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);

    UserBasicResponse? createdByUser = null;
    if (!reader.IsDBNull(11))
    {
        createdByUser = new UserBasicResponse(
            reader.GetInt32(11),
            reader.GetString(12));
    }

    UserBasicResponse? assignee = null;
    if (!reader.IsDBNull(13))
    {
        assignee = new UserBasicResponse(
            reader.GetInt32(13),
            reader.GetString(14));
    }

    return new TaskResponse(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.IsDBNull(2) ? $"TASK-{reader.GetInt32(0)}" : reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        dueAtValue.HasValue ? dueAtValue.Value.ToString("yyyy-MM-dd") : dueDateValue?.ToString("yyyy-MM-dd"),
        dueAtValue.HasValue ? ToIsoString(dueAtValue.Value) : null,
        reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetInt32(10),
        createdByUser,
        assignee,
        ToIsoString(reader.GetDateTime(15)),
        reader.GetInt32(16),
        reader.IsDBNull(17) ? null : Convert.ToDouble(reader.GetDecimal(17)),
        reader.IsDBNull(18) ? null : reader.GetInt32(18));
}

public static async Task<TaskStatsResponse> GetTaskStatsAsync(NpgsqlDataSource db, int workspaceId, int? assigneeId = null)
{
    await using var conn = await db.OpenConnectionAsync();

    var sql = """
        SELECT
            COUNT(*) AS total,
            COUNT(*) FILTER (WHERE status = 'pending') AS pending,
            COUNT(*) FILTER (WHERE status = 'in_progress') AS in_progress,
            COUNT(*) FILTER (WHERE status = 'completed') AS completed,
            COUNT(*) FILTER (WHERE COALESCE(due_at::date, due_date) < CURRENT_DATE AND status <> 'completed') AS overdue
        FROM tasks
        WHERE workspace_id = @workspace_id
        """;

    if (assigneeId.HasValue)
        sql += " AND assignee_id = @assignee_id";

    sql += ";";

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("workspace_id", workspaceId);

    if (assigneeId.HasValue)
        cmd.Parameters.AddWithValue("assignee_id", assigneeId.Value);

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();

    return new TaskStatsResponse(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt32(4));
}

public static async Task<string?> GetWorkspaceRoleAsync(NpgsqlDataSource db, int workspaceId, int userId)
{
    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT role
        FROM workspace_members
        WHERE workspace_id = @workspace_id AND user_id = @user_id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("workspace_id", workspaceId);
    cmd.Parameters.AddWithValue("user_id", userId);

    return (string?)await cmd.ExecuteScalarAsync();
}

public static async Task<string?> GetWorkspaceNameAsync(NpgsqlDataSource db, int workspaceId)
{
    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT name
        FROM workspaces
        WHERE id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", workspaceId);

    return (string?)await cmd.ExecuteScalarAsync();
}

public static async Task<UserBasicResponse?> GetUserBasicAsync(NpgsqlDataSource db, int userId)
{
    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT id, name
        FROM users
        WHERE id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", userId);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return null;

    return new UserBasicResponse(reader.GetInt32(0), reader.GetString(1));
}

public static async Task<TaskInfo?> GetTaskInfoAsync(NpgsqlDataSource db, int id)
{
    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT id, workspace_id, sku, title, description, due_date, due_at, story_points, priority, status, created_by, assignee_id
        FROM tasks
        WHERE id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return null;

    var dueDateValue = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5);
    var dueAtValue = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);

    if (!dueAtValue.HasValue && dueDateValue.HasValue)
        dueAtValue = DateTime.SpecifyKind(dueDateValue.Value.ToDateTime(new TimeOnly(23, 59, 0)), DateTimeKind.Utc);

    return new TaskInfo(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.IsDBNull(2) ? $"TASK-{reader.GetInt32(0)}" : reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        dueAtValue,
        reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetInt32(10),
        reader.IsDBNull(11) ? null : reader.GetInt32(11));
}

public static bool IsValidTaskStatus(string? status)
{
    return status is null || status is "pending" or "in_progress" or "completed";
}

public static bool IsValidTaskPriority(string? priority)
{
    return priority is null || priority is "low" or "medium" or "high" or "urgent";
}

public static string ToIsoString(DateTime value)
{
    var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    return utc.ToString("O");
}


public static string NormalizeOAuthProvider(string provider) => provider.Trim().ToLowerInvariant();

public static bool TryGetOAuthEndpoints(string provider, out OAuthProviderEndpoints endpoints)
{
    endpoints = provider switch
    {
        OAuthProviders.Google => new OAuthProviderEndpoints(
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            "https://openidconnect.googleapis.com/v1/userinfo"),
        OAuthProviders.GitHub => new OAuthProviderEndpoints(
            "https://github.com/login/oauth/authorize",
            "https://github.com/login/oauth/access_token",
            "https://api.github.com/user"),
        _ => default
    };

    return endpoints != default;
}

public static OAuthProviderSettings GetOAuthProviderSettings(IConfiguration config, string provider)
{
    var section = config.GetSection($"OAuth:{provider}");
    return new OAuthProviderSettings(
        section["ClientId"]?.Trim() ?? string.Empty,
        section["ClientSecret"]?.Trim() ?? string.Empty);
}

public static bool IsOAuthProviderConfigured(OAuthProviderSettings settings)
{
    return !string.IsNullOrWhiteSpace(settings.ClientId) && !string.IsNullOrWhiteSpace(settings.ClientSecret);
}

public static string GetApiBaseUrl(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

public static string GetFrontendBaseUrl(HttpContext http, IConfiguration config)
{
    return DeploymentSupport.ResolveConfiguredFrontendBaseUrl(config) ?? GetApiBaseUrl(http);
}

public static string BuildResetPasswordUrl(HttpContext http, IConfiguration config, string token)
{
    return QueryHelpers.AddQueryString($"{GetFrontendBaseUrl(http, config)}/reset-password", "token", token);
}

public static string BuildVerifyEmailUrl(HttpContext http, string token)
{
    return QueryHelpers.AddQueryString($"{GetApiBaseUrl(http)}/api/verify-email", "token", token);
}

public static string BuildLoginUrl(HttpContext http, IConfiguration config)
{
    return $"{GetFrontendBaseUrl(http, config)}/login";
}

public static string BuildOAuthRedirectUri(HttpContext http, string provider)
{
    return $"{GetApiBaseUrl(http)}/api/oauth/{provider}/callback";
}

public static string ResolveFrontendOriginFromRequest(HttpContext http, IConfiguration config)
{
    if (DeploymentSupport.TryNormalizeAbsoluteHttpUrl(http.Request.Headers.Origin.ToString(), out var origin))
        return origin;

    if (DeploymentSupport.TryNormalizeAbsoluteHttpUrl(http.Request.Headers.Referer.ToString(), out var referer))
        return referer;

    return GetFrontendBaseUrl(http, config);
}

public static string SanitizeFrontendOrigin(string? origin, string fallbackOrigin)
{
    return DeploymentSupport.TryNormalizeAbsoluteHttpUrl(origin, out var normalized)
        ? normalized
        : fallbackOrigin;
}

public static string SanitizeFrontendPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
        return "/oauth/callback";

    if (!path.StartsWith('/'))
        return "/oauth/callback";

    return path;
}

public static string BuildGoogleAuthorizationUrl(OAuthProviderSettings settings, string redirectUri, string state)
{
    var query = new Dictionary<string, string?>
    {
        ["client_id"] = settings.ClientId,
        ["redirect_uri"] = redirectUri,
        ["response_type"] = "code",
        ["scope"] = "openid email profile",
        ["state"] = state,
        ["access_type"] = "online",
        ["prompt"] = "select_account"
    };

    return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", query);
}

public static string BuildGitHubAuthorizationUrl(OAuthProviderSettings settings, string redirectUri, string state)
{
    var query = new Dictionary<string, string?>
    {
        ["client_id"] = settings.ClientId,
        ["redirect_uri"] = redirectUri,
        ["scope"] = "read:user user:email",
        ["state"] = state
    };

    return QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", query);
}

public static void ClearOAuthCookies(HttpContext http, string provider)
{
    var cookiePrefix = $"ewt_oauth_{provider}_";
    http.Response.Cookies.Delete($"{cookiePrefix}state");
    http.Response.Cookies.Delete($"{cookiePrefix}frontend");
    http.Response.Cookies.Delete($"{cookiePrefix}return_path");
}

public static string BuildFrontendOAuthSuccessUrl(string frontendOrigin, string returnPath, string code)
{
    return QueryHelpers.AddQueryString($"{frontendOrigin}{returnPath}", "code", code);
}

public static string BuildFrontendOAuthErrorUrl(string frontendOrigin, string returnPath, string? message)
{
    var query = new Dictionary<string, string?>
    {
        ["error"] = string.IsNullOrWhiteSpace(message) ? "OAuth sign-in failed." : message
    };

    return QueryHelpers.AddQueryString($"{frontendOrigin}{returnPath}", query);
}

public static bool CryptographicEquals(string expected, string actual)
{
    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
        return false;

    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    var actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);

    return expectedBytes.Length == actualBytes.Length &&
           CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
}

public static string CreateUrlSafeToken(int size)
{
    return Convert.ToHexString(RandomNumberGenerator.GetBytes(size)).ToLowerInvariant();
}

public static async Task<OAuthTokenResult> ExchangeGoogleCodeAsync(HttpClient client, OAuthProviderSettings settings, string code, string redirectUri)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        })
    };

    using var response = await client.SendAsync(request);
    var payload = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();

    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(payload?.AccessToken))
        throw new InvalidOperationException(payload?.ErrorDescription ?? payload?.Error ?? "Google token exchange failed.");

    return new OAuthTokenResult(payload.AccessToken);
}

public static async Task<OAuthTokenResult> ExchangeGitHubCodeAsync(HttpClient client, OAuthProviderSettings settings, string code, string redirectUri)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
    {
        Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        })
    };
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.UserAgent.ParseAdd("EasyWorkTogether-OAuth");

    using var response = await client.SendAsync(request);
    var payload = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>();

    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(payload?.AccessToken))
        throw new InvalidOperationException(payload?.ErrorDescription ?? payload?.Error ?? "GitHub token exchange failed.");

    return new OAuthTokenResult(payload.AccessToken);
}

public static async Task<OAuthUserInfo> GetGoogleUserInfoAsync(HttpClient client, string accessToken)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    using var response = await client.SendAsync(request);
    var payload = await response.Content.ReadFromJsonAsync<GoogleUserInfoResponse>();

    if (!response.IsSuccessStatusCode || payload is null)
        throw new InvalidOperationException("Google user info request failed.");

    if (payload.EmailVerified is false)
        throw new InvalidOperationException("Google account email must be verified.");

    return new OAuthUserInfo(
        payload.Sub ?? string.Empty,
        payload.Email?.Trim().ToLowerInvariant() ?? string.Empty,
        payload.Name?.Trim() ?? ExtractNameFromEmail(payload.Email));
}

public static async Task<OAuthUserInfo> GetGitHubUserInfoAsync(HttpClient client, string accessToken)
{
    using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
    userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    userRequest.Headers.UserAgent.ParseAdd("EasyWorkTogether-OAuth");
    userRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

    using var userResponse = await client.SendAsync(userRequest);
    var userPayload = await userResponse.Content.ReadFromJsonAsync<GitHubUserResponse>();

    if (!userResponse.IsSuccessStatusCode || userPayload is null || userPayload.Id <= 0)
        throw new InvalidOperationException("GitHub user info request failed.");

    using var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
    emailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    emailRequest.Headers.UserAgent.ParseAdd("EasyWorkTogether-OAuth");
    emailRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

    using var emailResponse = await client.SendAsync(emailRequest);
    var emailPayload = await emailResponse.Content.ReadFromJsonAsync<List<GitHubEmailResponse>>();

    if (!emailResponse.IsSuccessStatusCode || emailPayload is null)
        throw new InvalidOperationException("GitHub email request failed.");

    var bestEmail = emailPayload
        .Where(item => item.Verified && !string.IsNullOrWhiteSpace(item.Email))
        .OrderByDescending(item => item.Primary)
        .FirstOrDefault()
        ?.Email?.Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(bestEmail))
        throw new InvalidOperationException("GitHub account needs a verified email address.");

    return new OAuthUserInfo(
        userPayload.Id.ToString(),
        bestEmail,
        userPayload.Name?.Trim() ?? userPayload.Login?.Trim() ?? ExtractNameFromEmail(bestEmail));
}

public static string ExtractNameFromEmail(string? email)
{
    if (string.IsNullOrWhiteSpace(email))
        return "OAuth User";

    var local = email.Split('@', 2)[0].Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim();
    return string.IsNullOrWhiteSpace(local)
        ? "OAuth User"
        : string.Join(' ', local.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Capitalize));
}

public static string Capitalize(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return value;

    return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}

public static async Task<LoginResponse> LoginOrProvisionOAuthUserAsync(
    NpgsqlDataSource db,
    PasswordService passwordService,
    string provider,
    OAuthUserInfo userInfo,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    var normalizedEmail = userInfo.Email.Trim().ToLowerInvariant();

    const string identitySql = """
        SELECT u.id, u.email, u.name, u.avatar_url, u.public_user_id, u.is_system_admin
        FROM external_identities ei
        JOIN users u ON u.id = ei.user_id
        WHERE ei.provider = @provider AND ei.provider_user_id = @provider_user_id
        FOR UPDATE;
        """;

    await using var identityCmd = new NpgsqlCommand(identitySql, conn, tx);
    identityCmd.Parameters.AddWithValue("provider", provider);
    identityCmd.Parameters.AddWithValue("provider_user_id", userInfo.ProviderUserId);

    LoginUserResponse? loginUser = null;

    await using (var reader = await identityCmd.ExecuteReaderAsync())
    {
        if (await reader.ReadAsync())
        {
            loginUser = new LoginUserResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : FormatPublicUserId(reader.GetString(4)),
                reader.GetBoolean(5));
        }
    }

    if (loginUser is null)
    {
        const string existingUserSql = """
            SELECT id, email, name, avatar_url, public_user_id, is_system_admin
            FROM users
            WHERE email = @email
            FOR UPDATE;
            """;

        await using var existingUserCmd = new NpgsqlCommand(existingUserSql, conn, tx);
        existingUserCmd.Parameters.AddWithValue("email", normalizedEmail);

        await using (var reader = await existingUserCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                loginUser = new LoginUserResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : FormatPublicUserId(reader.GetString(4)),
                    reader.GetBoolean(5));
            }
        }
    }

    if (loginUser is null)
    {
        var grantSystemAdmin = await SystemAdminBootstrap.ShouldGrantSystemAdminOnRegistrationAsync(conn, tx, normalizedEmail, configuration, environment);

        const string createUserSql = """
            INSERT INTO users (email, password_hash, name, email_verified_at, public_user_id, is_system_admin, system_admin_granted_at)
            VALUES (@email, @password_hash, @name, NOW(), @public_user_id, @is_system_admin, CASE WHEN @is_system_admin THEN NOW() ELSE NULL END)
            RETURNING id, email, name, avatar_url, public_user_id, is_system_admin;
            """;

        await using var createUserCmd = new NpgsqlCommand(createUserSql, conn, tx);
        createUserCmd.Parameters.AddWithValue("email", normalizedEmail);
        createUserCmd.Parameters.AddWithValue("password_hash", passwordService.HashPassword(CreateUrlSafeToken(24)));
        createUserCmd.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(userInfo.Name) ? ExtractNameFromEmail(normalizedEmail) : userInfo.Name);
        createUserCmd.Parameters.AddWithValue("public_user_id", await GenerateUniquePublicUserIdAsync(conn, tx, userInfo.Name ?? normalizedEmail));
        createUserCmd.Parameters.AddWithValue("is_system_admin", grantSystemAdmin);

        await using var reader = await createUserCmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        loginUser = new LoginUserResponse(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : FormatPublicUserId(reader.GetString(4)),
            reader.GetBoolean(5));
    }

    const string markVerifiedSql = """
        UPDATE users
        SET email_verified_at = COALESCE(email_verified_at, NOW()), updated_at = NOW()
        WHERE id = @id;
        """;

    await using (var markVerifiedCmd = new NpgsqlCommand(markVerifiedSql, conn, tx))
    {
        markVerifiedCmd.Parameters.AddWithValue("id", loginUser.Id);
        await markVerifiedCmd.ExecuteNonQueryAsync();
    }

    const string upsertIdentitySql = """
        INSERT INTO external_identities (user_id, provider, provider_user_id, provider_email, last_login_at)
        VALUES (@user_id, @provider, @provider_user_id, @provider_email, NOW())
        ON CONFLICT (provider, provider_user_id)
        DO UPDATE SET provider_email = EXCLUDED.provider_email, last_login_at = NOW();
        """;

    await using (var upsertIdentityCmd = new NpgsqlCommand(upsertIdentitySql, conn, tx))
    {
        upsertIdentityCmd.Parameters.AddWithValue("user_id", loginUser.Id);
        upsertIdentityCmd.Parameters.AddWithValue("provider", provider);
        upsertIdentityCmd.Parameters.AddWithValue("provider_user_id", userInfo.ProviderUserId);
        upsertIdentityCmd.Parameters.AddWithValue("provider_email", normalizedEmail);
        await upsertIdentityCmd.ExecuteNonQueryAsync();
    }

    var sessionToken = Guid.NewGuid();
    var expiresAt = GetPersistentSessionExpiryUtc();

    const string sessionSql = """
        INSERT INTO sessions (token, user_id, expires_at)
        VALUES (@token, @user_id, @expires_at);
        """;

    await using (var sessionCmd = new NpgsqlCommand(sessionSql, conn, tx))
    {
        sessionCmd.Parameters.AddWithValue("token", sessionToken);
        sessionCmd.Parameters.AddWithValue("user_id", loginUser.Id);
        sessionCmd.Parameters.AddWithValue("expires_at", expiresAt);
        await sessionCmd.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    return new LoginResponse(loginUser, sessionToken, sessionToken);
}


    public static bool TryReadImageHeader(IFormFile file, int maxBytes, out byte[] header)
    {
        header = Array.Empty<byte>();
        try
        {
            using var stream = file.OpenReadStream();
            header = new byte[maxBytes];
            var read = stream.Read(header, 0, header.Length);
            if (read < header.Length)
                Array.Resize(ref header, read);
            return read > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool MatchesImageSignature(string extension, byte[] header)
    {
        extension = extension.ToLowerInvariant();
        return extension switch
        {
            ".png" => header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".gif" => header.Length >= 6 && Encoding.ASCII.GetString(header[..6]) is "GIF87a" or "GIF89a",
            ".webp" => header.Length >= 12 && Encoding.ASCII.GetString(header[..4]) == "RIFF" && Encoding.ASCII.GetString(header[8..12]) == "WEBP",
            ".svg" => Encoding.UTF8.GetString(header).Contains("<svg", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static bool CanManageWorkspaceInvitations(string? role) => role is "owner" or "admin";

    public static string NormalizeWorkspaceMemberRole(string? value, string fallback = "member")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "owner" => "owner",
            "admin" => "admin",
            "member" => "member",
            _ => fallback
        };
    }
}
