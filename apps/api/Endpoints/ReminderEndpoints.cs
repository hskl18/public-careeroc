using Careeroc.Application.Services;
using Careeroc.Api.Security;
using Careeroc.Contracts.Reminders;

namespace Careeroc.Api.Endpoints;

public static class ReminderEndpoints
{
    public static IEndpointRouteBuilder MapReminderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/api/reminders/{reminderId:guid}/evidence-refresh",
                async (
                    Guid reminderId,
                    HttpRequest httpRequest,
                    ReminderEvidenceRefreshService reminderEvidenceRefreshService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);
                    var result = await reminderEvidenceRefreshService.RefreshReminderAsync(
                        reminderId,
                        userEmail,
                        cancellationToken);

                    await securityEventLogger.LogMutationAsync(
                        httpRequest,
                        "reminder.evidence_refreshed",
                        userEmail,
                        "reminder",
                        reminderId,
                        result is null ? "not_found" : "success");

                    return result is null
                        ? Results.NotFound()
                        : Results.Ok(
                            new RefreshReminderEvidenceResponse(
                                result.RemindersRefreshed,
                                result.EmailsUpdated));
                })
            .WithName("RefreshReminderEvidence")
            .WithTags("Reminders");

        endpoints
            .MapPatch(
                "/api/reminders/{reminderId:guid}/snooze",
                async (
                    Guid reminderId,
                    SnoozeReminderRequest request,
                    HttpRequest httpRequest,
                    ReminderService reminderService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var updated = await reminderService.SnoozeAsync(
                            reminderId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "reminder.snoozed",
                            userEmail,
                            "reminder",
                            reminderId,
                            updated ? "success" : "not_found");

                        return updated ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "reminder.snoozed",
                            userEmail,
                            "reminder",
                            reminderId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("SnoozeReminder")
            .WithTags("Reminders");

        endpoints
            .MapPatch(
                "/api/reminders/{reminderId:guid}/status",
                async (
                    Guid reminderId,
                    UpdateReminderStatusRequest request,
                    HttpRequest httpRequest,
                    ReminderService reminderService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var updated = await reminderService.UpdateStatusAsync(
                            reminderId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "reminder.status_updated",
                            userEmail,
                            "reminder",
                            reminderId,
                            updated ? "success" : "not_found");

                        return updated ? Results.NoContent() : Results.NotFound();
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "reminder.status_updated",
                            userEmail,
                            "reminder",
                            reminderId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateReminderStatus")
            .WithTags("Reminders");

        return endpoints;
    }
}
