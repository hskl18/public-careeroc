using Careeroc.Api.Endpoints;
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
            context.Request.Method,
            context.Request.Path,
            ApiRateLimitPartitioner.GetRemoteIp(context),
            error,
            NormalizeValue(context.Request.Headers.UserAgent.ToString()));
    }

    public void LogRateLimitRejected(HttpContext context, string partitionKey, TimeSpan? retryAfter)
    {
        _logger.LogWarning(
            "SecurityEvent {EventType} rate limited request {Method} {Path} from {RemoteIp}. Partition={PartitionKey} RetryAfterSeconds={RetryAfterSeconds} UserEmail={UserEmail}",
            "api.rate_limited",
            context.Request.Method,
            context.Request.Path,
            ApiRateLimitPartitioner.GetRemoteIp(context),
            partitionKey,
            retryAfter is null ? null : Math.Ceiling(retryAfter.Value.TotalSeconds),
            NormalizeValue(RequestUserContext.GetUserEmail(context.Request)));
    }

    public async Task LogAccessDeniedAsync(HttpContext context, string userEmail)
    {
        _logger.LogWarning(
            "SecurityEvent {EventType} rejected request {Method} {Path} from {RemoteIp}. UserEmail={UserEmail}",
            "api.access_denied",
            context.Request.Method,
            context.Request.Path,
            ApiRateLimitPartitioner.GetRemoteIp(context),
            NormalizeValue(userEmail));

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
            "AuditEvent {EventType} {Outcome} resource {ResourceType}/{ResourceId} for {UserEmail}. Method={Method} Path={Path} RemoteIp={RemoteIp} Detail={Detail}",
            eventType,
            outcome,
            resourceType,
            resourceId,
            NormalizeValue(userEmail),
            request.Method,
            request.Path,
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
            "AuditEvent {EventType} {Outcome} for {UserEmail}. TargetEmail={TargetEmail} Method={Method} Path={Path} RemoteIp={RemoteIp} Detail={Detail}",
            eventType,
            outcome,
            NormalizeValue(userEmail),
            NormalizeValue(targetEmail),
            request.Method,
            request.Path,
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

    private static string NormalizeValue(string? value)
    {
        return SensitiveDataRedactor.Redact(value, maxLength: 500) ?? "n/a";
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
