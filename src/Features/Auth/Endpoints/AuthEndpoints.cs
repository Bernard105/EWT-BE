using EasyWorkTogether.Api.Shared.Infrastructure.Auth;
using EasyWorkTogether.Api.Services;

namespace EasyWorkTogether.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api");
        publicApi.MapPost("/register", RegisterAsync);
        publicApi.MapPost("/login", LoginAsync);
        publicApi.MapPost("/forgot-password", ForgotPasswordAsync);
        publicApi.MapPost("/reset-password", ResetPasswordAsync);
        publicApi.MapGet("/verify-email", VerifyEmailAsync);
        publicApi.MapGet("/oauth/providers", GetOAuthProvidersAsync);
        publicApi.MapGet("/oauth/{provider}/start", StartOAuthAsync);
        publicApi.MapGet("/oauth/{provider}/callback", CompleteOAuthAsync);
        publicApi.MapPost("/oauth/exchange", ExchangeOAuthCodeAsync);

        var authApi = app.MapGroup("/api");
        authApi.AddEndpointFilter<RequireSessionFilter>();
        authApi.MapPost("/logout", LogoutAsync);
        authApi.MapGet("/profile", GetProfileAsync);
        authApi.MapGet("/profile/summary", GetProfileSummaryAsync);
        authApi.MapPut("/profile", UpdateProfileAsync);
        authApi.MapPost("/change-password", ChangePasswordAsync);
        authApi.MapPost("/realtime/ws-ticket", CreateWebSocketTicketAsync);
        // Temporarily disabled until UploadImageAsync is implemented
        // authApi.MapPost("/uploads/images", UploadImageAsync);
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext http,
        RegisterRequest request,
        NpgsqlDataSource db,
        PasswordService passwordService,
        EmailService emailService,
        IConfiguration config,
        IHostEnvironment environment)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var name = NormalizeHumanName(request.Name ?? string.Empty);

        if (string.IsNullOrWhiteSpace(email))
            return Results.BadRequest(new ErrorResponse("Email is required"));

        var nameError = ValidateHumanName(name);
        if (nameError is not null)
            return Results.BadRequest(new ErrorResponse(nameError));

        var passwordError = ValidateStrongPassword(request.Password ?? string.Empty);
        if (passwordError is not null)
            return Results.BadRequest(new ErrorResponse(passwordError));

        var passwordHash = passwordService.HashPassword(request.Password ?? string.Empty);
        var emailVerifiedAt = DateTime.UtcNow;

        await CleanupExpiredSessionsAsync(db);
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var publicUserId = await GenerateUniquePublicUserIdAsync(conn, tx, name ?? email, http.RequestAborted);

        var grantSystemAdmin = await SystemAdminBootstrap.ShouldGrantSystemAdminOnRegistrationAsync(conn, tx, email, config, environment, http.RequestAborted);

        const string sql = """
        INSERT INTO users (email, password_hash, name, email_verified_at, public_user_id, is_system_admin, system_admin_granted_at)
        VALUES (@email, @password_hash, @name, @email_verified_at, @public_user_id, @is_system_admin, CASE WHEN @is_system_admin THEN NOW() ELSE NULL END)
        RETURNING id, email, name, created_at, public_user_id, is_system_admin;
        """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("password_hash", passwordHash);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("email_verified_at", emailVerifiedAt);
        cmd.Parameters.AddWithValue("public_user_id", publicUserId);
        cmd.Parameters.AddWithValue("is_system_admin", grantSystemAdmin);

        try
        {
            UserResponse response;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();

                response = new UserResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    ToIsoString(reader.GetDateTime(3)),
                    null,
                    FormatPublicUserId(reader.GetString(4)),
                    reader.GetBoolean(5));
            }

            await tx.CommitAsync();

            return Results.Created($"/api/users/{response.Id}", new RegisterResponse(
                "Account created successfully. You can now log in.",
                false,
                null,
                null,
                response));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync();
            return Results.BadRequest(new ErrorResponse("Email already exists"));
        }
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, NpgsqlDataSource db, PasswordService passwordService, IConfiguration config)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;

        await using var conn = await db.OpenConnectionAsync();

        const string userSql = """
        SELECT id, email, name, password_hash, avatar_url, public_user_id, is_system_admin
        FROM users
        WHERE email = @email;
        """;

        await using var userCmd = new NpgsqlCommand(userSql, conn);
        userCmd.Parameters.AddWithValue("email", email);

        await using var reader = await userCmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return Results.BadRequest(new ErrorResponse("Invalid email or password"));

        var userId = reader.GetInt32(0);
        var userEmail = reader.GetString(1);
        var userName = reader.GetString(2);
        var passwordHash = reader.GetString(3);
        var userAvatarUrl = reader.IsDBNull(4) ? null : reader.GetString(4);
        var publicUserId = reader.IsDBNull(5) ? null : FormatPublicUserId(reader.GetString(5));
        var isSystemAdmin = reader.GetBoolean(6);

        if (!passwordService.VerifyPassword(password, passwordHash))
            return Results.BadRequest(new ErrorResponse("Invalid email or password"));

        await reader.CloseAsync();

        var sessionToken = Guid.NewGuid();
        var expiresAt = GetPersistentSessionExpiryUtc(config);

        const string sessionSql = """
        INSERT INTO sessions (token, user_id, expires_at)
        VALUES (@token, @user_id, @expires_at);
        """;

        await using var sessionCmd = new NpgsqlCommand(sessionSql, conn);
        sessionCmd.Parameters.AddWithValue("token", sessionToken);
        sessionCmd.Parameters.AddWithValue("user_id", userId);
        sessionCmd.Parameters.AddWithValue("expires_at", expiresAt);
        await sessionCmd.ExecuteNonQueryAsync();

        return Results.Ok(new LoginResponse(
            new LoginUserResponse(userId, userEmail, userName, userAvatarUrl, publicUserId, isSystemAdmin),
            sessionToken,
            sessionToken));
    }

    private static async Task<IResult> ForgotPasswordAsync(HttpContext http, ForgotPasswordRequest request, NpgsqlDataSource db, EmailService emailService, IConfiguration config)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
            return Results.BadRequest(new ErrorResponse("Email is required"));

        await using var conn = await db.OpenConnectionAsync();

        const string userSql = """
        SELECT id
        FROM users
        WHERE email = @email;
        """;

        await using var userCmd = new NpgsqlCommand(userSql, conn);
        userCmd.Parameters.AddWithValue("email", email);

        var userIdValue = await userCmd.ExecuteScalarAsync();
        if (userIdValue is null)
        {
            return Results.Ok(new ForgotPasswordResponse(
                "If the account exists, a reset link has been prepared.",
                null,
                null,
                60));
        }

        var userId = (int)userIdValue;
        var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash = ComputeSha256(resetToken);
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        await using var tx = await conn.BeginTransactionAsync();

        const string invalidateSql = """
        UPDATE password_reset_tokens
        SET used_at = NOW()
        WHERE user_id = @user_id AND used_at IS NULL;
        """;

        await using (var invalidateCmd = new NpgsqlCommand(invalidateSql, conn, tx))
        {
            invalidateCmd.Parameters.AddWithValue("user_id", userId);
            await invalidateCmd.ExecuteNonQueryAsync();
        }

        const string insertSql = """
        INSERT INTO password_reset_tokens (user_id, token_hash, expires_at)
        VALUES (@user_id, @token_hash, @expires_at);
        """;

        await using (var insertCmd = new NpgsqlCommand(insertSql, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("user_id", userId);
            insertCmd.Parameters.AddWithValue("token_hash", tokenHash);
            insertCmd.Parameters.AddWithValue("expires_at", expiresAt);
            await insertCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        var resetUrl = BuildResetPasswordUrl(http, config, resetToken);
        var emailSent = await emailService.TrySendResetPasswordEmailAsync(email, resetUrl, expiresAt);

        return emailSent
            ? Results.Ok(new ForgotPasswordResponse(
                "If the account exists, a password reset email has been sent.",
                null,
                null,
                60))
            : Results.Ok(new ForgotPasswordResponse(
                "Reset link created. Gmail SMTP is not configured or email sending failed, so the link is returned directly.",
                resetToken,
                resetUrl,
                60));
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, NpgsqlDataSource db, PasswordService passwordService)
    {
        var token = (request.Token ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new ErrorResponse("Reset token is required"));

        var passwordError = ValidateStrongPassword(request.NewPassword ?? string.Empty, "New password");
        if (passwordError is not null)
            return Results.BadRequest(new ErrorResponse(passwordError));

        var tokenHash = ComputeSha256(token);

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string tokenSql = """
        SELECT id, user_id
        FROM password_reset_tokens
        WHERE token_hash = @token_hash
          AND used_at IS NULL
          AND expires_at > NOW()
        FOR UPDATE;
        """;

        await using var tokenCmd = new NpgsqlCommand(tokenSql, conn, tx);
        tokenCmd.Parameters.AddWithValue("token_hash", tokenHash);

        int resetTokenId;
        int userId;

        await using (var reader = await tokenCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return Results.BadRequest(new ErrorResponse("Reset token is invalid or expired"));

            resetTokenId = reader.GetInt32(0);
            userId = reader.GetInt32(1);
        }

        const string updateUserSql = """
        UPDATE users
        SET password_hash = @password_hash, updated_at = NOW()
        WHERE id = @id;
        """;

        await using (var updateUserCmd = new NpgsqlCommand(updateUserSql, conn, tx))
        {
            updateUserCmd.Parameters.AddWithValue("password_hash", passwordService.HashPassword(request.NewPassword ?? string.Empty));
            updateUserCmd.Parameters.AddWithValue("id", userId);
            await updateUserCmd.ExecuteNonQueryAsync();
        }

        const string useTokenSql = """
        UPDATE password_reset_tokens
        SET used_at = NOW()
        WHERE id = @id;
        """;

        await using (var useTokenCmd = new NpgsqlCommand(useTokenSql, conn, tx))
        {
            useTokenCmd.Parameters.AddWithValue("id", resetTokenId);
            await useTokenCmd.ExecuteNonQueryAsync();
        }

        const string invalidateOtherSql = """
        UPDATE password_reset_tokens
        SET used_at = NOW()
        WHERE user_id = @user_id AND used_at IS NULL;
        """;

        await using (var invalidateOtherCmd = new NpgsqlCommand(invalidateOtherSql, conn, tx))
        {
            invalidateOtherCmd.Parameters.AddWithValue("user_id", userId);
            await invalidateOtherCmd.ExecuteNonQueryAsync();
        }

        const string deleteSessionsSql = """
        DELETE FROM sessions
        WHERE user_id = @user_id;
        """;

        await using (var deleteSessionsCmd = new NpgsqlCommand(deleteSessionsSql, conn, tx))
        {
            deleteSessionsCmd.Parameters.AddWithValue("user_id", userId);
            await deleteSessionsCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return Results.Ok(new MessageResponse("Password reset successfully"));
    }


    private static async Task<IResult> VerifyEmailAsync(HttpContext http, string token, NpgsqlDataSource db, IConfiguration config)
    {
        token = token.Trim();

        if (string.IsNullOrWhiteSpace(token))
            return Results.Redirect(QueryHelpers.AddQueryString(BuildLoginUrl(http, config), "verify_error", "Verification token is missing."));

        var tokenHash = ComputeSha256(token);

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string tokenSql = """
        SELECT id, user_id
        FROM email_verification_tokens
        WHERE token_hash = @token_hash AND used_at IS NULL AND expires_at > NOW()
        FOR UPDATE;
        """;

        await using var tokenCmd = new NpgsqlCommand(tokenSql, conn, tx);
        tokenCmd.Parameters.AddWithValue("token_hash", tokenHash);

        int verificationTokenId;
        int userId;

        await using (var reader = await tokenCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return Results.Redirect(QueryHelpers.AddQueryString(BuildLoginUrl(http, config), "verify_error", "Verification link is invalid or expired."));

            verificationTokenId = reader.GetInt32(0);
            userId = reader.GetInt32(1);
        }

        const string verifyUserSql = """
        UPDATE users
        SET email_verified_at = COALESCE(email_verified_at, NOW()), updated_at = NOW()
        WHERE id = @id;
        """;

        await using (var verifyUserCmd = new NpgsqlCommand(verifyUserSql, conn, tx))
        {
            verifyUserCmd.Parameters.AddWithValue("id", userId);
            await verifyUserCmd.ExecuteNonQueryAsync();
        }

        const string useTokenSql = """
        UPDATE email_verification_tokens
        SET used_at = NOW()
        WHERE id = @id;
        """;

        await using (var useTokenCmd = new NpgsqlCommand(useTokenSql, conn, tx))
        {
            useTokenCmd.Parameters.AddWithValue("id", verificationTokenId);
            await useTokenCmd.ExecuteNonQueryAsync();
        }

        const string invalidateOtherSql = """
        UPDATE email_verification_tokens
        SET used_at = NOW()
        WHERE user_id = @user_id AND used_at IS NULL;
        """;

        await using (var invalidateOtherCmd = new NpgsqlCommand(invalidateOtherSql, conn, tx))
        {
            invalidateOtherCmd.Parameters.AddWithValue("user_id", userId);
            await invalidateOtherCmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        return Results.Redirect(QueryHelpers.AddQueryString(BuildLoginUrl(http, config), "verified", "1"));
    }


    private static IResult GetOAuthProvidersAsync(HttpContext http, IConfiguration config)
    {
        var frontendOrigin = ResolveFrontendOriginFromRequest(http, config);
        var returnPath = SanitizeFrontendPath(http.Request.Query["return_path"].ToString());
        var providers = new List<OAuthProviderAvailability>();

        foreach (var provider in new[] { OAuthProviders.Google, OAuthProviders.GitHub })
        {
            var settings = GetOAuthProviderSettings(config, provider);
            var startUrl = QueryHelpers.AddQueryString(
                $"{GetApiBaseUrl(http)}/api/oauth/{provider}/start",
                new Dictionary<string, string?>
                {
                    ["frontend_origin"] = frontendOrigin,
                    ["return_path"] = returnPath
                });

            providers.Add(new OAuthProviderAvailability(
                provider,
                IsOAuthProviderConfigured(settings),
                startUrl));
        }

        return Results.Ok(new OAuthProvidersResponse(providers));
    }

    private static IResult StartOAuthAsync(string provider, HttpContext http, IConfiguration config)
    {
        provider = NormalizeOAuthProvider(provider);

        if (!TryGetOAuthEndpoints(provider, out _))
            return Results.NotFound(new ErrorResponse("OAuth provider is not supported"));

        var settings = GetOAuthProviderSettings(config, provider);
        if (!IsOAuthProviderConfigured(settings))
            return Results.BadRequest(new ErrorResponse($"{provider} OAuth is not configured"));

        var fallbackFrontendOrigin = GetFrontendBaseUrl(http, config);
        var frontendOrigin = SanitizeFrontendOrigin(http.Request.Query["frontend_origin"].ToString(), fallbackFrontendOrigin);
        var returnPath = SanitizeFrontendPath(http.Request.Query["return_path"].ToString());

        var state = CreateUrlSafeToken(32);
        var cookiePrefix = $"ewt_oauth_{provider}_";

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddMinutes(10)
        };

        http.Response.Cookies.Append($"{cookiePrefix}state", state, cookieOptions);
        http.Response.Cookies.Append($"{cookiePrefix}frontend", frontendOrigin, cookieOptions);
        http.Response.Cookies.Append($"{cookiePrefix}return_path", returnPath, cookieOptions);

        var redirectUri = BuildOAuthRedirectUri(http, provider);
        var authorizationUrl = provider switch
        {
            OAuthProviders.Google => BuildGoogleAuthorizationUrl(settings, redirectUri, state),
            OAuthProviders.GitHub => BuildGitHubAuthorizationUrl(settings, redirectUri, state),
            _ => throw new InvalidOperationException("Unsupported OAuth provider")
        };

        return Results.Redirect(authorizationUrl);
    }

    private static async Task<IResult> CompleteOAuthAsync(
        string provider,
        HttpContext http,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        NpgsqlDataSource db,
        PasswordService passwordService,
        ILoggerFactory loggerFactory)
    {
        provider = NormalizeOAuthProvider(provider);
        var logger = loggerFactory.CreateLogger("OAuthEndpoints");

        if (!TryGetOAuthEndpoints(provider, out _))
            return Results.NotFound(new ErrorResponse("OAuth provider is not supported"));

        var settings = GetOAuthProviderSettings(config, provider);
        if (!IsOAuthProviderConfigured(settings))
            return Results.BadRequest(new ErrorResponse($"{provider} OAuth is not configured"));

        var cookiePrefix = $"ewt_oauth_{provider}_";
        var expectedState = http.Request.Cookies[$"{cookiePrefix}state"];
        var fallbackFrontendOrigin = GetFrontendBaseUrl(http, config);
        var frontendOrigin = SanitizeFrontendOrigin(http.Request.Cookies[$"{cookiePrefix}frontend"], fallbackFrontendOrigin);
        var returnPath = SanitizeFrontendPath(http.Request.Cookies[$"{cookiePrefix}return_path"]);

        ClearOAuthCookies(http, provider);

        if (!string.IsNullOrWhiteSpace(http.Request.Query["error"]))
        {
            var providerError = http.Request.Query["error_description"].ToString();
            if (string.IsNullOrWhiteSpace(providerError))
                providerError = http.Request.Query["error"].ToString();

            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, providerError));
        }

        var state = http.Request.Query["state"].ToString();
        if (string.IsNullOrWhiteSpace(expectedState) || !CryptographicEquals(expectedState, state))
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, "OAuth state is invalid or expired. Please try again."));

        var code = http.Request.Query["code"].ToString();
        if (string.IsNullOrWhiteSpace(code))
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, "Authorization code is missing."));

        var redirectUri = BuildOAuthRedirectUri(http, provider);
        var client = httpClientFactory.CreateClient();

        OAuthTokenResult tokenResult;
        try
        {
            tokenResult = provider switch
            {
                OAuthProviders.Google => await ExchangeGoogleCodeAsync(client, settings, code, redirectUri),
                OAuthProviders.GitHub => await ExchangeGitHubCodeAsync(client, settings, code, redirectUri),
                _ => throw new InvalidOperationException("Unsupported OAuth provider")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OAuth token exchange failed for provider {Provider}", provider);
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, ex.Message));
        }

        OAuthUserInfo userInfo;
        try
        {
            userInfo = provider switch
            {
                OAuthProviders.Google => await GetGoogleUserInfoAsync(client, tokenResult.AccessToken),
                OAuthProviders.GitHub => await GetGitHubUserInfoAsync(client, tokenResult.AccessToken),
                _ => throw new InvalidOperationException("Unsupported OAuth provider")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OAuth user info request failed for provider {Provider}", provider);
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, ex.Message));
        }

        if (string.IsNullOrWhiteSpace(userInfo.Email))
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, "The provider did not return a usable email address."));

        try
        {
            var loginResponse = await LoginOrProvisionOAuthUserAsync(db, passwordService, provider, userInfo, config, http.RequestServices.GetRequiredService<IHostEnvironment>());
            var handoffCode = CreateUrlSafeToken(24);

            await using var conn = await db.OpenConnectionAsync();
            const string handoffSql = """
            INSERT INTO oauth_handoff_codes (code, user_id, session_token, expires_at)
            VALUES (@code, @user_id, @session_token, @expires_at);
            """;
            await using var handoffCmd = new NpgsqlCommand(handoffSql, conn);
            handoffCmd.Parameters.AddWithValue("code", handoffCode);
            handoffCmd.Parameters.AddWithValue("user_id", loginResponse.User.Id);
            handoffCmd.Parameters.AddWithValue("session_token", loginResponse.SessionToken);
            handoffCmd.Parameters.AddWithValue("expires_at", DateTime.UtcNow.AddSeconds(60));
            await handoffCmd.ExecuteNonQueryAsync();

            return Results.Redirect(BuildFrontendOAuthSuccessUrl(frontendOrigin, returnPath, handoffCode));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OAuth login or provisioning failed for provider {Provider}", provider);
            return Results.Redirect(BuildFrontendOAuthErrorUrl(frontendOrigin, returnPath, ex.Message));
        }
    }

    private static async Task<IResult> ExchangeOAuthCodeAsync(OAuthExchangeRequest request, NpgsqlDataSource db)
    {
        var code = (request.Code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
            return Results.BadRequest(new ErrorResponse("OAuth code is required"));

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string sql = """
        SELECT oh.session_token, u.id, u.email, u.name, u.avatar_url, u.public_user_id, u.is_system_admin
        FROM oauth_handoff_codes oh
        JOIN sessions s ON s.token = oh.session_token AND s.expires_at > NOW()
        JOIN users u ON u.id = oh.user_id
        WHERE oh.code = @code AND oh.used_at IS NULL AND oh.expires_at > NOW()
        FOR UPDATE;
        """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("code", code);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.BadRequest(new ErrorResponse("OAuth handoff code is invalid or expired"));

        var sessionToken = reader.GetGuid(0);
        var response = new OAuthExchangeResponse(
            new LoginUserResponse(
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : FormatPublicUserId(reader.GetString(5)),
                reader.GetBoolean(6)),
            sessionToken,
            sessionToken);
        await reader.CloseAsync();

        await using var markCmd = new NpgsqlCommand("UPDATE oauth_handoff_codes SET used_at = NOW() WHERE code = @code;", conn, tx);
        markCmd.Parameters.AddWithValue("code", code);
        await markCmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();

        return Results.Ok(response);
    }

    private static async Task<IResult> CreateWebSocketTicketAsync(HttpContext http, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var ticket = CreateUrlSafeToken(24);
        var expiresAt = DateTime.UtcNow.AddSeconds(60);

        await using var conn = await db.OpenConnectionAsync();
        const string sql = """
        INSERT INTO ws_tickets (ticket, user_id, expires_at)
        VALUES (@ticket, @user_id, @expires_at);
        """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ticket", ticket);
        cmd.Parameters.AddWithValue("user_id", currentUser.Id);
        cmd.Parameters.AddWithValue("expires_at", expiresAt);
        await cmd.ExecuteNonQueryAsync();

        return Results.Ok(new WebSocketTicketResponse(ticket, 60));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, NpgsqlDataSource db)
    {
        var token = AuthTokenHelper.GetBearerToken(http);
        if (token is null)
            return Results.Unauthorized();

        await using var conn = await db.OpenConnectionAsync();

        const string sql = """
        DELETE FROM sessions
        WHERE token = @token;
        """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("token", token.Value);
        await cmd.ExecuteNonQueryAsync();

        return Results.NoContent();
    }

    private static IResult GetProfileAsync(HttpContext http)
    {
        var user = http.GetCurrentUser();

        return Results.Ok(new UserResponse(
            user.Id,
            user.Email,
            user.Name,
            ToIsoString(user.CreatedAt),
            user.AvatarUrl,
            user.PublicUserId,
            user.IsSystemAdmin));
    }

    private static IResult GetProfileSummaryAsync(HttpContext http)
    {
        var user = http.GetCurrentUser();
        var profile = new UserResponse(
            user.Id,
            user.Email,
            user.Name,
            ToIsoString(user.CreatedAt),
            user.AvatarUrl,
            user.PublicUserId,
            user.IsSystemAdmin);

        return Results.Ok(new ProfileSummaryResponse(
            profile,
            BuildAvatarLabel(user.Name, user.Email),
            new ProfileBackendStatusResponse(
                "Task backend is running",
                "ok",
                "Connected",
                ToIsoString(DateTime.UtcNow))));
    }

    private static async Task<IResult> UpdateProfileAsync(HttpContext http, UpdateProfileRequest request, NpgsqlDataSource db)
    {
        var currentUser = http.GetCurrentUser();
        var newName = request.Name is null ? currentUser.Name : NormalizeHumanName(request.Name);

        if (request.Name is not null)
        {
            var nameError = ValidateHumanName(newName);
            if (nameError is not null)
                return Results.BadRequest(new ErrorResponse(nameError));
        }

        string? avatarUrl;
        try
        {
            avatarUrl = request.AvatarUrl is null
                ? currentUser.AvatarUrl
                : NormalizeProfileAvatarUrl(request.AvatarUrl);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        await using var conn = await db.OpenConnectionAsync();

        const string sql = """
        UPDATE users
        SET name = @name,
            avatar_url = @avatar_url,
            updated_at = NOW()
        WHERE id = @id
        RETURNING id, email, name, created_at, avatar_url, public_user_id;
        """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", newName);
        cmd.Parameters.AddWithValue("avatar_url", (object?)avatarUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", currentUser.Id);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        var response = new UserResponse(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            ToIsoString(reader.GetDateTime(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : FormatPublicUserId(reader.GetString(5)),
            currentUser.IsSystemAdmin);

        return Results.Ok(response);
    }

    private static async Task<IResult> ChangePasswordAsync(HttpContext http, ChangePasswordRequest request, NpgsqlDataSource db, PasswordService passwordService)
    {
        var passwordError = ValidateStrongPassword(request.NewPassword ?? string.Empty, "New password");
        if (passwordError is not null)
            return Results.BadRequest(new ErrorResponse(passwordError));

        var currentUser = http.GetCurrentUser();

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string getSql = """
        SELECT password_hash
        FROM users
        WHERE id = @id;
        """;

        await using var getCmd = new NpgsqlCommand(getSql, conn, tx);
        getCmd.Parameters.AddWithValue("id", currentUser.Id);

        var currentHash = (string?)await getCmd.ExecuteScalarAsync();
        if (currentHash is null || !passwordService.VerifyPassword(request.OldPassword ?? string.Empty, currentHash))
            return Results.BadRequest(new ErrorResponse("Old password is incorrect"));

        const string updateSql = """
        UPDATE users
        SET password_hash = @password_hash, updated_at = NOW()
        WHERE id = @id;
        """;

        await using var updateCmd = new NpgsqlCommand(updateSql, conn, tx);
        updateCmd.Parameters.AddWithValue("password_hash", passwordService.HashPassword(request.NewPassword ?? string.Empty));
        updateCmd.Parameters.AddWithValue("id", currentUser.Id);
        await updateCmd.ExecuteNonQueryAsync();

        const string deleteSessionsSql = """
        DELETE FROM sessions
        WHERE user_id = @user_id;
        """;

        await using var deleteCmd = new NpgsqlCommand(deleteSessionsSql, conn, tx);
        deleteCmd.Parameters.AddWithValue("user_id", currentUser.Id);
        await deleteCmd.ExecuteNonQueryAsync();

        await tx.CommitAsync();

        return Results.Ok(new MessageResponse("Password changed successfully"));
    }
}
