using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Abstractions;

public interface IInboxAttachmentDownloader
{
    Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
        InboxEmail inboxEmail,
        int maxBytes,
        CancellationToken cancellationToken = default);
}
