using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Applications;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Tests;

public sealed class ApplicationArtifactEvidenceServiceTests
{
    [Fact]
    public async Task CreateAsync_StoresEvidenceCardWithoutMutatingApplication()
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
            Priority = 5
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 5, 4, 20, 0, 0, TimeSpan.Zero);
        var service = new ApplicationArtifactEvidenceService(dbContext, new TestClock(now));
        var request = new ApplicationArtifactInputDto(
            "interview_screenshot",
            "interview.png",
            "image/png",
            Convert.ToBase64String(MinimalPngBytes()),
            "Interview screenshot says final round is scheduled for 2026-05-12 at 10:00 AM. https://calendar.example.com/secret");

        var response = await service.CreateAsync(application.Id, request, user.Email);

        Assert.NotNull(response);
        Assert.Equal(application.Id, response.JobApplicationId);
        Assert.Equal("schedule", response.ExtractedFactType);
        Assert.False(response.RequiresReview);
        Assert.Equal("evidence_recorded_no_mutation", response.TraceSummary.ReviewGateResult);
        Assert.Equal("stored_evidence_only_no_mutation", response.TraceSummary.FallbackPath);
        Assert.Equal("gemma4:31b", response.TraceSummary.ModelPath);
        Assert.Equal("artifact_evidence_extraction", response.TraceSummary.Purpose);
        Assert.Equal(now, response.CreatedAtUtc);

        var refreshedApplication = await dbContext.JobApplications.SingleAsync();
        Assert.Equal(ApplicationStatus.Interviewing, refreshedApplication.Status);
        Assert.Empty(dbContext.Reminders);
        var storedEvidence = await dbContext.ApplicationArtifactEvidence.SingleAsync();
        Assert.DoesNotContain("calendar.example.com", storedEvidence.SourceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ReviewGatesLowConfidenceArtifact()
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
            Company = "Anthropic",
            Role = "Backend Engineer",
            Source = "Email",
            Status = ApplicationStatus.Applied
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationArtifactEvidenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 4, 20, 0, 0, TimeSpan.Zero)));
        var request = new ApplicationArtifactInputDto(
            "offer_pdf",
            "offer.pdf",
            "application/pdf",
            Convert.ToBase64String("%PDF-1.7\n"u8.ToArray()),
            "Offer attachment.");

        var response = await service.CreateAsync(application.Id, request, user.Email);

        Assert.NotNull(response);
        Assert.True(response.RequiresReview);
        Assert.Equal("manual_review_required", response.TraceSummary.ReviewGateResult);
        Assert.Equal("manual_review_no_mutation", response.TraceSummary.FallbackPath);
        Assert.Contains("not confident enough", response.ReviewReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_RejectsMismatchedContentType()
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
            Source = "Email"
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationArtifactEvidenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 4, 20, 0, 0, TimeSpan.Zero)));
        var request = new ApplicationArtifactInputDto(
            "interview_screenshot",
            "interview.png",
            "image/png",
            Convert.ToBase64String("not a png"u8.ToArray()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(application.Id, request, user.Email));
        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_UsesVisionExtractionForImageEvidence()
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
            Status = ApplicationStatus.Applied
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        await dbContext.SaveChangesAsync();

        var visionExtractor = new FakeVisionTextExtractor(
            new ResumeVisionTextExtractionDecision(
                "Final interview confirmed for 2026-05-18 at 2 PM with hiring manager.",
                0.92m,
                "Resume Vision Agent: Gemma via Ollama (model-router-fallback)"));
        var service = new ApplicationArtifactEvidenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 4, 20, 0, 0, TimeSpan.Zero)),
            visionExtractor);
        var request = new ApplicationArtifactInputDto(
            "interview_screenshot",
            "interview.png",
            "image/png",
            Convert.ToBase64String(MinimalPngBytes()));

        var response = await service.CreateAsync(application.Id, request, user.Email);

        Assert.NotNull(response);
        Assert.False(response.RequiresReview);
        Assert.Equal(new DateTimeOffset(2026, 5, 18, 0, 0, 0, TimeSpan.Zero), response.NormalizedDateUtc);
        Assert.Contains("Final interview confirmed", response.SourceSnippet);
        Assert.Equal("vision:interview_screenshot:interview.png", response.TraceSummary.EvidenceSource);
        Assert.Equal("model-router-fallback", response.TraceSummary.ModelPath);
        Assert.Equal("application_artifact:interview_screenshot", visionExtractor.LastRequest?.SourceKind);
    }

    [Fact]
    public async Task ReviewAsync_ClearsArtifactReviewWithoutMutatingApplicationOrReminders()
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
            LastActivityAtUtc = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero)
        };
        var evidence = new ApplicationArtifactEvidence
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
            Confidence = 0.54m,
            SourceSnippet = "Offer deadline may be 2026-05-12.",
            ModelPath = "model-router-fallback",
            Purpose = "artifact_evidence_extraction",
            EvidenceSource = "pdf_text:offer_pdf:offer.pdf",
            ReviewGateResult = "manual_review_required",
            FallbackPath = "manual_review_no_mutation",
            RequiresReview = true,
            ReviewReason = "Artifact evidence is not confident enough to mutate application state or create reminders.",
            CreatedAtUtc = new DateTimeOffset(2026, 5, 4, 11, 0, 0, TimeSpan.Zero)
        };
        dbContext.Users.Add(user);
        dbContext.JobApplications.Add(application);
        dbContext.ApplicationArtifactEvidence.Add(evidence);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationArtifactEvidenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.ReviewAsync(
            application.Id,
            evidence.Id,
            new ReviewApplicationArtifactRequest("keep_evidence", "Verified manually from the offer PDF."),
            user.Email);

        Assert.NotNull(response);
        Assert.False(response.RequiresReview);
        Assert.Equal("manual_review_kept_no_mutation", response.TraceSummary.ReviewGateResult);
        Assert.Equal("stored_evidence_only_no_mutation", response.TraceSummary.FallbackPath);
        Assert.Equal("Verified manually from the offer PDF.", response.ReviewReason);

        var refreshedApplication = await dbContext.JobApplications.SingleAsync();
        Assert.Equal(ApplicationStatus.Interviewing, refreshedApplication.Status);
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero), refreshedApplication.LastActivityAtUtc);
        Assert.Empty(dbContext.Reminders);
    }

    [Fact]
    public async Task ReviewAsync_RejectsCrossUserArtifactReview()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var owner = new User
        {
            Email = "owner@example.com",
            Name = "Owner"
        };
        var otherUser = new User
        {
            Email = "other@example.com",
            Name = "Other"
        };
        var application = new JobApplication
        {
            UserId = owner.Id,
            User = owner,
            Company = "Anthropic",
            Role = "Backend Engineer",
            Source = "Email"
        };
        var evidence = new ApplicationArtifactEvidence
        {
            UserId = owner.Id,
            User = owner,
            JobApplicationId = application.Id,
            JobApplication = application,
            ArtifactKind = "offer_pdf",
            FileName = "offer.pdf",
            ContentType = "application/pdf",
            ContentHash = "abc123",
            SafeTitle = "offer pdf evidence: offer.pdf",
            ExtractedFactType = "offer_detail",
            ExtractedFactText = "Offer deadline is unclear.",
            Confidence = 0.44m,
            SourceSnippet = "Offer deadline is unclear.",
            ModelPath = "model-router-fallback",
            Purpose = "artifact_evidence_extraction",
            EvidenceSource = "pdf_text:offer_pdf:offer.pdf",
            ReviewGateResult = "manual_review_required",
            FallbackPath = "manual_review_no_mutation",
            RequiresReview = true,
            CreatedAtUtc = new DateTimeOffset(2026, 5, 4, 11, 0, 0, TimeSpan.Zero)
        };
        dbContext.Users.AddRange(owner, otherUser);
        dbContext.JobApplications.Add(application);
        dbContext.ApplicationArtifactEvidence.Add(evidence);
        await dbContext.SaveChangesAsync();

        var service = new ApplicationArtifactEvidenceService(
            dbContext,
            new TestClock(new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero)));

        var response = await service.ReviewAsync(
            application.Id,
            evidence.Id,
            new ReviewApplicationArtifactRequest("dismiss_evidence"),
            otherUser.Email);

        Assert.Null(response);
        Assert.True((await dbContext.ApplicationArtifactEvidence.SingleAsync()).RequiresReview);
    }

    private static byte[] MinimalPngBytes()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D
        ];
    }

    private sealed class FakeVisionTextExtractor(ResumeVisionTextExtractionDecision? decision) : IResumeVisionTextExtractor
    {
        public ResumeVisionTextExtractionRequest? LastRequest { get; private set; }

        public Task<ResumeVisionTextExtractionDecision?> ExtractAsync(
            ResumeVisionTextExtractionRequest request,
            CancellationToken cancellationToken = default,
            ModelCallContext? modelCallContext = null)
        {
            LastRequest = request;
            return Task.FromResult(decision);
        }
    }
}
