using Careeroc.Application.Services;
using Careeroc.Api.Security;
using Careeroc.Contracts.Applications;
using Careeroc.Contracts.Emails;
using Careeroc.Contracts.Resumes;

namespace Careeroc.Api.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/applications/lookup",
                async (
                    HttpRequest request,
                    string? q,
                    int? limit,
                    string? sort,
                    ApplicationLookupQueryService applicationLookupQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await applicationLookupQueryService.SearchAsync(
                        RequestUserContext.GetUserEmail(request),
                        q,
                        limit,
                        sort,
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("LookupApplications")
            .WithTags("Applications")
            .Produces<ApplicationLookupResponse>();

        endpoints
            .MapGet(
                "/api/applications/{jobApplicationId:guid}",
                async (
                    Guid jobApplicationId,
                    HttpRequest request,
                    ReminderEvidenceRefreshService reminderEvidenceRefreshService,
                    ApplicationDetailQueryService applicationDetailQueryService,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    await reminderEvidenceRefreshService.RefreshPendingRemindersAsync(
                        userEmail,
                        cancellationToken);
                    var response = await applicationDetailQueryService.GetAsync(
                        jobApplicationId,
                        userEmail,
                        cancellationToken);

                    return response is null
                        ? Results.NotFound()
                        : Results.Ok(response);
                })
            .WithName("GetApplicationDetail")
            .WithTags("Applications")
            .Produces<ApplicationDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapGet(
                "/api/applications/{jobApplicationId:guid}/resume-fit",
                async (
                    Guid jobApplicationId,
                    Guid? resumeVersionId,
                    HttpRequest request,
                    ResumeFitService resumeFitService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await resumeFitService.GetFitAsync(
                        jobApplicationId,
                        RequestUserContext.GetUserEmail(request),
                        resumeVersionId,
                        cancellationToken);

                    return response is null ? Results.NotFound() : Results.Ok(response);
                })
            .WithName("GetApplicationResumeFit")
            .WithTags("Applications")
            .Produces<ResumeFitDto>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPatch(
                "/api/applications/{jobApplicationId:guid}/status",
                async (
                    Guid jobApplicationId,
                    UpdateApplicationStatusRequest request,
                    HttpRequest httpRequest,
                    ManualReviewService manualReviewService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var updated = await manualReviewService.UpdateApplicationStatusAsync(
                            jobApplicationId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "application.status_updated",
                            userEmail,
                            "job_application",
                            jobApplicationId,
                            updated ? "success" : "not_found");

                        return updated ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "application.status_updated",
                            userEmail,
                            "job_application",
                            jobApplicationId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateApplicationStatus")
            .WithTags("Applications");

        endpoints
            .MapPatch(
                "/api/applications/{jobApplicationId:guid}/correction",
                async (
                    Guid jobApplicationId,
                    CorrectApplicationRequest request,
                    HttpRequest httpRequest,
                    ManualReviewService manualReviewService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var updated = await manualReviewService.CorrectApplicationAsync(
                            jobApplicationId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "application.correction_submitted",
                            userEmail,
                            "job_application",
                            jobApplicationId,
                            updated ? "success" : "not_found",
                            request.Kind);

                        return updated ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "application.correction_submitted",
                            userEmail,
                            "job_application",
                            jobApplicationId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("CorrectApplication")
            .WithTags("Applications");

        endpoints
            .MapPatch(
                "/api/emails/{inboxEmailId:guid}/correction",
                async (
                    Guid inboxEmailId,
                    CorrectEmailRequest request,
                    HttpRequest httpRequest,
                    ManualReviewService manualReviewService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var updated = await manualReviewService.CorrectEmailAsync(
                            inboxEmailId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "email.correction_submitted",
                            userEmail,
                            "inbox_email",
                            inboxEmailId,
                            updated ? "success" : "not_found");

                        return updated ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "email.correction_submitted",
                            userEmail,
                            "inbox_email",
                            inboxEmailId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("CorrectEmail")
            .WithTags("Applications");

        return endpoints;
    }
}
