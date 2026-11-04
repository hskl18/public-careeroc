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

        endpoints
            .MapPost(
                "/api/resumes/import",
                async (
                    ImportResumeFileRequest importRequest,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.ImportResumeFileAsync(
                            importRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.imported",
                            userEmail,
                            "resume_document",
                            response.Documents.FirstOrDefault()?.Id,
                            "success",
                            importRequest.FileName);
                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.imported",
                            userEmail,
                            "resume_document",
                            null,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("ImportResumeFile")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>();

        endpoints
            .MapPost(
                "/api/resumes/{resumeDocumentId:guid}/reextract",
                async (
                    Guid resumeDocumentId,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.ReextractResumeAsync(
                            resumeDocumentId,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.reextracted",
                            userEmail,
                            "resume_document",
                            resumeDocumentId,
                            response is null ? "not_found" : "success",
                            null);

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.reextracted",
                            userEmail,
                            "resume_document",
                            resumeDocumentId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("ReextractResume")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPost(
                "/api/resumes/versions/{resumeVersionId:guid}/analyze",
                async (
                    Guid resumeVersionId,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.AnalyzeResumeVersionAsync(
                            resumeVersionId,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.analyzed",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            response is null ? "not_found" : "success",
                            null);

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.analyzed",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("AnalyzeResumeVersion")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapGet(
                "/api/resumes/{resumeDocumentId:guid}/suggestions",
                async (
                    Guid resumeDocumentId,
                    HttpRequest request,
                    ResumeSuggestionService resumeSuggestionService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await resumeSuggestionService.GetSuggestionsAsync(
                        resumeDocumentId,
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);
                    return response is null ? Results.NotFound() : Results.Ok(response);
                })
            .WithName("GetResumeSuggestions")
            .WithTags("Resumes")
            .Produces<IReadOnlyList<ResumeSuggestionDto>>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPost(
                "/api/resumes/{resumeDocumentId:guid}/suggestions/{suggestionId:guid}/accept",
                async (
                    Guid resumeDocumentId,
                    Guid suggestionId,
                    HttpRequest httpRequest,
                    ResumeSuggestionService resumeSuggestionService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);
                    var accepted = await resumeSuggestionService.AcceptSuggestionAsync(
                        resumeDocumentId,
                        suggestionId,
                        userEmail,
                        cancellationToken);
                    await securityEventLogger.LogMutationAsync(
                        httpRequest,
                        "resume.suggestion_accepted",
                        userEmail,
                        "resume_document",
                        resumeDocumentId,
                        accepted is null ? "not_found" : accepted.Value ? "success" : "rejected",
                        suggestionId.ToString());
                    return accepted is null
                        ? Results.NotFound()
                        : accepted.Value
                            ? Results.NoContent()
                            : Results.BadRequest(new { error = "suggestion_not_found" });
                })
            .WithName("AcceptResumeSuggestion")
            .WithTags("Resumes")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPost(
                "/api/resumes/{resumeDocumentId:guid}/suggestions/{suggestionId:guid}/decline",
                async (
                    Guid resumeDocumentId,
                    Guid suggestionId,
                    HttpRequest httpRequest,
                    ResumeSuggestionService resumeSuggestionService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);
                    var declined = await resumeSuggestionService.DeclineSuggestionAsync(
                        resumeDocumentId,
                        suggestionId,
                        userEmail,
                        cancellationToken);
                    await securityEventLogger.LogMutationAsync(
                        httpRequest,
                        "resume.suggestion_declined",
                        userEmail,
                        "resume_document",
                        resumeDocumentId,
                        declined is null ? "not_found" : declined.Value ? "success" : "rejected",
                        suggestionId.ToString());
                    return declined is null
                        ? Results.NotFound()
                        : declined.Value
                            ? Results.NoContent()
                            : Results.BadRequest(new { error = "suggestion_not_found" });
                })
            .WithName("DeclineResumeSuggestion")
            .WithTags("Resumes")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPatch(
                "/api/resumes/versions/{resumeVersionId:guid}/structured-data",
                async (
                    Guid resumeVersionId,
                    PatchResumeStructuredDataRequest patchRequest,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.PatchStructuredDataAsync(
                            resumeVersionId,
                            patchRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.structured_data_patched",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            response is null ? "not_found" : "success",
                            $"{patchRequest.Operations.Count} operations");

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.structured_data_patched",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("PatchResumeStructuredData")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapPost(
                "/api/resumes/versions/{resumeVersionId:guid}/corrections",
                async (
                    Guid resumeVersionId,
                    CorrectResumeFieldRequest correctionRequest,
                    HttpRequest httpRequest,
                    ResumeIntelligenceService resumeIntelligenceService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await resumeIntelligenceService.AddCorrectionAsync(
                            resumeVersionId,
                            correctionRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.correction_submitted",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            response is null ? "not_found" : "success",
                            correctionRequest.FieldPath);

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "resume.correction_submitted",
                            userEmail,
                            "resume_version",
                            resumeVersionId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("CorrectResumeVersion")
            .WithTags("Resumes")
            .Produces<ResumeWorkspaceDto>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
