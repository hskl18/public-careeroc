using Careeroc.Api.Security;
using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Contracts.Orchestrator;

namespace Careeroc.Api.Endpoints;

public static class OrchestratorEndpoints
{
    public static IEndpointRouteBuilder MapOrchestratorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/orchestrator/resume-mailbox-review",
                async (
                    HttpRequest request,
                    CareerOrchestratorService careerOrchestratorService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    ResumeMailboxReviewDto response;
                    try
                    {
                        response = await careerOrchestratorService.ReviewResumeMailboxFitAsync(
                            userEmail,
                            cancellationToken);
                    }
                    catch (UserFacingException exception)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }

                    await securityEventLogger.LogMutationAsync(
                        request,
                        "orchestrator.resume_mailbox_reviewed",
                        userEmail,
                        "user_memory",
                        null,
                        "success",
                        response.Summary);
                    return Results.Ok(response);
                })
            .WithName("ReviewResumeMailboxFit")
            .WithTags("Orchestrator")
            .RequireRateLimiting(AgentReviewRateLimit.PolicyName)
            .Produces<ResumeMailboxReviewDto>();

        return endpoints;
    }
}
