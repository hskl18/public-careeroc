using Careeroc.Domain.Entities;

namespace Careeroc.Application.Abstractions;

public interface IExplicitDueDateExtractor
{
    Task<DateTimeOffset?> ExtractDueDateUtcAsync(
        InboxEmail inboxEmail,
        CancellationToken cancellationToken = default);
}
