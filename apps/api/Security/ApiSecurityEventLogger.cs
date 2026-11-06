using Careeroc.Api.Endpoints;
using Careeroc.Application;
using Careeroc.Application.Security;
using Careeroc.Domain.Entities;
using Careeroc.Infrastructure.Persistence;

namespace Careeroc.Api.Security;

internal sealed class ApiSecurityEventLogger
{
    private readonly ILogger<ApiSecurityEventLogger> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ApiSecurityEventLogger(
        ILogger<ApiSecurityEventLogger> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // Unsigned requests and rate-limited requests are logged but never stored: anyone can send them
    // as fast as they like, and a row per request would let them fill the database.
    public void LogRequestSignatureRejected(HttpContext context, string error)
    {
        _logger.LogWarning(
            "SecurityEvent {EventType} rejected request {Method} {Path} from {RemoteIp}. Error={Error} UserAgent={UserAgent}",
            "api.request_signature_rejected",
            NormalizeValue(context.Request.Method),
            NormalizeValue(context.Request.Path.Value),
            ApiRateLimitPartitioner.GetRemoteIp(context),
            NormalizeValue(error),
            NormalizeValue(context.Request.Headers.UserAgent.ToString()));
    }

    public void LogRateLimitRejected(HttpContext context, string partitionKey, TimeSpan? retryAfter)
    {
        _logger.LogWarning(
            "SecurityEvent {EventType} rate limited request {Method} {Path} from {RemoteIp}. Partition={PartitionKey} RetryAfterSeconds={RetryAfterSeconds} UserKey={UserKey}",
            "api.rate_limited",
            NormalizeValue(context.Request.Method),
            NormalizeValue(context.Request.Path.Value),
            ApiRateLimitPartitioner.GetRemoteIp(context),
            NormalizeValue(partitionKey),
            retryAfter is null ? null : Math.Ceiling(retryAfter.Value.TotalSeconds),
            LogUserKey(RequestUserContext.GetUserEmail(context.Request)));
    }

    public async Task LogAccessDeniedAsync(HttpContext context, string userEmail)
    {
        _logger.LogWarning(
            "SecurityEvent {EventType} rejected request {Method} {Path} from {RemoteIp}. UserKey={UserKey}",
            "api.access_denied",
            NormalizeValue(context.Request.Method),
            NormalizeValue(context.Request.Path.Value),
            ApiRateLimitPartitioner.GetRemoteIp(context),
            LogUserKey(userEmail));

        await PersistAuditEventAsync(
            new AuditEvent
            {
                EventType = "api.access_denied",
                Outcome = "rejected",
                UserEmail = NormalizeNullable(userEmail),
                Method = context.Request.Method,
                Path = context.Request.Path,
                RemoteIp = ApiRateLimitPartitioner.GetRemoteIp(context)
            });
    }

    public async Task LogMutationAsync(
        HttpRequest request,
        string eventType,
        string? userEmail,
        string resourceType,
        Guid? resourceId,
        string outcome,
        string? detail = null)
    {
        _logger.LogInformation(
            "AuditEvent {EventType} {Outcome} resource {ResourceType}/{ResourceId} for {UserKey}. Method={Method} Path={Path} RemoteIp={RemoteIp} Detail={Detail}",
            NormalizeValue(eventType),
            NormalizeValue(outcome),
            NormalizeValue(resourceType),
            resourceId,
            LogUserKey(userEmail),
            NormalizeValue(request.Method),
            NormalizeValue(request.Path.Value),
            ApiRateLimitPartitioner.GetRemoteIp(request.HttpContext),
            NormalizeValue(detail));

        await PersistAuditEventAsync(
            new AuditEvent
            {
                EventType = NormalizeRequired(eventType, "unknown.mutation"),
                Outcome = NormalizeRequired(outcome, "unknown"),
                UserEmail = NormalizeNullable(userEmail),
                ResourceType = NormalizeNullable(resourceType),
                ResourceId = resourceId,
                Method = request.Method,
                Path = request.Path,
                RemoteIp = ApiRateLimitPartitioner.GetRemoteIp(request.HttpContext),
                Detail = NormalizeDetail(detail)
            });
    }

    public async Task LogOAuthEventAsync(
        HttpRequest request,
        string eventType,
        string? userEmail,
        string? targetEmail,
        string outcome,
        string? detail = null)
    {
        _logger.LogInformation(
            "AuditEvent {EventType} {Outcome} for {UserKey}. TargetKey={TargetKey} Method={Method} Path={Path} RemoteIp={RemoteIp} Detail={Detail}",
            NormalizeValue(eventType),
            NormalizeValue(outcome),
            LogUserKey(userEmail),
            LogUserKey(targetEmail),
            NormalizeValue(request.Method),
            NormalizeValue(request.Path.Value),
            ApiRateLimitPartitioner.GetRemoteIp(request.HttpContext),
            NormalizeValue(detail));

        await PersistAuditEventAsync(
            new AuditEvent
            {
                EventType = NormalizeRequired(eventType, "unknown.oauth"),
                Outcome = NormalizeRequired(outcome, "unknown"),
                UserEmail = NormalizeNullable(userEmail),
                TargetEmail = NormalizeNullable(targetEmail),
                Method = request.Method,
                Path = request.Path,
                RemoteIp = ApiRateLimitPartitioner.GetRemoteIp(request.HttpContext),
                Detail = NormalizeDetail(detail)
            });
    }

    // Every value in a log line goes through the redactor: it drops email addresses and tokens, and
    // control characters that could forge a line.
    private static string NormalizeValue(string? value)
    {
        return SensitiveDataRedactor.Redact(value, maxLength: 500) ?? "n/a";
    }

    // Logs name a person by the same hashed key as spans; the audit_events row keeps the address.
    private static string LogUserKey(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? "n/a" : CareerocTelemetry.UserKey(email);
    }

    private async Task PersistAuditEventAsync(AuditEvent auditEvent)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            await dbContext.AuditEvents.AddAsync(auditEvent);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to persist audit event {EventType}.", auditEvent.EventType);
        }
    }

    private static string NormalizeRequired(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeDetail(string? value)
    {
        var normalized = NormalizeNullable(value);
        if (normalized is null)
        {
            return null;
        }

        return SensitiveDataRedactor.Redact(normalized);
    }
}
