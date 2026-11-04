using Careeroc.Application.Services;
using Careeroc.Contracts.Profile;

namespace Careeroc.Api.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/profile",
                async (
                    HttpRequest request,
                    UserProfileService userProfileService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await userProfileService.GetProfileAsync(
                        RequestUserContext.GetUserEmail(request),
                        RequestUserContext.GetUserName(request),
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("GetProfile")
            .WithTags("Profile")
            .Produces<UserProfileDto>();

        endpoints
            .MapGet(
                "/api/profile/agent-context",
                async (
                    HttpRequest request,
                    UserAgentContextSnapshotService snapshotService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await snapshotService.GetSnapshotAsync(
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("GetProfileAgentContext")
            .WithTags("Profile")
            .Produces<UserAgentContextSnapshotDto>();

        endpoints
            .MapGet(
                "/api/profile/memory/proposals",
                async (
                    HttpRequest request,
                    UserMemoryProposalService memoryProposalService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await memoryProposalService.GetPendingProposalsAsync(
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("GetProfileMemoryProposals")
            .WithTags("Profile")
            .Produces<IReadOnlyList<UserMemoryEntryDto>>();

        endpoints
            .MapGet(
                "/api/profile/memory",
                async (
                    HttpRequest request,
                    UserMemoryProposalService memoryProposalService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await memoryProposalService.GetConfirmedMemoryAsync(
                        RequestUserContext.GetUserEmail(request),
                        cancellationToken);

                    return TypedResults.Ok(response);
                })
            .WithName("GetProfileMemory")
            .WithTags("Profile")
            .Produces<IReadOnlyList<UserMemoryEntryDto>>();

        endpoints
            .MapPatch(
                "/api/profile/memory/{memoryEntryId:guid}",
                async (
                    Guid memoryEntryId,
                    UpdateUserMemoryRequest updateRequest,
                    HttpRequest request,
                    UserMemoryProposalService memoryProposalService,
                    Api.Security.ApiSecurityEventLogger auditLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    try
                    {
                        var response = await memoryProposalService.UpdateConfirmedMemoryAsync(
                            memoryEntryId,
                            updateRequest,
                            userEmail,
                            cancellationToken);

                        if (response is null)
                        {
                            await auditLogger.LogMutationAsync(
                                request,
                                "profile.memory_updated",
                                userEmail,
                                "user_memory",
                                memoryEntryId,
                                "not_found");
                            return Results.NotFound();
                        }

                        await auditLogger.LogMutationAsync(
                            request,
                            "profile.memory_updated",
                            userEmail,
                            "user_memory",
                            response.Id,
                            "success",
                            $"{response.Kind}/{response.Key}");

                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await auditLogger.LogMutationAsync(
                            request,
                            "profile.memory_updated",
                            userEmail,
                            "user_memory",
                            memoryEntryId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateProfileMemory")
            .WithTags("Profile")
            .Produces<UserMemoryEntryDto>();

        endpoints
            .MapDelete(
                "/api/profile/memory/{memoryEntryId:guid}",
                async (
                    Guid memoryEntryId,
                    HttpRequest request,
                    UserMemoryProposalService memoryProposalService,
                    Api.Security.ApiSecurityEventLogger auditLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    var deleted = await memoryProposalService.DeleteMemoryAsync(
                        memoryEntryId,
                        userEmail,
                        cancellationToken);

                    await auditLogger.LogMutationAsync(
                        request,
                        "profile.memory_deleted",
                        userEmail,
                        "user_memory",
                        memoryEntryId,
                        deleted ? "success" : "not_found");

                    return deleted ? Results.NoContent() : Results.NotFound();
                })
            .WithName("DeleteProfileMemory")
            .WithTags("Profile");

        endpoints
            .MapPatch(
                "/api/profile/memory/proposals/{memoryEntryId:guid}",
                async (
                    Guid memoryEntryId,
                    ResolveUserMemoryProposalRequest resolveRequest,
                    HttpRequest request,
                    UserMemoryProposalService memoryProposalService,
                    CareerOrchestratorService careerOrchestratorService,
                    Api.Security.ApiSecurityEventLogger auditLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    try
                    {
                        var response = await memoryProposalService.ResolveProposalAsync(
                            memoryEntryId,
                            resolveRequest,
                            userEmail,
                            cancellationToken);

                        if (response is null)
                        {
                            await auditLogger.LogMutationAsync(
                                request,
                                "profile.memory_proposal_resolved",
                                userEmail,
                                "user_memory",
                                memoryEntryId,
                                "not_found",
                                resolveRequest.Action);
                            return Results.NotFound();
                        }

                        await auditLogger.LogMutationAsync(
                            request,
                            "profile.memory_proposal_resolved",
                            userEmail,
                            "user_memory",
                            response.Id,
                            "success",
                            $"{resolveRequest.Action}:{response.Kind}/{response.Key}");

                        return Results.Ok(
                            await careerOrchestratorService.ApplyReviewDecisionAsync(response, cancellationToken));
                    }
                    catch (InvalidOperationException exception)
                    {
                        await auditLogger.LogMutationAsync(
                            request,
                            "profile.memory_proposal_resolved",
                            userEmail,
                            "user_memory",
                            memoryEntryId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("ResolveProfileMemoryProposal")
            .WithTags("Profile")
            .Produces<UserMemoryEntryDto>();

        endpoints
            .MapPatch(
                "/api/profile",
                async (
                    UpdateUserProfileRequest updateRequest,
                    HttpRequest request,
                    UserProfileService userProfileService,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var response = await userProfileService.UpdateProfileAsync(
                            updateRequest,
                            RequestUserContext.GetUserEmail(request),
                            RequestUserContext.GetUserName(request),
                            cancellationToken);

                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateProfile")
            .WithTags("Profile")
            .Produces<UserProfileDto>();

        endpoints
            .MapPost(
                "/api/profile/welcome-dismissed",
                async (
                    HttpRequest request,
                    UserProfileService userProfileService,
                    Api.Security.ApiSecurityEventLogger auditLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    var response = await userProfileService.DismissWelcomeAsync(
                        userEmail,
                        RequestUserContext.GetUserName(request),
                        cancellationToken);

                    await auditLogger.LogMutationAsync(
                        request,
                        "profile.welcome_dismissed",
                        userEmail,
                        "user",
                        null,
                        "success");

                    return Results.Ok(response);
                })
            .WithName("DismissProfileWelcome")
            .WithTags("Profile")
            .Produces<UserProfileDto>();

        return endpoints;
    }
}
