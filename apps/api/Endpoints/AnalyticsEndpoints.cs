using Careeroc.Application.Services;
using Careeroc.Contracts.Analytics;

namespace Careeroc.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/analytics",
                async (
                    string? range,
                    string? provider,
                    HttpRequest request,
                    AnalyticsQueryService analyticsQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await analyticsQueryService.GetAnalyticsAsync(
                        range,
                        provider,
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);
                    return TypedResults.Ok(response);
                })
            .WithName("GetAnalytics")
            .WithTags("Analytics")
            .Produces<AnalyticsResponse>();

        return endpoints;
    }
}
