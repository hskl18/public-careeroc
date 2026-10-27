using System.Security.Cryptography;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Careeroc.Application.Services;

public sealed class EmailAttachmentEvidenceImportService : IEmailAttachmentEvidenceImporter
{
    private const int MaxArtifactBytes = 5 * 1024 * 1024;

    private readonly ICareerocDbContext _dbContext;
    private readonly IInboxAttachmentDownloader _attachmentDownloader;
    private readonly ApplicationArtifactEvidenceService _artifactEvidenceService;
    private readonly ILogger<EmailAttachmentEvidenceImportService> _logger;

    public EmailAttachmentEvidenceImportService(
        ICareerocDbContext dbContext,
        IInboxAttachmentDownloader attachmentDownloader,
        ApplicationArtifactEvidenceService artifactEvidenceService,
        ILogger<EmailAttachmentEvidenceImportService> logger)
    {
        _dbContext = dbContext;
        _attachmentDownloader = attachmentDownloader;
        _artifactEvidenceService = artifactEvidenceService;
        _logger = logger;
    }

    public async Task<int> ImportForEmailAsync(
        Guid inboxEmailId,
        CancellationToken cancellationToken = default)
    {
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("artifact.import_email_attachments");
        activity?.SetTag("email.inbox_email_id", inboxEmailId.ToString());

        var inboxEmail = await _dbContext.InboxEmails
            .Include(email => email.InboxAccount)
            .ThenInclude(account => account.User)
            .FirstOrDefaultAsync(email => email.Id == inboxEmailId, cancellationToken);
        if (inboxEmail?.JobApplicationId is null)
        {
            return 0;
        }

        IReadOnlyList<DownloadedInboxAttachment> attachments;
        try
        {
            attachments = await _attachmentDownloader.DownloadSupportedAttachmentsAsync(
                inboxEmail,
                MaxArtifactBytes,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Skipped attachment evidence import for inbox email {InboxEmailId}; attachment discovery failed.",
                inboxEmailId);
            return 0;
        }

        var imported = 0;
        foreach (var attachment in attachments)
        {
            var contentHash = Convert.ToHexString(SHA256.HashData(attachment.ContentBytes)).ToLowerInvariant();
            var alreadyImported = await _dbContext.ApplicationArtifactEvidence.AnyAsync(
                evidence =>
                    evidence.JobApplicationId == inboxEmail.JobApplicationId.Value &&
                    evidence.ContentHash == contentHash,
                cancellationToken);
            if (alreadyImported)
            {
                continue;
            }

            var request = new ApplicationArtifactInputDto(
                InferArtifactKind(attachment.FileName),
                attachment.FileName,
                attachment.ContentType,
                Convert.ToBase64String(attachment.ContentBytes),
                "Gmail attachment from synced recruiting email.");

            try
            {
                var created = await _artifactEvidenceService.CreateAsync(
                    inboxEmail.JobApplicationId.Value,
                    request,
                    inboxEmail.InboxAccount.User.Email,
                    cancellationToken);

                if (created is not null)
                {
                    imported++;
                }
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogInformation(
                    exception,
                    "Skipped unsupported attachment {FileName} from inbox email {InboxEmailId}.",
                    attachment.FileName,
                    inboxEmailId);
            }
        }

        activity?.SetTag("artifact.imported_count", imported);
        return imported;
    }

    private static string InferArtifactKind(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("offer", StringComparison.Ordinal))
        {
            return "offer_pdf";
        }

        if (lower.Contains("assessment", StringComparison.Ordinal) ||
            lower.Contains("oa", StringComparison.Ordinal))
        {
            return "assessment_pdf";
        }

        if (lower.Contains("interview", StringComparison.Ordinal) ||
            lower.Contains("schedule", StringComparison.Ordinal))
        {
            return "interview_screenshot";
        }

        return "recruiting_artifact";
    }
}
