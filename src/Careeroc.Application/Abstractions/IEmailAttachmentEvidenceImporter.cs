namespace Careeroc.Application.Abstractions;

public interface IEmailAttachmentEvidenceImporter
{
    Task<int> ImportForEmailAsync(
        Guid inboxEmailId,
        CancellationToken cancellationToken = default);
}
