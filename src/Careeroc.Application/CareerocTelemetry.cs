using System.Diagnostics;
using System.Threading;
using Careeroc.Application.Models;

namespace Careeroc.Application;

public static class CareerocTelemetry
{
    public const string CorrelationHeaderName = "X-Careeroc-Correlation-Id";
    public const string ActivitySourceName = "Careeroc";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly AsyncLocal<CorrelationState?> CurrentState = new();

    public static string? CurrentCorrelationId => CurrentState.Value?.CorrelationId;

    public static string? CurrentRunId => CurrentState.Value?.RunId;

    public static IDisposable BeginCorrelationScope(string? correlationId = null, string? runId = null)
    {
        var previous = CurrentState.Value;
        var resolvedCorrelationId =
            NormalizeTraceValue(correlationId) ??
            previous?.CorrelationId ??
            NormalizeTraceValue(Activity.Current?.TraceId.ToString()) ??
            Guid.NewGuid().ToString("N");
        var resolvedRunId = NormalizeTraceValue(runId) ?? previous?.RunId;

        CurrentState.Value = new CorrelationState(resolvedCorrelationId, resolvedRunId);
        ApplyCurrentTags();

        return new CorrelationScope(previous);
    }

    public static void TagCurrentActivity()
    {
        ApplyCurrentTags();
    }

    public static ModelCallContext AttachCorrelation(ModelCallContext context)
    {
        return context with
        {
            RunId = NormalizeTraceValue(context.RunId) ?? CurrentRunId,
            CorrelationId = NormalizeTraceValue(context.CorrelationId) ?? CurrentCorrelationId
        };
    }

    private static void ApplyCurrentTags()
    {
        var state = CurrentState.Value;
        if (state is null || Activity.Current is null)
        {
            return;
        }

        Activity.Current.SetTag("careeroc.correlation_id", state.CorrelationId);
        if (!string.IsNullOrWhiteSpace(state.RunId))
        {
            Activity.Current.SetTag("careeroc.run_id", state.RunId);
        }
    }

    private static string? NormalizeTraceValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..120];
    }

    private sealed record CorrelationState(string CorrelationId, string? RunId);

    private sealed class CorrelationScope : IDisposable
    {
        private readonly CorrelationState? _previous;
        private bool _disposed;

        public CorrelationScope(CorrelationState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentState.Value = _previous;
            _disposed = true;
        }
    }
}
