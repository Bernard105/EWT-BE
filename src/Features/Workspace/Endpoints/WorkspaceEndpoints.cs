using EasyWorkTogether.Api.Shared.Infrastructure.Auth;
using EasyWorkTogether.Api.Services;

namespace EasyWorkTogether.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var authApi = app.MapGroup("/api");
        authApi.AddEndpointFilter<RequireSessionFilter>();

        authApi.MapPost("/workspaces", CreateWorkspaceAsync);
        authApi.MapGet("/workspaces", ListWorkspacesAsync);
        authApi.MapGet("/workspaces/invite-suggestions", ListWorkspaceInviteSuggestionsAsync);
        authApi.MapGet("/workspaces/{id:int}/invitations/suggestions", ListWorkspaceInviteSuggestionsByWorkspaceAsync);
        authApi.MapPut("/workspaces/{id:int}", UpdateWorkspaceAsync);
        authApi.MapDelete("/workspaces/{id:int}", DeleteWorkspaceAsync);
        authApi.MapGet("/workspaces/{id:int}/members", ListWorkspaceMembersAsync);
        authApi.MapGet("/workspaces/{id:int}/members/{memberId:int}", GetWorkspaceMemberAsync);
        authApi.MapPut("/workspaces/{id:int}/members/{memberId:int}", UpdateWorkspaceMemberRoleAsync);
        authApi.MapDelete("/workspaces/{id:int}/members/{memberId:int}", RemoveWorkspaceMemberAsync);
        authApi.MapPost("/workspaces/{id:int}/transfer-ownership", TransferWorkspaceOwnershipAsync);
        authApi.MapGet("/workspaces/{id:int}/invitations", ListWorkspaceInvitationsAsync);
        authApi.MapPost("/workspaces/{id:int}/invitations", CreateInvitationAsync);
        authApi.MapPost("/workspaces/{workspaceId:int}/invitations/{invitationId:int}/resend", ResendInvitationAsync);
        authApi.MapPost("/workspaces/{workspaceId:int}/invitations/{invitationId:int}/revoke", RevokeInvitationAsync);
        authApi.MapGet("/invitations/pending", ListMyPendingInvitationsAsync);
        authApi.MapGet("/invitations/{id:int}", GetInvitationAsync);
        authApi.MapPost("/invitations/accept", AcceptInvitationAsync);
        authApi.MapPost("/invitations/{id:int}/decline", DeclineInvitationAsync);
    }

private static async Task<IResult> CreateWorkspaceAsync(HttpContext http, CreateWorkspaceRequest request, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();
    var name = NormalizeWorkspaceName(request.Name);

    var nameError = ValidateWorkspaceName(name);
    if (nameError is not null)
        return Results.BadRequest(new ErrorResponse(nameError));

    string? domainNamespace;
    string? industryVertical;
    string? workspaceLogoData;

    try
    {
        domainNamespace = NormalizeDomainNamespace(request.DomainNamespace) ?? NormalizeDomainNamespace(name);
        industryVertical = NormalizeIndustryVertical(request.IndustryVertical);
        workspaceLogoData = NormalizeWorkspaceLogoData(request.WorkspaceLogoData);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse(ex.Message));
    }

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    const string workspaceSql = """
        INSERT INTO workspaces (name, owner_id, domain_namespace, industry_vertical, workspace_logo_data)
        VALUES (@name, @owner_id, @domain_namespace, @industry_vertical, @workspace_logo_data)
        RETURNING id, name, owner_id, created_at, domain_namespace, industry_vertical, workspace_logo_data;
        """;

    await using var workspaceCmd = new NpgsqlCommand(workspaceSql, conn, tx);
    workspaceCmd.Parameters.AddWithValue("name", name);
    workspaceCmd.Parameters.AddWithValue("owner_id", currentUser.Id);
    workspaceCmd.Parameters.AddWithValue("domain_namespace", (object?)domainNamespace ?? DBNull.Value);
    workspaceCmd.Parameters.AddWithValue("industry_vertical", (object?)industryVertical ?? DBNull.Value);
    workspaceCmd.Parameters.AddWithValue("workspace_logo_data", (object?)workspaceLogoData ?? DBNull.Value);

    int workspaceId;
    string workspaceName;
    int ownerId;
    DateTime createdAt;
    string? createdDomainNamespace;
    string? createdIndustryVertical;
    string? createdWorkspaceLogoData;

    await using (var reader = await workspaceCmd.ExecuteReaderAsync())
    {
        await reader.ReadAsync();
        workspaceId = reader.GetInt32(0);
        workspaceName = reader.GetString(1);
        ownerId = reader.GetInt32(2);
        createdAt = reader.GetDateTime(3);
        createdDomainNamespace = reader.IsDBNull(4) ? null : reader.GetString(4);
        createdIndustryVertical = reader.IsDBNull(5) ? null : reader.GetString(5);
        createdWorkspaceLogoData = reader.IsDBNull(6) ? null : reader.GetString(6);
    }

    const string memberSql = """
        INSERT INTO workspace_members (workspace_id, user_id, role)
        VALUES (@workspace_id, @user_id, 'owner');
        """;

    await using var memberCmd = new NpgsqlCommand(memberSql, conn, tx);
    memberCmd.Parameters.AddWithValue("workspace_id", workspaceId);
    memberCmd.Parameters.AddWithValue("user_id", currentUser.Id);
    await memberCmd.ExecuteNonQueryAsync();

    await tx.CommitAsync();

    return Results.Created($"/api/workspaces/{workspaceId}", new WorkspaceResponse(
        workspaceId,
        workspaceName,
        ownerId,
        ToIsoString(createdAt),
        null,
        createdDomainNamespace,
        createdIndustryVertical,
        createdWorkspaceLogoData));
}

