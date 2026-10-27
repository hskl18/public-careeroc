using Careeroc.Application.Abstractions;

namespace Careeroc.Tests;

internal sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; }
}
