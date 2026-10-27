using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;

namespace Careeroc.Application.Services;

public sealed class NoopInboxAttachmentDownloader : IInboxAttachmentDownloader
{
    public static NoopInboxAttachmentDownloader Instance { get; } = new();

    private NoopInboxAttachmentDownloader()
    {
    }

    public Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
        InboxEmail inboxEmail,
        int maxBytes,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DownloadedInboxAttachment>>([]);
    }
}
