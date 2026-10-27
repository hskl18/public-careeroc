using Careeroc.Application.Models;

namespace Careeroc.Application.Abstractions;

public interface IModelCallEventRecorder
{
    Task RecordAsync(ModelCallEventRecord record, CancellationToken cancellationToken = default);
}
