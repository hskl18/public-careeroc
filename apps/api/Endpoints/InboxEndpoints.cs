using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Api.Security;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Inbox;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Careeroc.Api.Endpoints;

public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/inbox-accounts",
                async (
                    HttpRequest request,
                    InboxSetupService inboxSetupService,
                    CancellationToken cancellationToken) =>
                {
                    var response = await inboxSetupService.GetSetupAsync(
                        RequestUserContext.GetUserEmail(request),
                        gmailOnly: true,
                        cancellationToken: cancellationToken);
                    return TypedResults.Ok(response);
                })
            .WithName("GetInboxAccounts")
            .WithTags("Inbox")
            .Produces<InboxSetupResponse>();

        endpoints
            .MapPost(
                "/api/inbox-accounts/sync-now",
                (
                    HttpRequest request,
                    IServiceScopeFactory scopeFactory,
                    IMemoryCache memoryCache,
                    IHostApplicationLifetime applicationLifetime,
                    IClock clock,
                    ILoggerFactory loggerFactory) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    var throttleKey = $"inbox-sync-now:{userEmail}";
                    if (memoryCache.TryGetValue(throttleKey, out _))
                    {
                        return Results.Json(
                            new { error = "sync_now_rate_limited", retryAfterSeconds = 30 },
                            statusCode: StatusCodes.Status429TooManyRequests);
                    }

                    var attemptedAtUtc = clock.UtcNow;
                    memoryCache.Set(throttleKey, true, TimeSpan.FromSeconds(30));
                    var logger = loggerFactory.CreateLogger("InboxSyncNow");
                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                using var scope = scopeFactory.CreateScope();
                                var coordinator = scope.ServiceProvider.GetRequiredService<InboxSyncCoordinator>();
                                await coordinator.RunForUserAsync(userEmail, applicationLifetime.ApplicationStopping);
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (Exception exception)
                            {
                                logger.LogWarning(
                                    exception,
                                    "User-triggered inbox sync failed for {UserEmail}.",
                                    userEmail);
                            }
                        },
                        CancellationToken.None);

                    return Results.Accepted(
                        value: new SyncNowResponseDto(true, attemptedAtUtc));
                })
            .WithName("SyncInboxAccountsNow")
            .WithTags("Inbox")
            .Produces<SyncNowResponseDto>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints
            .MapPost(
                "/api/inbox-accounts/gmail",
                async (
                    RegisterInboxAccountRequest request,
                    HttpRequest httpRequest,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await inboxSetupService.RegisterGmailAsync(
                            request,
                            userEmail,
                            RequestUserContext.GetUserName(httpRequest),
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "inbox.account_registered",
                            userEmail,
                            "inbox_account",
                            response.Accounts.FirstOrDefault(account => string.Equals(account.EmailAddress, request.EmailAddress, StringComparison.OrdinalIgnoreCase))?.Id,
                            "success",
                            request.EmailAddress);
                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "inbox.account_registered",
                            userEmail,
                            "inbox_account",
                            null,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("RegisterGmailInboxAccount")
            .WithTags("Inbox")
            .RejectDemoIdentities();

        endpoints
            .MapGet(
                "/api/inbox-accounts/gmail/oauth/start",
                async (
                    string emailAddress,
                    int? backfillMonths,
                    string? returnUrl,
                    HttpRequest request,
                    IConfiguration configuration,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);

                    try
                    {
                        var authorizationUrl = await inboxSetupService.StartGmailOAuthAsync(
                            new RegisterInboxAccountRequest
                            {
                                EmailAddress = emailAddress,
                                BackfillMonths = backfillMonths
                            },
                            BuildGmailRedirectUri(configuration),
                            returnUrl,
                            userEmail,
                            RequestUserContext.GetUserName(request),
                            cancellationToken);
                        await securityEventLogger.LogOAuthEventAsync(
                            request,
                            "inbox.gmail_oauth_started",
                            userEmail,
                            emailAddress,
                            "success");

                        return Results.Redirect(authorizationUrl);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogOAuthEventAsync(
                            request,
                            "inbox.gmail_oauth_started",
                            userEmail,
                            emailAddress,
                            "rejected",
                            exception.Message);
                        return !string.IsNullOrWhiteSpace(returnUrl)
                            ? Results.Redirect(
                                BuildReturnUrl(
                                    returnUrl,
                                    new GmailOAuthCompletionResult(returnUrl, false, exception.Message)))
                            : Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("StartGmailInboxOAuth")
            .WithTags("Inbox")
            .RejectDemoIdentities();

        endpoints
            .MapGet(
                "/api/inbox-accounts/gmail/oauth/callback",
                async (
                    string? state,
                    string? code,
                    string? error,
                    HttpRequest request,
                    IConfiguration configuration,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    // Google redirects the browser to the web app, which forwards the callback signed as
                    // the signed-in user; the service only completes a flow that user started.
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            var failed = await inboxSetupService.FailGmailOAuthAsync(
                                state ?? string.Empty,
                                error,
                                userEmail,
                                cancellationToken);
                            await securityEventLogger.LogOAuthEventAsync(
                                request,
                                "inbox.gmail_oauth_callback",
                                userEmail,
                                null,
                                "failed",
                                error);

                            return Results.Redirect(BuildReturnUrl(failed.ReturnUrl, failed));
                        }

                        var completed = await inboxSetupService.CompleteGmailOAuthAsync(
                            state ?? string.Empty,
                            code ?? string.Empty,
                            BuildGmailRedirectUri(configuration),
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogOAuthEventAsync(
                            request,
                            "inbox.gmail_oauth_callback",
                            userEmail,
                            null,
                            completed.Succeeded ? "success" : "failed",
                            completed.Message);

                        return Results.Redirect(BuildReturnUrl(completed.ReturnUrl, completed));
                    }
                    catch (InvalidOperationException exception)
                    {
                        var rejected = new GmailOAuthCompletionResult(
                            "/app/inbox#mail-sync",
                            false,
                            ToOAuthCallbackErrorMessage(exception));
                        await securityEventLogger.LogOAuthEventAsync(
                            request,
                            "inbox.gmail_oauth_callback",
                            userEmail,
                            null,
                            "rejected",
                            exception.Message);
                        return Results.Redirect(BuildReturnUrl(rejected.ReturnUrl, rejected));
                    }
                })
            .WithName("CompleteGmailInboxOAuth")
            .WithTags("Inbox");

        endpoints
            .MapPatch(
                "/api/inbox-accounts/{inboxAccountId:guid}/backfill",
                async (
                    Guid inboxAccountId,
                    UpdateInboxBackfillRequest request,
                    HttpRequest httpRequest,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(httpRequest);

                    try
                    {
                        var response = await inboxSetupService.UpdateGmailBackfillAsync(
                            inboxAccountId,
                            request,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "inbox.backfill_updated",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            response is null ? "not_found" : "success",
                            request.BackfillMonths?.ToString());
                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            httpRequest,
                            "inbox.backfill_updated",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("UpdateGmailInboxBackfill")
            .WithTags("Inbox")
            .RejectDemoIdentities();

        endpoints
            .MapDelete(
                "/api/inbox-accounts/{inboxAccountId:guid}",
                async (
                    Guid inboxAccountId,
                    HttpRequest request,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);
                    var response = await inboxSetupService.DeleteInboxAccountAsync(
                        inboxAccountId,
                        userEmail,
                        cancellationToken);
                    await securityEventLogger.LogMutationAsync(
                        request,
                        "inbox.account_deleted",
                        userEmail,
                        "inbox_account",
                        inboxAccountId,
                        response is null ? "not_found" : "success");
                    return response is null ? Results.NotFound() : Results.Ok(response);
                })
            .WithName("DeleteInboxAccount")
            .WithTags("Inbox");

        endpoints
            .MapDelete(
                "/api/inbox-accounts/{inboxAccountId:guid}/imported-data",
                async (
                    Guid inboxAccountId,
                    [FromBody] DeleteImportedInboxDataRequest deleteRequest,
                    HttpRequest request,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);

                    try
                    {
                        var response = await inboxSetupService.DeleteImportedDataAsync(
                            inboxAccountId,
                            deleteRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            request,
                            "inbox.imported_data_deleted",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            response is null ? "not_found" : "success");

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            request,
                            "inbox.imported_data_deleted",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("DeleteImportedInboxData")
            .WithTags("Inbox");

        endpoints
            .MapDelete(
                "/api/inbox-accounts/{inboxAccountId:guid}/purge",
                async (
                    Guid inboxAccountId,
                    [FromBody] DeleteInboxPurgeRequest purgeRequest,
                    HttpRequest request,
                    InboxSetupService inboxSetupService,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    var userEmail = RequestUserContext.GetUserEmail(request);

                    try
                    {
                        var response = await inboxSetupService.PurgeInboxAndImportedApplicationsAsync(
                            inboxAccountId,
                            purgeRequest,
                            userEmail,
                            cancellationToken);
                        await securityEventLogger.LogMutationAsync(
                            request,
                            "inbox.account_purged",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            response is null ? "not_found" : "success");

                        return response is null ? Results.NotFound() : Results.Ok(response);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await securityEventLogger.LogMutationAsync(
                            request,
                            "inbox.account_purged",
                            userEmail,
                            "inbox_account",
                            inboxAccountId,
                            "rejected",
                            exception.Message);
                        return Results.BadRequest(new { error = exception.Message });
                    }
                })
            .WithName("PurgeInboxAccount")
            .WithTags("Inbox");

        return endpoints;
    }

    // Google must send the browser back through the web app, which signs the callback as the user.
    private static string BuildGmailRedirectUri(IConfiguration configuration)
    {
        var publicWebBaseUrl =
            configuration["Careeroc:PublicWebBaseUrl"] ??
            configuration["NEXT_PUBLIC_SITE_URL"] ??
            configuration["AUTH_URL"];

        if (!Uri.TryCreate(publicWebBaseUrl, UriKind.Absolute, out var configuredWebBaseUrl) ||
            !IsHttpUrl(configuredWebBaseUrl))
        {
            throw new InvalidOperationException(
                "Gmail sign-in needs the web app's public URL. Set NEXT_PUBLIC_SITE_URL on the API.");
        }

        return new Uri(configuredWebBaseUrl, "/api/inbox-accounts/gmail/oauth/callback").AbsoluteUri;
    }

    // A path the web app resolves against its own public origin.
    private static string BuildReturnUrl(string returnUrl, GmailOAuthCompletionResult result)
    {
        return QueryHelpers.AddQueryString(
            ApplicationDefaults.IsLocalPath(returnUrl) ? returnUrl : "/",
            new Dictionary<string, string?>
            {
                ["gmail_status"] = result.Succeeded ? "connected" : "error",
                ["gmail_message"] = result.Message
            });
    }

    private static string ToOAuthCallbackErrorMessage(InvalidOperationException exception)
    {
        return exception.Message.Contains(
            "No pending Gmail authorization session",
            StringComparison.OrdinalIgnoreCase)
            ? "Gmail connection is already complete or the sign-in link expired. Check the connected inbox status below."
            : exception.Message;
    }

    private static bool IsHttpUrl(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp ||
               uri.Scheme == Uri.UriSchemeHttps;
    }
}
