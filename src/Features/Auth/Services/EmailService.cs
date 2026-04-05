namespace EasyWorkTogether.Api.Services;

public sealed class EmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _logger = logger;

        var section = config.GetSection("Email");
        _settings = new EmailSettings(
            bool.TryParse(section["Enabled"], out var enabled) && enabled,
            section["FromName"]?.Trim() ?? "EasyWorkTogether",
            section["FromAddress"]?.Trim() ?? string.Empty,
            section["SmtpHost"]?.Trim() ?? "smtp.gmail.com",
            int.TryParse(section["SmtpPort"], out var port) ? port : 587,
            section["Username"]?.Trim() ?? string.Empty,
            section["Password"]?.Trim() ?? string.Empty,
            !bool.TryParse(section["UseSsl"], out var useSsl) || useSsl);
    }

    public bool IsConfigured =>
        _settings.Enabled &&
        !string.IsNullOrWhiteSpace(_settings.FromAddress) &&
        !string.IsNullOrWhiteSpace(_settings.SmtpHost) &&
        !string.IsNullOrWhiteSpace(_settings.Username) &&
        !string.IsNullOrWhiteSpace(_settings.Password);

    public Task<bool> TrySendResetPasswordEmailAsync(string toEmail, string resetUrl, DateTime expiresAtUtc)
    {
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        var body = $"""
            <p>Hello,</p>
            <p>We received a request to reset your EasyWorkTogether password.</p>
            <p><a href="{safeUrl}">Click here to reset your password</a></p>
            <p>This link expires at <strong>{expiresAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC</strong>.</p>
            <p>If you did not request this, you can ignore this email.</p>
            """;

        return TrySendAsync(toEmail, "EasyWorkTogether password reset", body);
    }

    public Task<bool> TrySendEmailVerificationAsync(string toEmail, string name, string verificationUrl, DateTime expiresAtUtc)
    {
        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(name) ? "there" : name);
        var safeUrl = WebUtility.HtmlEncode(verificationUrl);
        var body = $"""
            <p>Hello {safeName},</p>
            <p>Welcome to EasyWorkTogether.</p>
            <p>Please verify your email address before logging in.</p>
            <p><a href="{safeUrl}">Verify my email</a></p>
            <p>This link expires at <strong>{expiresAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC</strong>.</p>
            <p>If you did not create this account, you can ignore this email.</p>
            """;

        return TrySendAsync(toEmail, "Verify your EasyWorkTogether email", body);
    }

    public Task<bool> TrySendWorkspaceInvitationEmailAsync(string toEmail, string workspaceName, string inviterName, string code, DateTime expiresAtUtc, string loginUrl, string? role = null)
    {
        var safeWorkspace = WebUtility.HtmlEncode(workspaceName);
        var safeInviter = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(inviterName) ? "A workspace owner" : inviterName);
        var safeCode = WebUtility.HtmlEncode(code);
        var safeLoginUrl = WebUtility.HtmlEncode(loginUrl);
        var safeRole = string.IsNullOrWhiteSpace(role) ? null : WebUtility.HtmlEncode(role.Trim());
        var roleBlock = safeRole is null ? string.Empty : $"<p><strong>Assigned role:</strong> {safeRole}</p>";
        var body = $"""
            <p>Hello,</p>
            <p>{safeInviter} invited you to join <strong>{safeWorkspace}</strong> on EasyWorkTogether.</p>
            <p>Your invitation code is:</p>
            <p><strong style="font-size:18px;letter-spacing:1px;">{safeCode}</strong></p>
            {roleBlock}
            <p>Please sign in with Google, GitHub, or your email account, then paste the code in the workspace screen.</p>
            <p><a href="{safeLoginUrl}">Open EasyWorkTogether</a></p>
            <p>This invitation expires at <strong>{expiresAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC</strong>.</p>
            """;

        return TrySendAsync(toEmail, $"Invitation to join {workspaceName}", body);
    }

    private async Task<bool> TrySendAsync(string toEmail, string subject, string htmlBody)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(toEmail))
            return false;

        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(_settings.FromAddress, _settings.FromName);
            message.To.Add(new MailAddress(toEmail));
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;

            using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password)
            };

            await client.SendMailAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }
}

