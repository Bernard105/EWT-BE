namespace EasyWorkTogether.Api.Shared.Infrastructure.Auth;

public sealed class RequireSystemAdminFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.GetCurrentUser();
        if (!user.IsSystemAdmin)
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));

        return next(context);
    }
}
