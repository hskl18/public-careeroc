using Careeroc.Api.Background;
using Careeroc.Api.Endpoints;

namespace Careeroc.Api.Security;

internal sealed class ApiUserContextMiddleware
{

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ApiSecurityEventLogger _securityEventLogger;

    public ApiUserContextMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment,
        ApiSecurityEventLogger securityEventLogger)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
        _securityEventLogger = securityEventLogger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ConfiguredUserAccess configuredUserAccess)
    {
        if (!RequiresAuthenticatedUserContext(context.Request))
        {
            await _next(context);
            return;
        }

        var validation = await ApiRequestSignatureValidator.ValidateAsync(
            context.Request,
            _configuration,
            _environment,
            context.RequestAborted);
        if (!validation.IsValid)
        {
            _securityEventLogger.LogRequestSignatureRejected(context, validation.Error);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = validation.Error });
            return;
        }

        var userContext = validation.UserContext!;
        if (!configuredUserAccess.IsAllowed(userContext.Email))
        {
            await _securityEventLogger.LogAccessDeniedAsync(context, userContext.Email);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "User is not allowed to access this workspace." });
            return;
        }

        RequestUserContext.SetUserContext(context, userContext);
        await _next(context);
    }

    private bool RequiresAuthenticatedUserContext(HttpRequest request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        return request.Path.StartsWithSegments("/api");
    }
}
