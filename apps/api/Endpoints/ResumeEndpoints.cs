using Careeroc.Application.Services;
using Careeroc.Api.Security;
using Careeroc.Contracts.Resumes;

namespace Careeroc.Api.Endpoints;

public static class ResumeEndpoints
{
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/resumes",
                async (
                    HttpRequest request,
                    ResumeIntelligenceService resumeIntelligenceService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await resumeIntelligenceService.GetWorkspaceAsync(
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);
                    return TypedResults.Ok(response);
                })
            .WithName("GetResumeWorkspace")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>();

        endpoints
            .MapPost(
                "/api/resumes",
                async (
                    CreateResumeRequest createRequest,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.CreateResumeAsync(
                            createRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.created",
                            userEmail,
                            "resume_document",
                            response.Documents.FirstOrDefault()?.Id,
                            "success",
                            createRequest.Title);
                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.created",
                            userEmail,
                            "resume_document",
                            null,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("CreateResume")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>();

        return endpoints;
    }
}
