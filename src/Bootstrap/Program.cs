using System.Text.Json;
using EasyWorkTogether.Api.Bootstrap;
using EasyWorkTogether.Api.Endpoints;
using EasyWorkTogether.Api.Shared.Infrastructure.Auth;
using EasyWorkTogether.Api.Shared.Infrastructure.Db;
using EasyWorkTogether.Api.Shared.Infrastructure.Middleware;
using static EasyWorkTogether.Api.Bootstrap.DeploymentSupport;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

ConfigureRenderPort(builder);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
});

var connectionString = ResolveConnectionString(builder.Configuration);
builder.Services.AddApplicationSwagger();
builder.Services.AddApplicationDependencies(connectionString);

var allowedOrigins = ResolveCorsOrigins(builder.Configuration);
builder.Services.AddApplicationCors(builder.Environment, allowedOrigins);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseApplicationSwagger();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Frontend");
app.UseDefaultFiles();
app.UseStaticFiles();

await DbInitializer.InitializeAsync(app.Services);
await SystemAdminBootstrap.EnsureConfiguredSystemAdminsAsync(app.Services, app.Configuration);

app.MapGet("/api/status", () => Results.Ok(new { message = "Task backend is running" }))
    .WithName("GetApiStatus")
    .WithTags("System");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth")
    .WithTags("System");

app.MapAuthEndpoints();
app.MapWorkspaceEndpoints();
app.MapTaskEndpoints();
app.MapNotificationEndpoints();
app.MapFriendEndpoints();
app.MapChatEndpoints();
app.MapUploadEndpoints();
app.MapRealtimeEndpoints();
app.MapAdminEndpoints();

if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
