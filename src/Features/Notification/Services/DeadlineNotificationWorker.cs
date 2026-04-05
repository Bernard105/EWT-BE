namespace EasyWorkTogether.Api.Services;

public sealed class DeadlineNotificationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeadlineNotificationWorker> _logger;

    public DeadlineNotificationWorker(IServiceProvider serviceProvider, ILogger<DeadlineNotificationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            const string sql = """
                SELECT id,
                       title,
                       due_at,
                       created_by,
                       assignee_id,
                       support_requested_from,
                       is_emergency
                FROM tasks
                WHERE due_at IS NOT NULL
                  AND status <> 'completed'
                  AND due_at > NOW()
                  AND due_at <= NOW() + INTERVAL '1 day';
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var now = DateTime.UtcNow;
            while (await reader.ReadAsync(cancellationToken))
            {
                var taskId = reader.GetInt32(0);
                var title = reader.GetString(1);
                var dueAt = reader.GetDateTime(2);
                var createdBy = reader.GetInt32(3);
                var assigneeId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var supportRequestedFrom = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var isEmergency = reader.GetBoolean(6);

                var dueIn = dueAt - now;
                if (dueIn <= TimeSpan.Zero)
                    continue;

                var recipients = new HashSet<int> { createdBy };
                if (assigneeId.HasValue)
                    recipients.Add(assigneeId.Value);
                if (supportRequestedFrom.HasValue)
                    recipients.Add(supportRequestedFrom.Value);

                if (dueIn <= TimeSpan.FromHours(1))
                {
                    foreach (var recipient in recipients)
                    {
                        var type = isEmergency ? "emergency" : "task_deadline";
                        var titleText = isEmergency ? "Emergency task due in 1 hour" : "Task deadline in 1 hour";
                        var messageText = isEmergency
                            ? $"{title} cần xử lý khẩn trong vòng 1 giờ tới."
                            : $"{title} sẽ đến hạn trong vòng 1 giờ tới.";

                        await notifications.CreateAsync(
                            recipient,
                            type,
                            titleText,
                            messageText,
                            createdBy,
                            "task",
                            taskId,
                            new Dictionary<string, string?>
                            {
                                ["task_id"] = taskId.ToString(),
                                ["due_at"] = ToIsoString(dueAt),
                                ["reminder_window"] = "1h"
                            },
                            $"reminder:{type}:{taskId}:{recipient}:1h",
                            cancellationToken);
                    }

                    continue;
                }

                if (!isEmergency)
                    continue;

                var band = dueIn <= TimeSpan.FromHours(4)
                    ? "4h"
                    : dueIn <= TimeSpan.FromDays(1)
                        ? "1d"
                        : null;

                if (band is null)
                    continue;

                var headline = band == "4h" ? "Emergency task due in 4 hours" : "Emergency task due in 1 day";
                var message = band == "4h"
                    ? $"{title} là công việc khẩn và sẽ đến hạn trong vòng 4 giờ tới."
                    : $"{title} là công việc khẩn và sẽ đến hạn trong vòng 24 giờ tới.";

                foreach (var recipient in recipients)
                {
                    await notifications.CreateAsync(
                        recipient,
                        "emergency",
                        headline,
                        message,
                        createdBy,
                        "task",
                        taskId,
                        new Dictionary<string, string?>
                        {
                            ["task_id"] = taskId.ToString(),
                            ["due_at"] = ToIsoString(dueAt),
                            ["reminder_window"] = band
                        },
                        $"reminder:emergency:{taskId}:{recipient}:{band}",
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deadline notification worker failed");
        }
    }
}