private static async Task<IResult> ListWorkspacesAsync(HttpContext http, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT w.id, w.name, wm.role, w.domain_namespace, w.industry_vertical, w.workspace_logo_data
        FROM workspace_members wm
        JOIN workspaces w ON w.id = wm.workspace_id
        WHERE wm.user_id = @user_id
        ORDER BY w.id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("user_id", currentUser.Id);

    var items = new List<WorkspaceListItem>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new WorkspaceListItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5)));
    }

    return Results.Ok(new WorkspaceListResponse(items));
}

private static async Task<IResult> ListWorkspaceMembersAsync(HttpContext http, int id, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT u.id, u.name, u.email, wm.role, wm.joined_at, u.avatar_url, u.public_user_id
        FROM workspace_members wm
        JOIN users u ON u.id = wm.user_id
        WHERE wm.workspace_id = @workspace_id
        ORDER BY CASE wm.role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, LOWER(u.name), u.id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("workspace_id", id);

    var items = new List<WorkspaceMemberResponse>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new WorkspaceMemberResponse(
            reader.GetInt32(0),
            reader.GetString(1),
            null,
            reader.GetString(3),
            ToIsoString(reader.GetDateTime(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : FormatPublicUserId(reader.GetString(6)),
            MaskEmailAddress(reader.GetString(2))));
    }

    return Results.Ok(new WorkspaceMembersResponse(items));
}

private static async Task<IResult> ListWorkspaceInvitationsAsync(HttpContext http, int id, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT wi.id,
               wi.invitee_email,
               invitee.name AS invitee_name,
               wi.status,
               COALESCE(NULLIF(BTRIM(wi.role), ''), 'Team Member') AS role,
               wi.expires_at,
               wi.created_at,
               wi.responded_at,
               inviter.id AS inviter_id,
               inviter.name AS inviter_name
        FROM workspace_invitations wi
        JOIN users inviter ON inviter.id = wi.inviter_id
        LEFT JOIN users invitee ON LOWER(invitee.email) = LOWER(wi.invitee_email)
        WHERE wi.workspace_id = @workspace_id
        ORDER BY
            CASE wi.status WHEN 'pending' THEN 0 ELSE 1 END,
            wi.created_at DESC,
            wi.id DESC;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("workspace_id", id);

    var items = new List<WorkspaceInvitationListItem>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new WorkspaceInvitationListItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ToIsoString(reader.GetDateTime(5)),
            ToIsoString(reader.GetDateTime(6)),
            reader.IsDBNull(7) ? null : ToIsoString(reader.GetDateTime(7)),
            new UserBasicResponse(reader.GetInt32(8), reader.GetString(9)),
            MaskEmailAddress(reader.GetString(1))));
    }

    return Results.Ok(new WorkspaceInvitationsResponse(items));
}

