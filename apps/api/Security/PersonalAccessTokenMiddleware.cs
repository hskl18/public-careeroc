using Careeroc.Api.Endpoints;
using Careeroc.Application.Services;

namespace Careeroc.Api.Security;

// Authenticates external assistants on /mcp and /agent with a personal access token (Authorization: Bearer coc_...).
internal sealed class PersonalAccessTokenMiddleware
{
    private readonly RequestDelegate _next;

    public PersonalAccessTokenMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public static bool AppliesTo(PathString path)
    {
        return path.StartsWithSegments("/mcp") || path.StartsWithSegments("/agent");
    }

    public async Task InvokeAsync(
        HttpContext context,
        PersonalAccessTokenService tokenService,
        ConfiguredUserAccess configuredUserAccess)
    {
        // Assistants call these routes directly, never from a web page, so any browser Origin is
        // rejected; MCP requires servers to validate Origin against DNS rebinding.
        if (context.Request.Headers.Origin.Count > 0)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Browser origins cannot call assistant endpoints." });
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
        var identity = await tokenService.AuthenticateAsync(token, context.RequestAborted);
        // A token outlives allowlist changes, so access is rechecked on every call.
        if (identity is null || !configuredUserAccess.IsAllowed(identity.Email))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"careeroc\"";
            await context.Response.WriteAsJsonAsync(new { error = "A valid careeroc personal access token is required." });
            return;
        }

        RequestUserContext.SetUserContext(context, new AuthenticatedUserContext(identity.Email, identity.Name));
        await _next(context);
    }
}
