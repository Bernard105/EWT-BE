namespace EasyWorkTogether.Api.Shared.Infrastructure.Auth;

public sealed class RequireSessionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authService = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
        var user = await authService.GetCurrentUserAsync(context.HttpContext);

        if (user is null)
            return Results.Unauthorized();

        context.HttpContext.Items[AuthService.HttpContextUserKey] = user;
        return await next(context);
    }
}