private static async Task<IResult> ListWorkspaceInviteSuggestionsAsync(HttpContext http, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT contact_user_id,
               contact_email,
               contact_name,
               source_label,
               interaction_count
        FROM (
            SELECT invited.id AS contact_user_id,
                   COALESCE(invited.email, wi.invitee_email) AS contact_email,
                   COALESCE(invited.name, split_part(wi.invitee_email, '@', 1)) AS contact_name,
                   'invited_by_you' AS source_label,
                   COUNT(*)::INT AS interaction_count
            FROM workspace_invitations wi
            LEFT JOIN users invited ON LOWER(invited.email) = LOWER(wi.invitee_email)
            WHERE wi.inviter_id = @user_id
            GROUP BY invited.id, COALESCE(invited.email, wi.invitee_email), COALESCE(invited.name, split_part(wi.invitee_email, '@', 1))

            UNION ALL

            SELECT inviter.id AS contact_user_id,
                   inviter.email AS contact_email,
                   inviter.name AS contact_name,
                   'invited_you' AS source_label,
                   COUNT(*)::INT AS interaction_count
            FROM workspace_invitations wi
            JOIN users inviter ON inviter.id = wi.inviter_id
            WHERE LOWER(wi.invitee_email) = LOWER(@user_email)
            GROUP BY inviter.id, inviter.email, inviter.name

            UNION ALL

            SELECT teammate.id AS contact_user_id,
                   teammate.email AS contact_email,
                   teammate.name AS contact_name,
                   'shared_workspace' AS source_label,
                   COUNT(*)::INT AS interaction_count
            FROM workspace_members self_member
            JOIN workspace_members other_member ON other_member.workspace_id = self_member.workspace_id AND other_member.user_id <> self_member.user_id
            JOIN users teammate ON teammate.id = other_member.user_id
            WHERE self_member.user_id = @user_id
            GROUP BY teammate.id, teammate.email, teammate.name
        ) interactions
        WHERE contact_email IS NOT NULL
          AND LOWER(contact_email) <> LOWER(@user_email);
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("user_id", currentUser.Id);
    cmd.Parameters.AddWithValue("user_email", currentUser.Email);

    var aggregates = new Dictionary<string, InviteSuggestionAggregate>(StringComparer.OrdinalIgnoreCase);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var email = reader.GetString(1);
        if (string.IsNullOrWhiteSpace(email))
            continue;

        if (!aggregates.TryGetValue(email, out var aggregate))
        {
            aggregate = new InviteSuggestionAggregate
            {
                UserId = reader.IsDBNull(0) ? null : reader.GetInt32(0),
                Email = email,
                Name = reader.GetString(2)
            };
            aggregates[email] = aggregate;
        }
        else if (!aggregate.UserId.HasValue && !reader.IsDBNull(0))
        {
            aggregate.UserId = reader.GetInt32(0);
        }

        var source = reader.GetString(3);
        var interactionCount = reader.GetInt32(4);
        aggregate.Score += interactionCount * (source == "shared_workspace" ? 1 : 2);
        aggregate.Sources.Add(source);
    }

    var suggestions = aggregates.Values
        .OrderByDescending(item => item.Score)
        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .Take(2)
        .Select(item => new WorkspaceInviteSuggestionResponse(
            item.UserId,
            item.Email,
            item.Name,
            item.Score,
            BuildInviteSuggestionReason(item.Sources)))
        .ToList();

    return Results.Ok(new WorkspaceInviteSuggestionListResponse(suggestions));
}

private static async Task<IResult> UpdateWorkspaceAsync(HttpContext http, int id, CreateWorkspaceRequest request, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var name = NormalizeWorkspaceName(request.Name);

    var nameError = ValidateWorkspaceName(name);
    if (nameError is not null)
        return Results.BadRequest(new ErrorResponse(nameError));

    string? domainNamespace;
    string? industryVertical;
    string? workspaceLogoData;

    try
    {
        domainNamespace = NormalizeDomainNamespace(request.DomainNamespace) ?? NormalizeDomainNamespace(name);
        industryVertical = NormalizeIndustryVertical(request.IndustryVertical);
        workspaceLogoData = NormalizeWorkspaceLogoData(request.WorkspaceLogoData);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse(ex.Message));
    }

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        UPDATE workspaces
        SET name = @name,
            domain_namespace = @domain_namespace,
            industry_vertical = @industry_vertical,
            workspace_logo_data = @workspace_logo_data,
            updated_at = NOW()
        WHERE id = @id
        RETURNING id, name, owner_id, updated_at, domain_namespace, industry_vertical, workspace_logo_data;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("name", name);
    cmd.Parameters.AddWithValue("domain_namespace", (object?)domainNamespace ?? DBNull.Value);
    cmd.Parameters.AddWithValue("industry_vertical", (object?)industryVertical ?? DBNull.Value);
    cmd.Parameters.AddWithValue("workspace_logo_data", (object?)workspaceLogoData ?? DBNull.Value);
    cmd.Parameters.AddWithValue("id", id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    return Results.Ok(new WorkspaceUpdateResponse(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetInt32(2),
        ToIsoString(reader.GetDateTime(3)),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6)));
}

