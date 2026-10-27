using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Dashboard;
using Microsoft.Extensions.Caching.Memory;

namespace Careeroc.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/dashboard",
                async (
                    HttpRequest request,
                    string? view,
                    int? limit,
                    string? cursor,
                    string? q,
                    string? company,
                    string? roleCanonical,
                    string? source,
                    string? location,
                    string? sort,
                    string? fresh,
                    IConfiguration configuration,
                    IMemoryCache memoryCache,
                    ReminderEvidenceRefreshService reminderEvidenceRefreshService,
                    DashboardQueryService dashboardQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = ApplicationDefaults.NormalizeRequiredUserEmail(
                        RequestUserContext.GetUserEmail(request));
                    var queryOptions = new DashboardQueryOptions(
                        view,
                        NormalizeLimit(limit),
                        NormalizeCursor(cursor),
                        q,
                        company,
                        roleCanonical,
                        source,
                        location,
                        sort);
                    var responseCacheSeconds = Math.Clamp(
                        configuration.GetValue("Dashboard:ResponseCacheSeconds", 20),
                        0,
                        120);
                    var cacheKey = BuildDashboardCacheKey(userEmail, queryOptions);
                    var bypassCache = !string.IsNullOrWhiteSpace(fresh);
                    if (!bypassCache &&
                        responseCacheSeconds > 0 &&
                        memoryCache.TryGetValue<DashboardResponse>(cacheKey, out var cachedResponse))
                    {
                        return TypedResults.Ok(cachedResponse);
                    }

                    await reminderEvidenceRefreshService.RefreshPendingRemindersAsync(
                        userEmail,
                        cancellationToken);
                    var response = await dashboardQueryService.GetDashboardAsync(
                        userEmail,
                        queryOptions,
                        cancellationToken);
                    if (responseCacheSeconds > 0)
                    {
                        memoryCache.Set(
                            cacheKey,
                            response,
                            new MemoryCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(responseCacheSeconds)
                            });
                    }

                    return TypedResults.Ok(response);
                })
            .WithName("GetDashboard")
            .WithTags("Dashboard")
            .Produces<DashboardResponse>();

        endpoints
            .MapPost(
                "/api/dashboard/inbox-check",
                async (
                    HttpRequest request,
                    IConfiguration configuration,
                    DashboardInboxCheckService dashboardInboxCheckService,
                    CancellationToken cancellationToken) =>
                {
                    var cacheSeconds = Math.Clamp(
                        configuration.GetValue("Dashboard:InboxCheckCacheSeconds", 120),
                        30,
                        900);
                    var response = await dashboardInboxCheckService.CheckAsync(
                        RequestUserContext.GetUserEmail(request),
                        TimeSpan.FromSeconds(cacheSeconds),
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("CheckDashboardInbox")
            .WithTags("Dashboard")
            .Produces<DashboardInboxCheckResult>();

        return endpoints;
    }

    private static int? NormalizeLimit(int? limit)
    {
        return limit.HasValue
            ? Math.Clamp(limit.Value, 1, 250)
            : null;
    }

    private static int? NormalizeCursor(string? cursor)
    {
        return int.TryParse(cursor, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static DashboardQueryOptions BuildDashboardQueryOptions(
        string? view,
        int? limit,
        string? cursor,
        string? q,
        string? company,
        string? roleCanonical,
        string? source,
        string? location,
        string? sort = null)
    {
        return new DashboardQueryOptions(
            view,
            NormalizeLimit(limit),
            NormalizeCursor(cursor),
            q,
            company,
            roleCanonical,
            source,
            location,
            sort);
    }

    private static string BuildDashboardCacheKey(string userEmail, DashboardQueryOptions options)
    {
        return string.Join(
            '|',
            "dashboard",
            userEmail.Trim().ToLowerInvariant(),
            options.View?.Trim().ToLowerInvariant() ?? "",
            options.Limit?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            options.CursorOffset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            options.Query?.Trim().ToLowerInvariant() ?? "",
            options.Company?.Trim().ToLowerInvariant() ?? "",
            options.RoleCanonical?.Trim().ToLowerInvariant() ?? "",
            options.Source?.Trim().ToLowerInvariant() ?? "",
            options.Location?.Trim().ToLowerInvariant() ?? "",
            options.Sort?.Trim().ToLowerInvariant() ?? "");
    }
}
