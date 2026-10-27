using Careeroc.Contracts.Profile;

namespace Careeroc.Application.Abstractions;

public sealed class NullUserAgentContextSnapshotProvider : IUserAgentContextSnapshotProvider
{
    public static NullUserAgentContextSnapshotProvider Instance { get; } = new(new NullClock());

    private readonly IClock _clock;

    public NullUserAgentContextSnapshotProvider(IClock clock)
    {
        _clock = clock;
    }

    public Task<UserAgentContextSnapshotDto> GetSnapshotAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        return Task.FromResult(
            new UserAgentContextSnapshotDto(
                nowUtc,
                """
                # memory.md

                No confirmed long-term memory is available in this execution context.
                """,
                """
                # soul.md

                No user soul profile is available in this execution context.
                """,
                0,
                null,
                null));
    }

    private sealed class NullClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Now.ToUniversalTime();
    }
}