private static async Task<IResult> DeleteWorkspaceAsync(HttpContext http, int id, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        DELETE FROM workspaces
        WHERE id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    await cmd.ExecuteNonQueryAsync();

    return Results.NoContent();
}

private static async Task<IResult> ListMyPendingInvitationsAsync(HttpContext http, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT wi.id,
               wi.workspace_id,
               w.name,
               wi.invitee_email,
               COALESCE(NULLIF(BTRIM(wi.role), ''), 'member') AS role,
               wi.expires_at,
               wi.created_at,
               inviter.id AS inviter_id,
               inviter.name AS inviter_name
        FROM workspace_invitations wi
        JOIN workspaces w ON w.id = wi.workspace_id
        JOIN users inviter ON inviter.id = wi.inviter_id
        WHERE wi.invitee_email = @invitee_email
          AND wi.status = 'pending'
          AND wi.expires_at >= NOW()
          AND NOT EXISTS (
              SELECT 1
              FROM workspace_members wm
              WHERE wm.workspace_id = wi.workspace_id
                AND wm.user_id = @user_id
          )
        ORDER BY wi.created_at DESC, wi.id DESC;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("invitee_email", currentUser.Email.Trim().ToLowerInvariant());
    cmd.Parameters.AddWithValue("user_id", currentUser.Id);

    var items = new List<PendingInvitationListItem>();

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new PendingInvitationListItem(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            ToIsoString(reader.GetDateTime(5)),
            ToIsoString(reader.GetDateTime(6)),
            new UserBasicResponse(reader.GetInt32(7), reader.GetString(8)),
            MaskEmailAddress(reader.GetString(3))));
    }

    return Results.Ok(new PendingInvitationsResponse(items));
}

