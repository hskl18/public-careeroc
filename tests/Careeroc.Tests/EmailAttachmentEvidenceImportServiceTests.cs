using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Careeroc.Tests;

public sealed class EmailAttachmentEvidenceImportServiceTests
{
    [Fact]
    public async Task ImportForEmailAsync_DownloadsAttachmentAfterApplicationLinkAndDeduplicates()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };
        var inboxAccount = new InboxAccount
        {
            UserId = user.Id,
            User = user,
            Provider = ApplicationDefaults.GmailInboxProvider,
            EmailAddress = user.Email
        };
        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Software Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied
        };
        var inboxEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "gmail-message-1",
            ThreadId = "thread-1",
            Sender = "recruiting@example.com",
            Subject = "Offer packet",
            RawBody = "Please see the attached offer.",
            ParsedBody = "Please see the attached offer.",
            ReceivedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.Add(inboxEmail);
        await dbContext.SaveChangesAsync();

        var downloader = new FixedAttachmentDownloader(
            [
                new DownloadedInboxAttachment(
                    "attachment-1",
                    "offer.pdf",
                    "application/pdf",
                    9,
                    "%PDF-1.7\n"u8.ToArray())
            ]);
        var clock = new TestClock(new DateTimeOffset(2026, 5, 5, 18, 0, 0, TimeSpan.Zero));
        var artifactService = new ApplicationArtifactEvidenceService(dbContext, clock);
        var importService = new EmailAttachmentEvidenceImportService(
            dbContext,
            downloader,
            artifactService,
            NullLogger<EmailAttachmentEvidenceImportService>.Instance);

        var firstImportCount = await importService.ImportForEmailAsync(inboxEmail.Id);
        var secondImportCount = await importService.ImportForEmailAsync(inboxEmail.Id);

        Assert.Equal(1, firstImportCount);
        Assert.Equal(0, secondImportCount);
        var evidence = await dbContext.ApplicationArtifactEvidence.SingleAsync();
        Assert.Equal(application.Id, evidence.JobApplicationId);
        Assert.Equal("offer_pdf", evidence.ArtifactKind);
        Assert.Equal("manual_review_required", evidence.ReviewGateResult);
        Assert.Equal("manual_review_no_mutation", evidence.FallbackPath);
        Assert.Empty(dbContext.Reminders);
    }

    private sealed class FixedAttachmentDownloader : IInboxAttachmentDownloader
    {
        private readonly IReadOnlyList<DownloadedInboxAttachment> _attachments;

        public FixedAttachmentDownloader(IReadOnlyList<DownloadedInboxAttachment> attachments)
        {
            _attachments = attachments;
        }

        public Task<IReadOnlyList<DownloadedInboxAttachment>> DownloadSupportedAttachmentsAsync(
            InboxEmail inboxEmail,
            int maxBytes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_attachments);
        }
    }
}
