using Microsoft.Extensions.Hosting;

namespace EasyWorkTogether.Api.Bootstrap;

public static class SystemAdminBootstrap
{
    public static async Task EnsureConfiguredSystemAdminsAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var configuredEmails = GetConfiguredSystemAdminEmails(configuration);
        if (configuredEmails.Count == 0)
            return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var conn = await db.OpenConnectionAsync(cancellationToken);

        const string sql = """
            UPDATE users
            SET is_system_admin = TRUE,
                system_admin_granted_at = COALESCE(system_admin_granted_at, NOW()),
                updated_at = NOW()
            WHERE LOWER(email) = ANY(@emails)
              AND is_system_admin = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("emails", configuredEmails.ToArray());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> ShouldGrantSystemAdminOnRegistrationAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string email,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return false;

        var configuredEmails = GetConfiguredSystemAdminEmails(configuration);
        if (configuredEmails.Contains(normalizedEmail))
            return true;

        if (!IsFirstUserBootstrapEnabled(configuration, environment))
            return false;

        const string sql = "SELECT COUNT(*) FROM users WHERE is_system_admin = TRUE;";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        return count == 0;
    }

    public static IReadOnlySet<string> GetConfiguredSystemAdminEmails(IConfiguration configuration)
    {
        var csv = configuration["SYSTEM_ADMIN_EMAILS"] ?? string.Empty;
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeEmail)
            .Where(static email => !string.IsNullOrWhiteSpace(email))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsFirstUserBootstrapEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var raw = configuration["ALLOW_FIRST_USER_SYSTEM_ADMIN_BOOTSTRAP"];
        if (bool.TryParse(raw, out var configured))
            return configured;

        return environment.IsDevelopment();
    }

    private static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
