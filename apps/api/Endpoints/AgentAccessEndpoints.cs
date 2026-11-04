using System.Security.Cryptography;
using System.Text;
using Careeroc.Application.Services;
using Careeroc.Contracts.Agents;

namespace Careeroc.Api.Endpoints;

public static class AgentAccessEndpoints
{
    public static IEndpointRouteBuilder MapAgentAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/profile/access-tokens",
                async (HttpRequest request, PersonalAccessTokenService tokens, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tokens.ListAsync(RequestUserContext.GetUserEmail(request), cancellationToken)))
            .WithName("ListPersonalAccessTokens")
            .WithTags("Agent access")
            .Produces<IReadOnlyList<PersonalAccessTokenDto>>();

        endpoints
            .MapPost(
                "/api/profile/access-tokens",
                async (
                    CreatePersonalAccessTokenRequest body,
                    HttpRequest request,
                    PersonalAccessTokenService tokens,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        return Results.Ok(await tokens.CreateAsync(RequestUserContext.GetUserEmail(request), body, cancellationToken));
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("CreatePersonalAccessToken")
            .WithTags("Agent access")
            .Produces<CreatedPersonalAccessTokenDto>()
            .Produces(StatusCodes.Status400BadRequest);

        endpoints
            .MapDelete(
                "/api/profile/access-tokens/{tokenId:guid}",
                async (Guid tokenId, HttpRequest request, PersonalAccessTokenService tokens, CancellationToken cancellationToken) =>
                    await tokens.RevokeAsync(RequestUserContext.GetUserEmail(request), tokenId, cancellationToken)
                        ? Results.NoContent()
                        : Results.NotFound())
            .WithName("RevokePersonalAccessToken")
            .WithTags("Agent access");

        // Token-authenticated reads for the A2A runtime, which never touches the database itself.
        endpoints
            .MapGet(
                "/agent/v1/me",
                (HttpRequest request) => TypedResults.Ok(new AgentCallerDto(OwnerKey(RequestUserContext.GetUserEmail(request) ?? string.Empty))))
            .WithName("GetAgentCaller")
            .WithTags("Agent access")
            .Produces<AgentCallerDto>();

        endpoints
            .MapGet(
                "/agent/v1/pipeline-summary",
                async (HttpRequest request, AgentQueryService queries, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await queries.GetPipelineSummaryAsync(RequestUserContext.GetUserEmail(request), cancellationToken)))
            .WithName("GetAgentPipelineSummary")
            .WithTags("Agent access")
            .Produces<AgentPipelineSummaryDto>();

        endpoints
            .MapGet(
                "/agent/v1/review-request",
                async (HttpRequest request, CareerOrchestratorService orchestrator, CancellationToken cancellationToken) =>
                    await orchestrator.BuildReviewRequestAsync(RequestUserContext.GetUserEmail(request), cancellationToken) is { } reviewRequest
                        ? Results.Ok(reviewRequest)
                        : Results.NotFound())
            .WithName("GetAgentReviewRequest")
            .WithTags("Agent access");

        return endpoints;
    }

    // An opaque, stable key the A2A runtime uses to keep each user's tasks apart.
    private static string OwnerKey(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}
