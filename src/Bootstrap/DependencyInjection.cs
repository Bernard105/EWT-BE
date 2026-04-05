using EasyWorkTogether.Api.Services;
using EasyWorkTogether.Api.Shared.Infrastructure.Auth;
using EasyWorkTogether.Api.Shared.Infrastructure.Db;
using Npgsql;

namespace EasyWorkTogether.Api.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<NpgsqlConnectionFactory>();

        services.AddSingleton<PasswordService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<RequireSessionFilter>();
        services.AddSingleton<RequireSystemAdminFilter>();
        services.AddSingleton<RealtimeConnectionManager>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<ChatService>();
        services.AddHostedService<DeadlineNotificationWorker>();
        services.AddHttpClient();

        return services;
    }
}
