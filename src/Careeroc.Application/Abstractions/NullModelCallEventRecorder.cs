using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public sealed class NullModelCallEventRecorder : IModelCallEventRecorder
{
    public static NullModelCallEventRecorder Instance { get; } = new();

    private NullModelCallEventRecorder()
    {
    }

    public Task RecordAsync(ModelCallEventRecord record, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
