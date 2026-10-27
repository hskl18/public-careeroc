using Careeroc.Application.Abstractions;

namespace Careeroc.Infrastructure.Email;

public sealed class OllamaModelCircuitBreaker
{
    public static OllamaModelCircuitBreaker Disabled { get; } =
        new(new DisabledClock(), disabled: true);

    private readonly IClock _clock;
    private readonly bool _disabled;
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _blockedUntilUtc;

    public OllamaModelCircuitBreaker(IClock clock)
        : this(clock, disabled: false)
    {
    }

    private OllamaModelCircuitBreaker(IClock clock, bool disabled)
    {
        _clock = clock;
        _disabled = disabled;
    }

    public bool CanSendRequest(OllamaEmailExtractionOptions options)
    {
        if (_disabled || !IsConfigured(options))
        {
            return true;
        }

        lock (_gate)
        {
            if (_blockedUntilUtc is null)
            {
                return true;
            }

            if (_clock.UtcNow < _blockedUntilUtc.Value)
            {
                return false;
            }

            _blockedUntilUtc = null;
            _consecutiveFailures = 0;
            return true;
        }
    }

    public void RecordSuccess()
    {
        if (_disabled)
        {
            return;
        }

        lock (_gate)
        {
            _consecutiveFailures = 0;
            _blockedUntilUtc = null;
        }
    }

    public void RecordFailure(OllamaEmailExtractionOptions options)
    {
        if (_disabled || !IsConfigured(options))
        {
            return;
        }

        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < options.FailureCircuitBreakerThreshold)
            {
                return;
            }

            _blockedUntilUtc = _clock.UtcNow.AddSeconds(options.FailureCircuitBreakerCooldownSeconds);
        }
    }

    private static bool IsConfigured(OllamaEmailExtractionOptions options)
    {
        return options.FailureCircuitBreakerThreshold > 0 &&
               options.FailureCircuitBreakerCooldownSeconds > 0;
    }

    private sealed class DisabledClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.MinValue;
    }
}
