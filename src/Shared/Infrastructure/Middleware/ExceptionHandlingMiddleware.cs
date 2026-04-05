namespace EasyWorkTogether.Api.Shared.Infrastructure.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
                throw;
            context.Response.Clear();
            var (statusCode, message, logLevel) = ex switch
            {
                ArgumentException argumentException => (StatusCodes.Status400BadRequest, argumentException.Message, LogLevel.Warning),
                FormatException formatException => (StatusCodes.Status400BadRequest, formatException.Message, LogLevel.Warning),
                InvalidOperationException invalidOperationException => (StatusCodes.Status400BadRequest, invalidOperationException.Message, LogLevel.Warning),
                KeyNotFoundException keyNotFoundException => (StatusCodes.Status404NotFound, keyNotFoundException.Message, LogLevel.Warning),
                UnauthorizedAccessException unauthorizedAccessException => (StatusCodes.Status403Forbidden, unauthorizedAccessException.Message, LogLevel.Warning),
                _ => (StatusCodes.Status500InternalServerError, "Internal server error", LogLevel.Error)
            };

            _logger.Log(logLevel, ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ErrorResponse(message));
        }
    }
}
