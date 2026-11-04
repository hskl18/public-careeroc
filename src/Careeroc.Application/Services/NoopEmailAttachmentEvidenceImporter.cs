using Careeroc.Application.Abstractions;

namespace Careeroc.Application.Services;

public sealed class NoopEmailAttachmentEvidenceImporter : IEmailAttachmentEvidenceImporter
{
    public static NoopEmailAttachmentEvidenceImporter Instance { get; } = new();

    private NoopEmailAttachmentEvidenceImporter()
    {
    }

    public Task<int> ImportForEmailAsync(
        Guid inboxEmailId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0);
    }
}