private static async Task<IResult> DeclineInvitationAsync(HttpContext http, int id, NpgsqlDataSource db)
{
    var currentUser = http.GetCurrentUser();

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    const string sql = """
        SELECT invitee_email, status, expires_at
        FROM workspace_invitations
        WHERE id = @id
        FOR UPDATE;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    cmd.Parameters.AddWithValue("id", id);

    string inviteeEmail;
    string status;
    DateTime expiresAt;

    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("Invitation not found"));

        inviteeEmail = reader.GetString(0);
        status = reader.GetString(1);
        expiresAt = reader.GetDateTime(2);
    }

    if (!string.Equals(inviteeEmail, currentUser.Email, StringComparison.OrdinalIgnoreCase))
        return Results.Json(new ErrorResponse("You cannot decline this invitation"), statusCode: StatusCodes.Status403Forbidden);

    if (status != "pending")
        return Results.BadRequest(new ErrorResponse("Invitation is no longer valid"));

    if (expiresAt < DateTime.UtcNow)
        return Results.BadRequest(new ErrorResponse("Invitation has expired"));

    const string updateSql = """
        UPDATE workspace_invitations
        SET status = 'declined',
            responded_at = COALESCE(responded_at, NOW())
        WHERE id = @id;
        """;

    await using (var updateCmd = new NpgsqlCommand(updateSql, conn, tx))
    {
        updateCmd.Parameters.AddWithValue("id", id);
        await updateCmd.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();

    return Results.Ok(new MessageResponse("Invitation declined"));
}

private static async Task<IResult> CreateInvitationAsync(HttpContext http, int id, InviteRequest request, NpgsqlDataSource db, EmailService emailService, IConfiguration config, NotificationService notifications)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email))
        return Results.BadRequest(new ErrorResponse("Email is required"));

    try
    {
        _ = new System.Net.Mail.MailAddress(email);
    }
    catch
    {
        return Results.BadRequest(new ErrorResponse("Email is invalid"));
    }

    string invitationRole;
    try
    {
        invitationRole = NormalizeWorkspaceMemberRole(request.Role, "member");
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ErrorResponse(ex.Message));
    }

    await using var conn = await db.OpenConnectionAsync();

    const string memberCheckSql = """
        SELECT 1
        FROM workspace_members wm
        JOIN users u ON u.id = wm.user_id
        WHERE wm.workspace_id = @workspace_id AND u.email = @email;
        """;

    await using (var memberCmd = new NpgsqlCommand(memberCheckSql, conn))
    {
        memberCmd.Parameters.AddWithValue("workspace_id", id);
        memberCmd.Parameters.AddWithValue("email", email);

        var alreadyMember = await memberCmd.ExecuteScalarAsync();
        if (alreadyMember is not null)
            return Results.BadRequest(new ErrorResponse("User is already a workspace member"));
    }

    var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
    var expiresAt = GetPersistentSessionExpiryUtc();

    const string inviteSql = """
        INSERT INTO workspace_invitations (workspace_id, inviter_id, invitee_email, code, role, expires_at)
        VALUES (@workspace_id, @inviter_id, @invitee_email, @code, @role, @expires_at)
        RETURNING id, invitee_email, COALESCE(NULLIF(BTRIM(role), ''), 'member'), expires_at;
        """;

    await using var inviteCmd = new NpgsqlCommand(inviteSql, conn);
    inviteCmd.Parameters.AddWithValue("workspace_id", id);
    inviteCmd.Parameters.AddWithValue("inviter_id", currentUser.Id);
    inviteCmd.Parameters.AddWithValue("invitee_email", email);
    inviteCmd.Parameters.AddWithValue("code", code);
    inviteCmd.Parameters.AddWithValue("role", invitationRole);
    inviteCmd.Parameters.AddWithValue("expires_at", expiresAt);

    var workspaceName = await GetWorkspaceNameAsync(db, id) ?? $"Workspace #{id}";

    int invitationId;
    string inviteeEmail;
    string persistedInvitationRole;
    DateTime invitationExpiresAt;

    await using (var reader = await inviteCmd.ExecuteReaderAsync())
    {
        await reader.ReadAsync();
        invitationId = reader.GetInt32(0);
        inviteeEmail = reader.GetString(1);
        persistedInvitationRole = reader.GetString(2);
        invitationExpiresAt = reader.GetDateTime(3);
    }

    var invitationLandingUrl = BuildLoginUrl(http, config);
    await emailService.TrySendWorkspaceInvitationEmailAsync(
        inviteeEmail,
        workspaceName,
        currentUser.Name,
        code,
        invitationExpiresAt,
        invitationLandingUrl,
        persistedInvitationRole);

    await notifications.CreateAsync(
        currentUser.Id,
        "workspace_invitation_created",
        "Workspace access sent",
        $"Workspace access sent to {MaskEmailAddress(inviteeEmail) ?? inviteeEmail} for {workspaceName}.",
        currentUser.Id,
        "workspace",
        id,
        new Dictionary<string, string?>
        {
            ["workspace_id"] = id.ToString(),
            ["workspace_name"] = workspaceName,
            ["invitation_id"] = invitationId.ToString(),
            ["invitee_email_hint"] = MaskEmailAddress(inviteeEmail)
        },
        $"workspace-invitation-created:{invitationId}",
        http.RequestAborted);

    return Results.Created($"/api/workspaces/{id}/invitations", new InvitationResponse(
        invitationId,
        inviteeEmail,
        persistedInvitationRole,
        ToIsoString(invitationExpiresAt),
        MaskEmailAddress(inviteeEmail)));
}

private static async Task<IResult> ResendInvitationAsync(HttpContext http, int workspaceId, int invitationId, NpgsqlDataSource db, EmailService emailService, IConfiguration config, NotificationService notifications)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, workspaceId, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var conn = await db.OpenConnectionAsync();

    const string sql = """
        SELECT wi.invitee_email,
               wi.code,
               wi.expires_at,
               wi.status,
               COALESCE(NULLIF(BTRIM(wi.role), ''), 'Team Member') AS role,
               w.name
        FROM workspace_invitations wi
        JOIN workspaces w ON w.id = wi.workspace_id
        WHERE wi.id = @invitation_id
          AND wi.workspace_id = @workspace_id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("invitation_id", invitationId);
    cmd.Parameters.AddWithValue("workspace_id", workspaceId);

    string inviteeEmail;
    string invitationCode;
    DateTime expiresAt;
    string invitationStatus;
    string invitationRole;
    string workspaceName;

    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("Invitation not found"));

        inviteeEmail = reader.GetString(0);
        invitationCode = reader.GetString(1);
        expiresAt = reader.GetDateTime(2);
        invitationStatus = reader.GetString(3);
        invitationRole = reader.GetString(4);
        workspaceName = reader.GetString(5);
    }

    if (invitationStatus != "pending")
        return Results.BadRequest(new ErrorResponse("Only pending invitations can be resent"));

    if (expiresAt < DateTime.UtcNow)
        return Results.BadRequest(new ErrorResponse("Invitation has expired"));

    var invitationLandingUrl = BuildLoginUrl(http, config);
    await emailService.TrySendWorkspaceInvitationEmailAsync(
        inviteeEmail,
        workspaceName,
        currentUser.Name,
        invitationCode,
        expiresAt,
        invitationLandingUrl,
        invitationRole);

    await notifications.CreateAsync(
        currentUser.Id,
        "workspace_invitation_resent",
        "Workspace access resent",
        $"Workspace access was resent to {MaskEmailAddress(inviteeEmail) ?? inviteeEmail} for {workspaceName}.",
        currentUser.Id,
        "workspace",
        workspaceId,
        new Dictionary<string, string?>
        {
            ["workspace_id"] = workspaceId.ToString(),
            ["workspace_name"] = workspaceName,
            ["invitation_id"] = invitationId.ToString(),
            ["invitee_email_hint"] = MaskEmailAddress(inviteeEmail)
        },
        $"workspace-invitation-resent:{invitationId}:{expiresAt:O}",
        http.RequestAborted);

    return Results.Ok(new MessageResponse("Invitation resent"));
}

