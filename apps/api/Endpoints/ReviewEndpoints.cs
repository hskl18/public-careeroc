using Careeroc.Application.Services;
using Careeroc.Contracts.Review;

namespace Careeroc.Api.Endpoints;

public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/review",
                async (
                    HttpRequest request,
                    ManualReviewQueryService manualReviewQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await manualReviewQueryService.GetAsync(
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);
                    return TypedResults.Ok(response);
                })
            .WithName("GetManualReview")
            .WithTags("Review")
            .Produces<ManualReviewResponse>();

        return endpoints;
    }
}
