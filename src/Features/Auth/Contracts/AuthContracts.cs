namespace EasyWorkTogether.Api.Models;

public static class OAuthProviders
{
    public const string Google = "google";
    public const string GitHub = "github";
}

public readonly record struct OAuthProviderSettings(string ClientId, string ClientSecret);

public readonly record struct EmailSettings(bool Enabled, string FromName, string FromAddress, string SmtpHost, int SmtpPort, string Username, string Password, bool UseSsl);

public readonly record struct OAuthProviderEndpoints(string AuthorizationEndpoint, string TokenEndpoint, string UserInfoEndpoint);

public readonly record struct OAuthTokenResult(string AccessToken);

public readonly record struct OAuthUserInfo(string ProviderUserId, string Email, string Name);

public sealed class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

public sealed class GoogleUserInfoResponse
{
    [JsonPropertyName("sub")]
    public string? Sub { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("email_verified")]
    public bool? EmailVerified { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class GitHubTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

public sealed class GitHubUserResponse
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class GitHubEmailResponse
{
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("primary")]
    public bool Primary { get; init; }
}

public record RegisterRequest(string Email, string Password, string Name);

public record LoginRequest(string Email, string Password);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record UpdateProfileRequest(string? Name, string? AvatarUrl = null);

public record ChangePasswordRequest(string OldPassword, string NewPassword);

public record OAuthExchangeRequest(string Code);

public record UserResponse(int Id, string Email, string Name, string CreatedAt, string? AvatarUrl = null, string? PublicUserId = null, bool IsSystemAdmin = false);

public record ProfileBackendStatusResponse(string ApiMessage, string HealthStatus, string State, string CheckedAt);

public record ProfileSummaryResponse(UserResponse User, string AvatarLabel, ProfileBackendStatusResponse Backend);

public record LoginUserResponse(int Id, string Email, string Name, string? AvatarUrl = null, string? PublicUserId = null, bool IsSystemAdmin = false);

public record LoginResponse(LoginUserResponse User, Guid AccessToken, Guid SessionToken);

public record OAuthProviderAvailability(string Provider, bool Enabled, string StartUrl);

public record OAuthProvidersResponse(List<OAuthProviderAvailability> Providers);

public record ForgotPasswordResponse(string Message, string? ResetToken, string? ResetUrl, int ExpiresInMinutes);

public record RegisterResponse(string Message, bool VerificationEmailSent, string? VerificationToken, string? VerificationUrl, UserResponse User);

public record OAuthExchangeResponse(LoginUserResponse User, Guid AccessToken, Guid SessionToken);

public record WebSocketTicketResponse(string Ticket, int ExpiresInSeconds);

public record SessionUser(int Id, string Email, string Name, DateTime CreatedAt, string? AvatarUrl = null, string? PublicUserId = null, bool IsSystemAdmin = false);
