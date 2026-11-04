using Careeroc.Api.Background;
using Careeroc.Api.Endpoints;
using Careeroc.Domain;
using Careeroc.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Api.Security;

internal sealed class ApiUserContextMiddleware
{

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ApiSecurityEventLogger _securityEventLogger;
    private readonly IMemoryCache _memoryCache;

    public ApiUserContextMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment,
        ApiSecurityEventLogger securityEventLogger,
        IMemoryCache memoryCache)
    {
        _next = next;
        _configuration = configuration;
        _environment = environment;
        _securityEventLogger = securityEventLogger;
        _memoryCache = memoryCache;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CareerocDbContext dbContext,
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

        var role = await ResolveRoleAsync(
            dbContext,
            configuredUserAccess,
            userContext.Email,
            _configuration,
            _memoryCache,
            context.RequestAborted);

        RequestUserContext.SetUserContext(context, userContext with { Role = role });
        await _next(context);
    }

    private static async Task<string> ResolveRoleAsync(
        CareerocDbContext dbContext,
        ConfiguredUserAccess configuredUserAccess,
        string email,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        CancellationToken cancellationToken)
    {
        if (configuredUserAccess.IsConfiguredAdmin(email))
        {
            return UserRoleNames.Admin;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var cacheSeconds = Math.Clamp(
            configuration.GetValue("Security:UserRoleCacheSeconds", 30),
            0,
            300);
        if (cacheSeconds == 0)
        {
            return await LoadStoredRoleAsync(dbContext, normalizedEmail, cancellationToken);
        }

        return await memoryCache.GetOrCreateAsync(
            $"api-user-role:{normalizedEmail}",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheSeconds);
                return await LoadStoredRoleAsync(dbContext, normalizedEmail, cancellationToken);
            }) ?? UserRoleNames.User;
    }

    private static async Task<string> LoadStoredRoleAsync(
        CareerocDbContext dbContext,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var storedRole = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Email == normalizedEmail)
            .Select(user => user.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return UserRoleNames.Normalize(storedRole);
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