private static async Task<IResult> RevokeInvitationAsync(HttpContext http, int workspaceId, int invitationId, NpgsqlDataSource db, NotificationService notifications)
{
    var currentUser = http.GetCurrentUser();
    var role = await GetWorkspaceRoleAsync(db, workspaceId, currentUser.Id);

    if (role is null)
        return Results.NotFound(new ErrorResponse("Workspace not found"));

    if (!CanManageWorkspaceInvitations(role))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    await using var conn = await db.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    const string sql = """
        SELECT status
        FROM workspace_invitations
        WHERE id = @invitation_id
          AND workspace_id = @workspace_id
        FOR UPDATE;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    cmd.Parameters.AddWithValue("invitation_id", invitationId);
    cmd.Parameters.AddWithValue("workspace_id", workspaceId);

    string invitationStatus;

    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("Invitation not found"));

        invitationStatus = reader.GetString(0);
    }

    if (invitationStatus != "pending")
        return Results.BadRequest(new ErrorResponse("Invitation is no longer pending"));

    const string updateSql = """
        UPDATE workspace_invitations
        SET status = 'revoked',
            responded_at = COALESCE(responded_at, NOW())
        WHERE id = @invitation_id
          AND workspace_id = @workspace_id;
        """;

    await using (var updateCmd = new NpgsqlCommand(updateSql, conn, tx))
    {
        updateCmd.Parameters.AddWithValue("invitation_id", invitationId);
        updateCmd.Parameters.AddWithValue("workspace_id", workspaceId);
        await updateCmd.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();

    await notifications.CreateAsync(
        currentUser.Id,
        "workspace_invitation_revoked",
        "Workspace access revoked",
        $"Pending workspace access invitation #{invitationId} was revoked.",
        currentUser.Id,
        "workspace",
        workspaceId,
        new Dictionary<string, string?>
        {
            ["workspace_id"] = workspaceId.ToString(),
            ["invitation_id"] = invitationId.ToString()
        },
        $"workspace-invitation-revoked:{invitationId}",
        http.RequestAborted);

    return Results.Ok(new MessageResponse("Invitation revoked"));
}


    private static async Task<IResult> GetInvitationAsync(HttpContext http, int id, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();

        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
            SELECT wi.id, wi.workspace_id, w.name, wi.invitee_email,
                   COALESCE(NULLIF(BTRIM(wi.role), ''), 'Team Member') AS role,
                   wi.status, wi.expires_at, wi.created_at, wi.responded_at,
                   inviter.id AS inviter_id, inviter.name AS inviter_name
            FROM workspace_invitations wi
            JOIN workspaces w ON w.id = wi.workspace_id
            JOIN users inviter ON inviter.id = wi.inviter_id
            WHERE wi.id = @id;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("Invitation not found"));

        var workspaceId = reader.GetInt32(1);
        var inviteeEmail = reader.GetString(3);
        var role = await GetWorkspaceRoleAsync(db, workspaceId, currentUser.Id);
        if (role is null && !string.Equals(inviteeEmail, currentUser.Email, StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return Results.Ok(new
        {
            id = reader.GetInt32(0),
            workspace_id = workspaceId,
            workspace_name = reader.GetString(2),
            invitee_email = inviteeEmail,
            invitee_email_hint = MaskEmailAddress(inviteeEmail),
            role = reader.GetString(4),
            status = reader.GetString(5),
            expires_at = ToIsoString(reader.GetDateTime(6)),
            created_at = ToIsoString(reader.GetDateTime(7)),
            responded_at = reader.IsDBNull(8) ? null : ToIsoString(reader.GetDateTime(8)),
            inviter = new UserBasicResponse(reader.GetInt32(9), reader.GetString(10))
        });
    }

    private static async Task<IResult> AcceptInvitationAsync(HttpContext http, AcceptInvitationRequest request, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToLowerInvariant();
        var invitationId = request.InvitationId;

        if (invitationId is null && string.IsNullOrWhiteSpace(code))
            return Results.BadRequest(new ErrorResponse("Invitation reference is required"));

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var sql = invitationId.HasValue
            ? """
            SELECT id, workspace_id, invitee_email, expires_at, status, COALESCE(NULLIF(BTRIM(role), ''), 'member')
            FROM workspace_invitations
            WHERE id = @invitation_id
            FOR UPDATE;
            """
            : """
            SELECT id, workspace_id, invitee_email, expires_at, status, COALESCE(NULLIF(BTRIM(role), ''), 'member')
            FROM workspace_invitations
            WHERE code = @code
            FOR UPDATE;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        if (invitationId.HasValue)
            cmd.Parameters.AddWithValue("invitation_id", invitationId.Value);
        else
            cmd.Parameters.AddWithValue("code", code!);

        int resolvedInvitationId;
        int workspaceId;
        string inviteeEmail;
        DateTime expiresAt;
        string status;
        string invitationRole;

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return Results.BadRequest(new ErrorResponse("Invitation is invalid or no longer available"));
            resolvedInvitationId = reader.GetInt32(0);
            workspaceId = reader.GetInt32(1);
            inviteeEmail = reader.GetString(2);
            expiresAt = reader.GetDateTime(3);
            status = reader.GetString(4);
            invitationRole = reader.GetString(5);
        }

        if (status != "pending")
            return Results.BadRequest(new ErrorResponse("Invitation is no longer valid"));
        if (expiresAt < DateTime.UtcNow)
            return Results.BadRequest(new ErrorResponse("Invitation has expired"));
        if (!string.Equals(inviteeEmail, currentUser.Email, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new ErrorResponse("Invitation email does not match current user"));

        const string memberCheckSql = """
            SELECT role FROM workspace_members WHERE workspace_id = @workspace_id AND user_id = @user_id;
            """;
        await using (var memberCheckCmd = new NpgsqlCommand(memberCheckSql, conn, tx))
        {
            memberCheckCmd.Parameters.AddWithValue("workspace_id", workspaceId);
            memberCheckCmd.Parameters.AddWithValue("user_id", currentUser.Id);
            var existingRole = await memberCheckCmd.ExecuteScalarAsync();
            if (existingRole is not null)
                return Results.BadRequest(new ErrorResponse("User is already a workspace member"));
        }

        var memberRole = NormalizeWorkspaceMemberRole(invitationRole, "member");
        if (memberRole == "owner")
            memberRole = "member";

        const string insertMemberSql = """
            INSERT INTO workspace_members (workspace_id, user_id, role)
            VALUES (@workspace_id, @user_id, @role);
            """;
        await using (var insertCmd = new NpgsqlCommand(insertMemberSql, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("workspace_id", workspaceId);
            insertCmd.Parameters.AddWithValue("user_id", currentUser.Id);
            insertCmd.Parameters.AddWithValue("role", memberRole);
            await insertCmd.ExecuteNonQueryAsync();
        }

        const string updateInvitationSql = """
            UPDATE workspace_invitations
            SET status = 'accepted', responded_at = COALESCE(responded_at, NOW())
            WHERE id = @id;
            """;
        await using (var updateCmd = new NpgsqlCommand(updateInvitationSql, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("id", resolvedInvitationId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        const string workspaceSql = """
            SELECT name FROM workspaces WHERE id = @id;
            """;
        string workspaceName;
        await using (var workspaceCmd = new NpgsqlCommand(workspaceSql, conn, tx))
        {
            workspaceCmd.Parameters.AddWithValue("id", workspaceId);
            workspaceName = (string)(await workspaceCmd.ExecuteScalarAsync())!;
        }

        await tx.CommitAsync();
        return Results.Ok(new AcceptInvitationResponse(workspaceId, workspaceName, memberRole));
    }

    private static async Task<IResult> GetWorkspaceMemberAsync(HttpContext http, int id, int memberId, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));

        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
            SELECT u.id, u.name, u.email, wm.role, wm.joined_at, u.avatar_url, u.public_user_id
            FROM workspace_members wm
            JOIN users u ON u.id = wm.user_id
            WHERE wm.workspace_id = @workspace_id AND wm.user_id = @user_id;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("workspace_id", id);
        cmd.Parameters.AddWithValue("user_id", memberId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.NotFound(new ErrorResponse("Member not found"));

        return Results.Ok(new WorkspaceMemberResponse(reader.GetInt32(0), reader.GetString(1), null, reader.GetString(3), ToIsoString(reader.GetDateTime(4)), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : FormatPublicUserId(reader.GetString(6)), MaskEmailAddress(reader.GetString(2))));
    }

    private static async Task<IResult> UpdateWorkspaceMemberRoleAsync(HttpContext http, int id, int memberId, UpdateWorkspaceMemberRoleRequest request, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));
        if (role != "owner")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (memberId == currentUser.Id)
            return Results.BadRequest(new ErrorResponse("Use transfer ownership to change the owner's role"));

        var newRole = NormalizeWorkspaceMemberRole(request.Role);
        if (newRole == "owner")
            return Results.BadRequest(new ErrorResponse("Use transfer ownership to assign owner role"));

        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
            UPDATE workspace_members
            SET role = @role
            WHERE workspace_id = @workspace_id AND user_id = @user_id
            RETURNING workspace_id;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("role", newRole);
        cmd.Parameters.AddWithValue("workspace_id", id);
        cmd.Parameters.AddWithValue("user_id", memberId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null)
            return Results.NotFound(new ErrorResponse("Member not found"));

        return await GetWorkspaceMemberAsync(http, id, memberId, db);
    }

    private static async Task<IResult> RemoveWorkspaceMemberAsync(HttpContext http, int id, int memberId, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));
        if (role != "owner")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (memberId == currentUser.Id)
            return Results.BadRequest(new ErrorResponse("Owner cannot remove themselves from the workspace"));

        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
            DELETE FROM workspace_members
            WHERE workspace_id = @workspace_id AND user_id = @user_id;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("workspace_id", id);
        cmd.Parameters.AddWithValue("user_id", memberId);
        var count = await cmd.ExecuteNonQueryAsync();
        if (count == 0)
            return Results.NotFound(new ErrorResponse("Member not found"));

        return Results.NoContent();
    }

    private static async Task<IResult> TransferWorkspaceOwnershipAsync(HttpContext http, int id, TransferWorkspaceOwnershipRequest request, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));
        if (role != "owner")
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.NewOwnerUserId == currentUser.Id)
            return Results.BadRequest(new ErrorResponse("The selected user is already the owner"));

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string memberSql = """
            SELECT role FROM workspace_members WHERE workspace_id = @workspace_id AND user_id = @user_id FOR UPDATE;
            """;
        await using (var memberCmd = new NpgsqlCommand(memberSql, conn, tx))
        {
            memberCmd.Parameters.AddWithValue("workspace_id", id);
            memberCmd.Parameters.AddWithValue("user_id", request.NewOwnerUserId);
            var existingRole = (string?)await memberCmd.ExecuteScalarAsync();
            if (existingRole is null)
                return Results.BadRequest(new ErrorResponse("New owner must already be a workspace member"));
        }

        await using (var workspaceCmd = new NpgsqlCommand("UPDATE workspaces SET owner_id = @owner_id, updated_at = NOW() WHERE id = @id;", conn, tx))
        {
            workspaceCmd.Parameters.AddWithValue("owner_id", request.NewOwnerUserId);
            workspaceCmd.Parameters.AddWithValue("id", id);
            await workspaceCmd.ExecuteNonQueryAsync();
        }
        await using (var demoteCmd = new NpgsqlCommand("UPDATE workspace_members SET role = 'member' WHERE workspace_id = @workspace_id AND user_id = @user_id;", conn, tx))
        {
            demoteCmd.Parameters.AddWithValue("workspace_id", id);
            demoteCmd.Parameters.AddWithValue("user_id", currentUser.Id);
            await demoteCmd.ExecuteNonQueryAsync();
        }
        await using (var promoteCmd = new NpgsqlCommand("UPDATE workspace_members SET role = 'owner' WHERE workspace_id = @workspace_id AND user_id = @user_id;", conn, tx))
        {
            promoteCmd.Parameters.AddWithValue("workspace_id", id);
            promoteCmd.Parameters.AddWithValue("user_id", request.NewOwnerUserId);
            await promoteCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return Results.Ok(new MessageResponse("Workspace ownership transferred successfully"));
    }

    private static async Task<IResult> ListWorkspaceInviteSuggestionsByWorkspaceAsync(HttpContext http, int id, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var role = await GetWorkspaceRoleAsync(db, id, currentUser.Id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("Workspace not found"));
        return await ListWorkspaceInviteSuggestionsAsync(http, db);
    }
}
