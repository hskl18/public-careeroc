using Careeroc.Application;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ManualReviewQueryServiceTests
{
    [Fact]
    public async Task GetAsync_IncludesSameThreadMessages_ForReviewEmailEvidence()
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
            Company = "Figma",
            Role = "Product Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied,
            Priority = 3,
            LastActivityAtUtc = new DateTimeOffset(2026, 4, 4, 16, 0, 0, TimeSpan.Zero),
            CreatedAtUtc = new DateTimeOffset(2026, 4, 4, 16, 0, 0, TimeSpan.Zero)
        };

        var threadId = "thread-figma-001";
        var earlierEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "figma-application-001",
            ThreadId = threadId,
            Sender = "careers@figma.com",
            Subject = "We received your application",
            ParsedBody = "Thanks for applying to the Product Engineer role.",
            RawBody = "Thanks for applying to the Product Engineer role.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 4, 17, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.ApplicationReceived,
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 4, 17, 2, 0, TimeSpan.Zero)
        };

        var reviewEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            JobApplicationId = application.Id,
            JobApplication = application,
            ExternalMessageId = "figma-interview-001",
            ThreadId = threadId,
            Sender = "coordinator@figma.com",
            Subject = "Interview invitation",
            ParsedBody = "We would love to schedule your first interview next week.",
            RawBody = "We would love to schedule your first interview next week.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.GenericUpdate,
            ContactEmail = "coordinator@figma.com",
            ContactType = RecruitingContactType.Coordinator,
            ContactRoleHint = "Recruiting Coordinator",
            ClassificationConfidence = 0.48m,
            MatchingConfidence = 0.55m,
            RequiresManualReview = true,
            ReviewReason = "Role could not be extracted confidently.",
            ProcessingTraceJson = InboxEmailAgentTraceJson.Serialize(
                new InboxEmailAgentTrace(
                    new DateTimeOffset(2026, 4, 5, 10, 2, 0, TimeSpan.Zero),
                    [
                        new AgentTraceStep(
                            "triage",
                            "Inbox Triage Agent",
                            "Escalated to structured workflow extraction.",
                            "Matched recruiting intent.",
                            [new AgentTraceFact("Recruiting related", "Yes")]),
                        new AgentTraceStep(
                            "workflow_extraction",
                            "Workflow Extraction Agent",
                            "Detected an interview-like thread but could not lock the role confidently.",
                            null,
                            [
                                new AgentTraceFact("Category", "generic_update"),
                                new AgentTraceFact("Company", "Figma"),
                                new AgentTraceFact("Role", "Unknown Role"),
                                new AgentTraceFact("Parsing source", "Heuristic parser")
                            ]),
                        new AgentTraceStep(
                            "review_evidence",
                            "Review Evidence Agent",
                            "Escalated with thread evidence because the workflow state is not yet trustworthy.",
                            "Role could not be extracted confidently.",
                            [new AgentTraceFact("Requires manual review", "Yes")])
                    ])),
            ProcessedAtUtc = new DateTimeOffset(2026, 4, 5, 10, 2, 0, TimeSpan.Zero)
        };

        var unrelatedReviewEmail = new InboxEmail
        {
            InboxAccountId = inboxAccount.Id,
            InboxAccount = inboxAccount,
            ExternalMessageId = "other-review-001",
            ThreadId = "thread-other-001",
            Sender = "recruiting@example.com",
            Subject = "Follow-up",
            ParsedBody = "Please reply with your availability.",
            RawBody = "Please reply with your availability.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 6, 11, 0, 0, TimeSpan.Zero),
            Category = EmailCategory.GenericUpdate,
            RequiresManualReview = true,
            ReviewReason = "Could not confidently map this email."
        };

        dbContext.Users.Add(user);
        dbContext.InboxAccounts.Add(inboxAccount);
        dbContext.JobApplications.Add(application);
        dbContext.InboxEmails.AddRange(earlierEmail, reviewEmail, unrelatedReviewEmail);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewQueryService(dbContext);
        var response = await service.GetAsync(user.Email);

        var selectedEmail = response.Emails.Single(email => email.Id == reviewEmail.Id);

        Assert.Equal(threadId, selectedEmail.ThreadId);
        Assert.Equal(2, selectedEmail.ThreadMessages.Count);
        Assert.Equal(earlierEmail.Id, selectedEmail.ThreadMessages[0].Id);
        Assert.Equal("application_received", selectedEmail.ThreadMessages[0].Category);
        Assert.False(selectedEmail.ThreadMessages[0].IsCurrentEmail);
        Assert.Equal(reviewEmail.Id, selectedEmail.ThreadMessages[1].Id);
        Assert.True(selectedEmail.ThreadMessages[1].IsCurrentEmail);
        Assert.Equal("generic_update", selectedEmail.ThreadMessages[1].Category);
        Assert.NotNull(selectedEmail.AgentTrace);
        Assert.Equal(3, selectedEmail.AgentTrace!.Steps.Count);
        Assert.Equal("Workflow Extraction Agent: Heuristic parser", selectedEmail.ProcessingSource);
        Assert.Equal("workflow_extraction", selectedEmail.AgentTrace.Steps[1].Stage);
        Assert.Equal("coordinator@figma.com", selectedEmail.ContactEmail);
        Assert.Equal("Coordinator", selectedEmail.ContactType);
        Assert.Equal(2, response.TotalWaiting);
        Assert.Equal(1, response.TotalUnassigned);
        Assert.Equal(0, response.TotalArtifactReviews);
    }

    [Fact]
    public async Task GetAsync_IncludesArtifactEvidenceWaitingForReview()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var user = new User
        {
            Email = "demo@example.com",
            Name = "Alex Rivera"
        };

        var application = new JobApplication
        {
            UserId = user.Id,
            User = user,
            Company = "OpenAI",
            Role = "Research Engineer",
            Source = "Email",
            Status = ApplicationStatus.Interviewing,
            Priority = 4,
            LastActivityAtUtc = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero),
            CreatedAtUtc = new DateTimeOffset(2026, 5, 5, 10, 0, 0, TimeSpan.Zero)
        };

        var reviewArtifact = new ApplicationArtifactEvidence
        {
            UserId = user.Id,
            User = user,
            JobApplicationId = application.Id,
            JobApplication = application,
            ArtifactKind = "offer_pdf",
            FileName = "offer.pdf",
            ContentType = "application/pdf",
            ContentHash = "abc123",
            SafeTitle = "offer pdf evidence: offer.pdf",
            ExtractedFactType = "offer_detail",
            ExtractedFactText = "Offer deadline may be 2026-05-12.",
            NormalizedDateUtc = new DateTimeOffset(2026, 5, 12, 0, 0, 0, TimeSpan.Zero),
            Confidence = 0.54m,
            SourceSnippet = "Offer deadline may be 2026-05-12, but the company name is unclear.",
            ModelPath = "model-router-fallback",
            Purpose = "artifact_evidence_extraction",
            EvidenceSource = "pdf_text:offer_pdf:offer.pdf",
            ReviewGateResult = "manual_review_required",
            FallbackPath = "manual_review_no_mutation",
            RequiresReview = true,
            ReviewReason = "Artifact evidence is not confident enough to mutate application state or create reminders.",
            CreatedAtUtc = new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero)
        };

        var clearedArtifact = new ApplicationArtifactEvidence
        {
            UserId = user.Id,
            User = user,
            JobApplicationId = application.Id,
            JobApplication = application,
            ArtifactKind = "interview_screenshot",
            FileName = "interview.png",
            ContentType = "image/png",
            ContentHash = "def456",
            SafeTitle = "interview screenshot evidence: interview.png",
            ExtractedFactType = "schedule",
            ExtractedFactText = "Interview confirmed for 2026-05-18.",
            Confidence = 0.86m,
            SourceSnippet = "Interview confirmed for 2026-05-18.",
            ModelPath = "model-router-fallback",
            Purpose = "artifact_evidence_extraction",
            EvidenceSource = "vision:interview_screenshot:interview.png",
            ReviewGateResult = "evidence_recorded_no_mutation",
            FallbackPath = "stored_evidence_only_no_mutation",
            RequiresReview = false,
            CreatedAtUtc = new DateTimeOffset(2026, 5, 5, 12, 30, 0, TimeSpan.Zero)
        };

        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.ApplicationArtifactEvidence.AddRange(reviewArtifact, clearedArtifact);
        await dbContext.SaveChangesAsync();

        var service = new ManualReviewQueryService(dbContext);
        var response = await service.GetAsync(user.Email);

        var artifact = Assert.Single(response.Artifacts);
        Assert.Equal(reviewArtifact.Id, artifact.Id);
        Assert.Equal(application.Id, artifact.JobApplicationId);
        Assert.Equal("OpenAI", artifact.Company);
        Assert.Equal("Research Engineer", artifact.Role);
        Assert.Equal("manual_review_required", artifact.TraceSummary.ReviewGateResult);
        Assert.Equal("manual_review_no_mutation", artifact.TraceSummary.FallbackPath);
        Assert.Equal("model-router-fallback", artifact.TraceSummary.ModelPath);
        Assert.Equal(1, response.TotalWaiting);
        Assert.Equal(0, response.TotalUnassigned);
        Assert.Equal(1, response.TotalArtifactReviews);
    }
}
