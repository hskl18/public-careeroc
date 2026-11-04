using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Careeroc.Infrastructure.Email;

public sealed class DatabaseModelCallEventRecorder : IModelCallEventRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseModelCallEventRecorder> _logger;

    public DatabaseModelCallEventRecorder(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseModelCallEventRecorder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordAsync(ModelCallEventRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CareerocDbContext>();
            var modelCallEvent = new ModelCallEvent
            {
                OccurredAtUtc = record.OccurredAtUtc,
                Provider = Normalize(record.Provider, 80),
                Model = Normalize(record.Model, 160),
                Purpose = Normalize(record.Purpose, 120),
                Outcome = Normalize(record.Outcome, 40),
                UserEmail = NormalizeNullable(record.UserEmail?.Trim().ToLowerInvariant(), 320),
                InboxAccountId = record.InboxAccountId,
                InboxEmailId = record.InboxEmailId,
                RunId = NormalizeNullable(record.RunId, 120),
                CorrelationId = NormalizeNullable(record.CorrelationId, 120),
                PromptTokens = record.PromptTokens,
                CompletionTokens = record.CompletionTokens,
                TotalTokens = record.TotalTokens,
                EstimatedCostUsd = record.EstimatedCostUsd,
                LatencyMs = Math.Max(0, record.LatencyMs),
                StatusCode = record.StatusCode,
                ErrorType = NormalizeNullable(record.ErrorType, 160)
            };

            dbContext.ModelCallEvents.Add(modelCallEvent);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (!string.Equals(modelCallEvent.Outcome, "success", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Model call recorded with non-success outcome. Provider={Provider} Model={Model} Purpose={Purpose} Outcome={Outcome} ErrorType={ErrorType} StatusCode={StatusCode} UserEmail={UserEmail} InboxAccountId={InboxAccountId} InboxEmailId={InboxEmailId} RunId={RunId} CorrelationId={CorrelationId} LatencyMs={LatencyMs}.",
                    modelCallEvent.Provider,
                    modelCallEvent.Model,
                    modelCallEvent.Purpose,
                    modelCallEvent.Outcome,
                    modelCallEvent.ErrorType,
                    modelCallEvent.StatusCode,
                    modelCallEvent.UserEmail,
                    modelCallEvent.InboxAccountId,
                    modelCallEvent.InboxEmailId,
                    modelCallEvent.RunId,
                    modelCallEvent.CorrelationId,
                    modelCallEvent.LatencyMs);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to persist model call event for purpose {Purpose} and model {Model}.",
                record.Purpose,
                record.Model);
        }
    }

    private static string Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
