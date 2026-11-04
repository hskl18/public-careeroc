using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Abstractions;

public interface IInboxEmailSource
{
    Task<IReadOnlyList<ExternalInboxEmail>> GetRecentEmailsAsync(
        InboxAccount inboxAccount,
        IReadOnlySet<string>? existingMessageIds = null,
        IReadOnlySet<string>? existingThreadIds = null,
        CancellationToken cancellationToken = default);
}
