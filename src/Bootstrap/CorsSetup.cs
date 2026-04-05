namespace EasyWorkTogether.Api.Bootstrap;

public static class CorsSetup
{
    public static IServiceCollection AddApplicationCors(this IServiceCollection services, IHostEnvironment environment, string[] allowedOrigins)
    {
        var isDevelopment = environment.IsDevelopment();
        if (!isDevelopment && (allowedOrigins.Length == 0 || allowedOrigins.Contains("*", StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("Production CORS requires an explicit CORS_ALLOWED_ORIGINS allowlist. Wildcard origins are only allowed in development.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                if (isDevelopment && (allowedOrigins.Length == 0 || allowedOrigins.Contains("*", StringComparer.Ordinal)))
                {
                    policy
                        .SetIsOriginAllowed(_ => true)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                    return;
                }

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }
}
