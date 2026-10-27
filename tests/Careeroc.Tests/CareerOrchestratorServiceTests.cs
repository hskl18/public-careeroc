using System.Text.Json;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Contracts.Orchestrator;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class CareerOrchestratorServiceTests
{
    [Fact]
    public async Task ReviewResumeMailboxFitAsync_WritesResumeMailboxBridgeMemory()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var resumeDocument = new ResumeDocument
        {
            User = user,
            UserId = user.Id,
            Title = "SWE resume"
        };
        var resumeVersion = new ResumeVersion
        {
            ResumeDocument = resumeDocument,
            ResumeDocumentId = resumeDocument.Id,
            Summary = "SWE resume summary",
            SkillsJson = """["TypeScript","React","PostgreSQL"]""",
            ProjectHighlightsJson = """["Careeroc"]""",
            ExperienceCount = 2,
            ProjectCount = 1,
            CreatedAtUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            Evaluation = new ResumeEvaluation
            {
                Score = 84,
                Verdict = "Strong resume foundation.",
                StrengthsJson = "[]",
                GapsJson = "[]",
                NextActionsJson = "[]",
                EvidenceJson = "{}"
            }
        };
        resumeVersion.Evaluation.ResumeVersion = resumeVersion;
        resumeVersion.Evaluation.ResumeVersionId = resumeVersion.Id;
        resumeDocument.Versions.Add(resumeVersion);
        dbContext.Users.Add(user);
        dbContext.ResumeDocuments.Add(resumeDocument);
        dbContext.JobApplications.Add(
            new JobApplication
            {
                User = user,
                UserId = user.Id,
                Company = "Roblox",
                Role = "Software Engineering Intern",
                Status = ApplicationStatus.Interviewing,
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero)
            });
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "full-stack TypeScript",
                SourceType = "user",
                ConfirmationStatus = "confirmed"
            });
        await dbContext.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
        var service = new CareerOrchestratorService(
            dbContext,
            new UserAgentContextSnapshotService(dbContext, clock),
            clock);
        var review = await service.ReviewResumeMailboxFitAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(clock.UtcNow, review.GeneratedAtUtc);
        Assert.Equal("career_orchestrator.resume_mailbox_review", review.RuntimePath);
        Assert.Equal("deterministic.shared_resume_mailbox_context.v1", review.ModelPath);
        Assert.Equal(84, review.Evidence.LatestResumeScore);
        Assert.Equal(resumeVersion.Id, review.Evidence.ResumeVersionLinkedToOutcome);
        Assert.Contains(review.Findings, finding => finding.Contains("interview", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Findings, finding => finding.Contains("Top mailbox role pattern", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Findings, finding => finding.Contains("Roblox", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Findings, finding => finding.Contains("confirmed long-term memory", StringComparison.OrdinalIgnoreCase));
        var memory = Assert.Single(
            dbContext.UserMemoryEntries.Where(entry => entry.Key == "orchestrator.resume_mailbox_bridge"));
        Assert.Equal("orchestrator", memory.Kind);
        Assert.Equal("career_orchestrator", memory.SourceType);
        var stored = JsonSerializer.Deserialize<ResumeMailboxReviewDto>(
            memory.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, stored!.Evidence.InterviewOrOfferApplications);
    }

    [Fact]
    public async Task ReviewResumeMailboxFitAsync_UsesLangGraphAndKeepsWeakReviewPending()
    {
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var clock = new TestClock(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        var graphClient = new StubCareerAgentGraphClient(
            request => CareerAgentGraphRunOutcome.Succeeded(
                new CareerAgentGraphReviewResponse(
                    request.ThreadId,
                    "review_required",
                    "langgraph.resume_mailbox_review.v1",
                    "ollama/gemma4:31b-cloud",
                    "More evidence is needed before changing strategy.",
                    ["No resume or application evidence is available."],
                    ["Import a resume and sync Gmail."],
                    true,
                    0.52m,
                    [
                        new CareerAgentGraphTraceStep(
                            "evidence_gate",
                            "deterministic_evidence_gate",
                            "Human confirmation is required.",
                            "resumePresent=false",
                            [])
                    ])));
        var service = new CareerOrchestratorService(
            dbContext,
            new UserAgentContextSnapshotService(dbContext, clock),
            new ResumeMailboxContextBuilder(dbContext, clock),
            graphClient,
            clock);

        var review = await service.ReviewResumeMailboxFitAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal("langgraph.resume_mailbox_review.v1", review.RuntimePath);
        Assert.Equal("review_required", review.Status);
        Assert.True(review.RequiresHumanReview);
        Assert.Equal(0.52m, review.Confidence);
        Assert.Single(review.Trace!.Steps);
        Assert.NotNull(graphClient.Request);
        Assert.Empty(graphClient.Request.Applications);
        var memory = Assert.Single(
            dbContext.UserMemoryEntries.Where(entry => entry.Key == "orchestrator.resume_mailbox_bridge"));
        Assert.Equal("career_orchestrator_langgraph", memory.SourceType);
        Assert.Equal("pending", memory.ConfirmationStatus);
    }

    [Fact]
    public async Task ReviewResumeMailboxFitAsync_ExposesDeterministicFallbackWhenGraphFails()
    {
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var clock = new TestClock(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero));
        var graphClient = new StubCareerAgentGraphClient(
            _ => CareerAgentGraphRunOutcome.Failed("Agent runtime returned HTTP 503."));
        var service = new CareerOrchestratorService(
            dbContext,
            new UserAgentContextSnapshotService(dbContext, clock),
            new ResumeMailboxContextBuilder(dbContext, clock),
            graphClient,
            clock);

        var review = await service.ReviewResumeMailboxFitAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal("career_orchestrator.resume_mailbox_review.fallback", review.RuntimePath);
        Assert.Equal("completed_with_fallback", review.Status);
        Assert.Contains("agent-runtime-fallback", review.ModelPath, StringComparison.Ordinal);
        Assert.Equal("deterministic fallback", review.Trace!.Summary!.ReviewGateResult);
    }

    [PostgresFact]
    public async Task ReviewResumeMailboxFitAsync_StoresAFullLangGraphTraceInPostgres()
    {
        // Regression: a real multi-agent trace exceeded the old 2000-character memory column.
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        dbContext.Users.Add(new User { Email = ApplicationDefaults.DefaultUserEmail, Name = "Alex Rivera" });
        await dbContext.SaveChangesAsync();
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var steps = Enumerable.Range(1, 6)
            .Select(
                index => new CareerAgentGraphTraceStep(
                    "parallel_analysis",
                    $"specialist_{index}",
                    new string('s', 400),
                    new string('r', 200),
                    Enumerable.Range(1, 6).Select(fact => new CareerAgentGraphTraceFact("Evidence", new string('e', 120))).ToList()))
            .ToList();
        var graphClient = new StubCareerAgentGraphClient(
            request => CareerAgentGraphRunOutcome.Succeeded(
                new CareerAgentGraphReviewResponse(
                    request.ThreadId,
                    "completed",
                    "langgraph.resume_mailbox_review.v1",
                    "deterministic/evidence-v1",
                    "Three specialists reviewed the evidence.",
                    ["Finding"],
                    ["Action"],
                    false,
                    0.8m,
                    steps)));
        var service = new CareerOrchestratorService(
            dbContext,
            new UserAgentContextSnapshotService(dbContext, clock),
            new ResumeMailboxContextBuilder(dbContext, clock),
            graphClient,
            clock);

        await service.ReviewResumeMailboxFitAsync(ApplicationDefaults.DefaultUserEmail);

        var stored = Assert.Single(dbContext.UserMemoryEntries.Where(entry => entry.Kind == "orchestrator"));
        Assert.True(stored.Value.Length > 2000);
        Assert.Contains("specialist_6", stored.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("career_orchestrator_langgraph", "review_required", "confirmed", true)]
    [InlineData("career_orchestrator_langgraph", "review_required", "rejected", false)]
    [InlineData("career_orchestrator_langgraph", "completed", "confirmed", null)]
    [InlineData("career_orchestrator", "review_required", "confirmed", null)]
    public async Task ApplyReviewDecisionAsync_ResumesOnlyPausedLangGraphReviews(
        string sourceType,
        string reviewStatus,
        string confirmationStatus,
        bool? expectedApproval)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var graphClient = new StubCareerAgentGraphClient(_ => CareerAgentGraphRunOutcome.Skipped());
        var service = CreateService(dbContext, graphClient, clock);
        var entry = new UserMemoryEntry
        {
            UserId = Guid.NewGuid(),
            Kind = "orchestrator",
            Key = "orchestrator.resume_mailbox_bridge",
            Value = $$"""{"threadId":"review:paused-001","status":"{{reviewStatus}}"}""",
            SourceType = sourceType,
            ConfirmationStatus = confirmationStatus
        };
        dbContext.UserMemoryEntries.Add(entry);
        await dbContext.SaveChangesAsync();
        var resolved = new UserMemoryEntryDto(
            entry.Id,
            entry.Kind,
            entry.Key,
            entry.Value,
            null,
            sourceType,
            null,
            0.5,
            confirmationStatus,
            clock.UtcNow,
            null);

        await service.ApplyReviewDecisionAsync(resolved);

        Assert.Equal(
            expectedApproval is null ? null : ("review:paused-001", expectedApproval.Value),
            graphClient.Decision);
    }

    [Fact]
    public async Task ApprovingAReviewCreatesItsRemindersAndOnlyThenReplacesTheConfirmedReview()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "alex@demo.careeroc.example", Name = "Alex Rivera" };
        var other = new User { Email = "jordan@example.com", Name = "Jordan Lee" };
        var interview = new JobApplication
        {
            User = user,
            Company = "Kestrel AI",
            Role = "Backend Engineer",
            Status = ApplicationStatus.Interviewing,
            LastActivityAtUtc = clock.UtcNow.AddDays(-2)
        };
        var foreign = new JobApplication { User = other, Company = "Harbor Labs", Role = "Engineer" };
        dbContext.AddRange(user, other, interview, foreign);
        dbContext.UserMemoryEntries.Add(
            new UserMemoryEntry
            {
                User = user,
                Kind = "orchestrator",
                Key = "orchestrator.resume_mailbox_bridge",
                Value = """{"summary":"Earlier review"}""",
                Summary = "Earlier review",
                SourceType = "career_orchestrator_langgraph",
                ConfirmationStatus = "confirmed"
            });
        await dbContext.SaveChangesAsync();
        var graphClient = new StubCareerAgentGraphClient(
            request => CareerAgentGraphRunOutcome.Succeeded(
                new CareerAgentGraphReviewResponse(
                    request.ThreadId,
                    "review_required",
                    "langgraph.resume_mailbox_review.v1",
                    "deterministic/evidence-v1",
                    "One reminder proposed.",
                    [],
                    [],
                    true,
                    0.8m,
                    [],
                    [
                        new CareerAgentGraphReminderProposal(interview.Id.ToString(), "Prepare for the next Kestrel AI step", 2),
                        new CareerAgentGraphReminderProposal(foreign.Id.ToString(), "Not this user's application", 1)
                    ])));
        var service = CreateService(dbContext, graphClient, clock);

        var review = await service.ReviewResumeMailboxFitAsync(user.Email);

        Assert.Equal(interview.Id, Assert.Single(review.ProposedReminders!).ApplicationId);
        Assert.True(Assert.Single(graphClient.Request!.Applications).Id == interview.Id);
        Assert.Equal("Earlier review", dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "confirmed").Summary);
        Assert.Empty(dbContext.Reminders);

        var pending = dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "pending");
        var resolved = await new UserMemoryProposalService(dbContext, clock).ResolveProposalAsync(
            pending.Id,
            new ResolveUserMemoryProposalRequest("confirm"),
            user.Email);
        var decided = await service.ApplyReviewDecisionAsync(resolved!);

        var reminder = Assert.Single(dbContext.Reminders);
        Assert.Equal(interview.Id, reminder.JobApplicationId);
        Assert.Equal(clock.UtcNow.AddDays(2), reminder.DueAtUtc);
        var stored = JsonSerializer.Deserialize<ResumeMailboxReviewDto>(
            decided.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("completed", stored!.Status);
        Assert.False(stored.RequiresHumanReview);
        Assert.Equal(1, stored.RemindersCreated);
        Assert.Equal("One reminder proposed.", dbContext.UserMemoryEntries.Single(entry => entry.ConfirmationStatus == "confirmed").Summary);
        Assert.Equal((review.ThreadId!, true), graphClient.Decision);
    }

    private static CareerOrchestratorService CreateService(
        Careeroc.Infrastructure.Persistence.CareerocDbContext dbContext,
        ICareerAgentGraphClient graphClient,
        TestClock clock)
    {
        return new CareerOrchestratorService(
            dbContext,
            new UserAgentContextSnapshotService(dbContext, clock),
            new ResumeMailboxContextBuilder(dbContext, clock),
            graphClient,
            clock);
    }

    private sealed class StubCareerAgentGraphClient : ICareerAgentGraphClient
    {
        private readonly Func<CareerAgentGraphReviewRequest, CareerAgentGraphRunOutcome> _run;

        public StubCareerAgentGraphClient(
            Func<CareerAgentGraphReviewRequest, CareerAgentGraphRunOutcome> run)
        {
            _run = run;
        }

        public CareerAgentGraphReviewRequest? Request { get; private set; }

        public Task<CareerAgentGraphRunOutcome> ReviewResumeMailboxAsync(
            CareerAgentGraphReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(_run(request));
        }

        public (string ThreadId, bool Approved)? Decision { get; private set; }

        public Task<CareerAgentGraphRunOutcome> DecideReviewAsync(
            string threadId,
            bool approved,
            CancellationToken cancellationToken = default)
        {
            Decision = (threadId, approved);
            return Task.FromResult(CareerAgentGraphRunOutcome.Skipped());
        }
    }
}
