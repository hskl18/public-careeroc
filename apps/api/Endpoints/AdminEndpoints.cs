using System.Globalization;
using Careeroc.Api.Security;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Security;
using Careeroc.Contracts.Admin;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Api.Endpoints;

// Operations page for whoever runs this instance: configuration checks, queue health,
// recent model calls, and retrying failed email jobs. Every route requires the admin role.
public static class AdminEndpoints
{
    private const int RecentItemLimit = 25;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/admin/health",
                async (HttpRequest request, ApiSecurityEventLogger securityEventLogger) =>
                {
                    if (await RejectNonAdminAsync(request, securityEventLogger, "admin.health_viewed", "admin", null) is { } forbidden)
                    {
                        return forbidden;
                    }

                    await LogViewAsync(securityEventLogger, request, "admin.health_viewed");

                    return Results.Ok(
                        new
                        {
                            status = "ok",
                            userEmail = RequestUserContext.GetUserEmail(request),
                            role = RequestUserContext.GetUserRole(request)
                        });
                })
            .WithName("GetAdminHealth")
            .WithTags("Admin");

        endpoints
            .MapGet(
                "/api/admin/summary",
                async (
                    HttpRequest request,
                    CareerocDbContext dbContext,
                    IConfiguration configuration,
                    IHostEnvironment environment,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    if (await RejectNonAdminAsync(request, securityEventLogger, "admin.summary_viewed", "admin", null) is { } forbidden)
                    {
                        return forbidden;
                    }

                    await LogViewAsync(securityEventLogger, request, "admin.summary_viewed");

                    var queueCounts = await dbContext.EmailProcessingJobs
                        .AsNoTracking()
                        .GroupBy(job => job.Status)
                        .Select(group => new { Status = group.Key, Count = group.Count() })
                        .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);
                    var recentModelCallEvents = await dbContext.ModelCallEvents
                        .AsNoTracking()
                        .OrderByDescending(modelCallEvent => modelCallEvent.OccurredAtUtc)
                        .Take(RecentItemLimit)
                        .Select(
                            modelCallEvent => new AdminModelCallEventDto(
                                modelCallEvent.Id,
                                modelCallEvent.OccurredAtUtc,
                                modelCallEvent.Provider,
                                modelCallEvent.Model,
                                modelCallEvent.Purpose,
                                modelCallEvent.Outcome,
                                modelCallEvent.UserEmail,
                                modelCallEvent.InboxAccountId,
                                modelCallEvent.InboxEmailId,
                                modelCallEvent.RunId,
                                modelCallEvent.CorrelationId,
                                modelCallEvent.PromptTokens,
                                modelCallEvent.CompletionTokens,
                                modelCallEvent.TotalTokens,
                                modelCallEvent.EstimatedCostUsd,
                                modelCallEvent.LatencyMs,
                                modelCallEvent.StatusCode,
                                modelCallEvent.ErrorType))
                        .ToListAsync(cancellationToken);
                    var recentFailedEmailJobs = await dbContext.EmailProcessingJobs
                        .AsNoTracking()
                        .Where(job => job.Status == EmailProcessingStatus.Failed)
                        .OrderByDescending(job => job.UpdatedAtUtc)
                        .Take(RecentItemLimit)
                        .Select(
                            job => new
                            {
                                job.Id,
                                job.InboxEmailId,
                                job.InboxEmail.InboxAccountId,
                                UserEmail = job.InboxEmail.InboxAccount.User.Email,
                                InboxEmailAddress = job.InboxEmail.InboxAccount.EmailAddress,
                                job.Attempts,
                                job.UpdatedAtUtc,
                                job.LockedAtUtc,
                                job.LastError
                            })
                        .ToListAsync(cancellationToken);

                    return Results.Ok(
                        new AdminSummaryResponse(
                            new AdminQueueSummaryDto(
                                queueCounts.GetValueOrDefault(EmailProcessingStatus.Pending),
                                queueCounts.GetValueOrDefault(EmailProcessingStatus.Processing),
                                queueCounts.GetValueOrDefault(EmailProcessingStatus.Failed)),
                            recentModelCallEvents,
                            BuildOperationalChecks(configuration, environment),
                            recentFailedEmailJobs
                                .Select(
                                    job => new AdminFailedEmailJobDto(
                                        job.Id,
                                        job.InboxEmailId,
                                        job.InboxAccountId,
                                        job.UserEmail,
                                        job.InboxEmailAddress,
                                        job.Attempts,
                                        job.UpdatedAtUtc,
                                        job.LockedAtUtc,
                                        SensitiveDataRedactor.Redact(job.LastError, maxLength: 500)))
                                .ToList()));
                })
            .WithName("GetAdminSummary")
            .WithTags("Admin")
            .Produces<AdminSummaryResponse>()
            .Produces(StatusCodes.Status403Forbidden);

        endpoints
            .MapPost(
                "/api/admin/email-processing-jobs/{jobId:guid}/retry",
                async (
                    Guid jobId,
                    [FromBody] AdminRepairRequest repairRequest,
                    HttpRequest request,
                    CareerocDbContext dbContext,
                    IClock clock,
                    ApiSecurityEventLogger securityEventLogger,
                    CancellationToken cancellationToken) =>
                {
                    const string eventType = "admin.email_job_retry";
                    if (await RejectNonAdminAsync(request, securityEventLogger, eventType, "email_processing_job", jobId) is { } forbidden)
                    {
                        return forbidden;
                    }

                    if (!string.Equals(repairRequest.Confirmation?.Trim(), "retry", StringComparison.OrdinalIgnoreCase))
                    {
                        await LogAsync(securityEventLogger, request, eventType, jobId, "rejected", "confirmation_required");
                        return Results.BadRequest(new { error = "Type retry to retry this failed email job." });
                    }

                    var job = await dbContext.EmailProcessingJobs
                        .Include(candidate => candidate.InboxEmail)
                        .FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
                    if (job is null)
                    {
                        await LogAsync(securityEventLogger, request, eventType, jobId, "not_found");
                        return Results.NotFound(new { error = "Email processing job was not found." });
                    }

                    if (job.Status != EmailProcessingStatus.Failed)
                    {
                        await LogAsync(securityEventLogger, request, eventType, jobId, "rejected", $"status={job.Status}");
                        return Results.BadRequest(new { error = "Only failed jobs can be retried." });
                    }

                    var nowUtc = clock.UtcNow;
                    job.Status = EmailProcessingStatus.Pending;
                    // A failed job has used all its attempts; without a reset the queue would never pick it up again.
                    job.Attempts = 0;
                    job.AvailableAtUtc = nowUtc;
                    job.LockedAtUtc = null;
                    job.CompletedAtUtc = null;
                    job.LastError = null;
                    job.UpdatedAtUtc = nowUtc;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await LogAsync(securityEventLogger, request, eventType, jobId, "success", $"inboxAccountId={job.InboxEmail.InboxAccountId}");

                    return Results.Ok(new AdminRetryEmailJobResponse(job.Id, job.InboxEmailId, job.Status.ToString(), job.Attempts));
                })
            .WithName("RetryAdminEmailProcessingJob")
            .WithTags("Admin")
            .Produces<AdminRetryEmailJobResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult?> RejectNonAdminAsync(
        HttpRequest request,
        ApiSecurityEventLogger securityEventLogger,
        string eventType,
        string resourceType,
        Guid? resourceId)
    {
        if (AdminAuthorization.RequireAdmin(request))
        {
            return null;
        }

        await securityEventLogger.LogMutationAsync(
            request,
            eventType,
            RequestUserContext.GetUserEmail(request),
            resourceType,
            resourceId,
            "forbidden");
        return Results.Json(new { error = "Admin role is required." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static Task LogViewAsync(ApiSecurityEventLogger securityEventLogger, HttpRequest request, string eventType) =>
        securityEventLogger.LogMutationAsync(request, eventType, RequestUserContext.GetUserEmail(request), "admin", null, "success");

    private static Task LogAsync(
        ApiSecurityEventLogger securityEventLogger,
        HttpRequest request,
        string eventType,
        Guid jobId,
        string outcome,
        string? detail = null)
    {
        return securityEventLogger.LogMutationAsync(
            request,
            eventType,
            RequestUserContext.GetUserEmail(request),
            "email_processing_job",
            jobId,
            outcome,
            detail);
    }

    private static IReadOnlyList<AdminOperationalCheckDto> BuildOperationalChecks(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var checks = new List<AdminOperationalCheckDto>();

        AddSecretCheck(
            checks,
            "Security",
            "API request signing secret",
            FirstNonEmpty(configuration["Careeroc:ApiAuthSecret"], configuration["CAREEROC_API_AUTH_SECRET"]),
            required: true,
            "The web proxy and API must share this value or every proxied API request returns 401.");
        AddSecretCheck(
            checks,
            "Security",
            "Inbox token protection key",
            FirstNonEmpty(configuration["Security:TokenProtectionKey"], configuration["CAREEROC_TOKEN_PROTECTION_KEY"]),
            required: false,
            "Gmail tokens are encrypted with this key; changing it requires reconnecting Gmail.");
        AddConnectionStringCheck(checks, configuration, environment);
        AddBooleanCheck(checks, configuration, "SyncWorker:Enabled", "Workers", "Inbox sync worker");
        AddIntegerCheck(checks, configuration, "EmailProcessing:MaxAttempts", "Workers", "Email processing max attempts", minimum: 1, maximum: 100);
        AddIntegerCheck(checks, configuration, "EmailProcessing:RetryDelaySeconds", "Workers", "Email processing retry delay seconds", minimum: 1, maximum: 3600);
        AddBooleanCheck(checks, configuration, "AgentRuntime:Enabled", "Agents", "LangGraph agent runtime");
        AddBooleanCheck(checks, configuration, "Ollama:Enabled", "Models", "Ollama model pass");

        if (IsEnabled(configuration["Ollama:Enabled"]))
        {
            AddSecretCheck(
                checks,
                "Models",
                "Ollama API key",
                configuration["Ollama:ApiKey"],
                required: true,
                "The Ollama model pass is enabled, so it needs a provider API key.");
        }

        AddQuotedValueCheck(
            checks,
            configuration,
            [
                "ConnectionStrings:Careeroc",
                "DATABASE_URL",
                "CAREEROC_API_AUTH_SECRET",
                "CAREEROC_TOKEN_PROTECTION_KEY",
                "Google:ClientId",
                "Google:ClientSecret",
                "SyncWorker:Enabled",
                "Ollama:Enabled",
                "Ollama:ApiKey"
            ]);

        return checks
            .OrderBy(check => check.Status == "critical" ? 0 : check.Status == "warning" ? 1 : 2)
            .ThenBy(check => check.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(check => check.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddConnectionStringCheck(
        List<AdminOperationalCheckDto> checks,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var value = FirstNonEmpty(configuration.GetConnectionString("Careeroc"), configuration["DATABASE_URL"]);
        if (value is null)
        {
            checks.Add(new AdminOperationalCheckDto("Database", "careeroc connection string", "critical", "No database connection string is configured.", null));
            return;
        }

        var isLocalConnectionString =
            value.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        checks.Add(new AdminOperationalCheckDto(
            "Database",
            "careeroc connection string",
            isLocalConnectionString && !environment.IsDevelopment() ? "critical" : "ok",
            isLocalConnectionString
                ? "Database connection string points at localhost, which is expected for local development."
                : "Database connection string is present and does not look local.",
            RedactConnectionString(value)));
    }

    private static void AddSecretCheck(
        List<AdminOperationalCheckDto> checks,
        string area,
        string name,
        string? value,
        bool required,
        string detail)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            checks.Add(new AdminOperationalCheckDto(
                area,
                name,
                required ? "critical" : "warning",
                required ? $"{detail} The value is missing." : $"{detail} No explicit value is configured.",
                null));
            return;
        }

        var status = normalized.Length < 32 ? "critical" : "ok";
        checks.Add(new AdminOperationalCheckDto(
            area,
            name,
            status,
            status == "ok" ? detail : $"{detail} The configured value is shorter than 32 characters.",
            MaskSecret(normalized)));
    }

    private static void AddIntegerCheck(
        List<AdminOperationalCheckDto> checks,
        IConfiguration configuration,
        string key,
        string area,
        string name,
        int minimum,
        int maximum)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            checks.Add(new AdminOperationalCheckDto(area, name, "ok", $"{key} is not set; the application default is used.", null));
            return;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            checks.Add(new AdminOperationalCheckDto(area, name, "critical", $"{key} must be an integer.", value));
            return;
        }

        var inRange = parsed >= minimum && parsed <= maximum;
        checks.Add(new AdminOperationalCheckDto(
            area,
            name,
            inRange ? "ok" : "critical",
            inRange ? $"{key} is valid." : $"{key} must be between {minimum} and {maximum}.",
            parsed.ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddBooleanCheck(
        List<AdminOperationalCheckDto> checks,
        IConfiguration configuration,
        string key,
        string area,
        string name)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            checks.Add(new AdminOperationalCheckDto(area, name, "ok", $"{key} is not set; the default is used.", null));
            return;
        }

        checks.Add(
            bool.TryParse(value, out var parsed)
                ? new AdminOperationalCheckDto(area, name, "ok", $"{key} is {parsed.ToString().ToLowerInvariant()}.", parsed.ToString().ToLowerInvariant())
                : new AdminOperationalCheckDto(area, name, "critical", $"{key} must be true or false.", value));
    }

    private static void AddQuotedValueCheck(
        List<AdminOperationalCheckDto> checks,
        IConfiguration configuration,
        IReadOnlyList<string> keys)
    {
        var quotedKeys = keys.Where(key => IsQuoted(configuration[key])).ToList();
        checks.Add(new AdminOperationalCheckDto(
            "Configuration",
            "Quoted environment values",
            quotedKeys.Count == 0 ? "ok" : "critical",
            quotedKeys.Count == 0
                ? "No known setting has literal outer quotes."
                : $"Remove literal outer quotes from: {string.Join(", ", quotedKeys)}.",
            quotedKeys.Count == 0 ? null : quotedKeys.Count.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool IsEnabled(string? value) => bool.TryParse(value?.Trim(), out var parsed) && parsed;

    private static bool IsQuoted(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is { Length: >= 2 } && trimmed.StartsWith('"') && trimmed.EndsWith('"');
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string MaskSecret(string value) =>
        value.Length <= 8 ? "********" : $"{value[..4]}...{value[^4..]}";

    private static string RedactConnectionString(string value) =>
        string.Join(
            ';',
            value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ? "Password=********" : part));
}
