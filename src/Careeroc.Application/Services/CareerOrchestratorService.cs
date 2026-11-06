using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Agents;
using Careeroc.Contracts.Orchestrator;
using Careeroc.Contracts.Profile;
using Careeroc.Contracts.Review;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class CareerOrchestratorService
{
    private const string ReviewMemoryKind = "orchestrator";
    private const string ReviewMemoryKey = "orchestrator.resume_mailbox_bridge";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICareerocDbContext _dbContext;
    private readonly IUserAgentContextSnapshotProvider _agentContextSnapshotProvider;
    private readonly ResumeMailboxContextBuilder _resumeMailboxContextBuilder;
    private readonly ICareerAgentGraphClient _agentGraphClient;
    private readonly IClock _clock;

    public CareerOrchestratorService(
        ICareerocDbContext dbContext,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ResumeMailboxContextBuilder resumeMailboxContextBuilder,
        ICareerAgentGraphClient agentGraphClient,
        IClock clock)
    {
        _dbContext = dbContext;
        _agentContextSnapshotProvider = agentContextSnapshotProvider;
        _resumeMailboxContextBuilder = resumeMailboxContextBuilder;
        _agentGraphClient = agentGraphClient;
        _clock = clock;
    }

    public async Task<ResumeMailboxReviewDto> ReviewResumeMailboxFitAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await UserAccounts.GetOrCreateAsync(_dbContext, userEmail, null, cancellationToken);
        var mailboxSnapshot = await _resumeMailboxContextBuilder.BuildForUserAsync(user.Id, cancellationToken);
        var withOpenReminders = await LoadApplicationsWithOpenRemindersAsync(user.Id, cancellationToken);

        var agentContext = await _agentContextSnapshotProvider.GetSnapshotAsync(
            user.Email,
            cancellationToken);
        var baselineReview = BuildReview(mailboxSnapshot, agentContext, _clock.UtcNow);
        var graphOutcome = await _agentGraphClient.ReviewResumeMailboxAsync(
            BuildGraphRequest(mailboxSnapshot, withOpenReminders, baselineReview),
            cancellationToken);
        var review = ApplyGraphOutcome(baselineReview, graphOutcome, mailboxSnapshot.Applications);
        var releasedThreads = await UpsertReviewMemoryAsync(user.Id, review, cancellationToken);
        await _dbContext.SaveOrAskToRetryAsync(
            "Another review finished at the same time. Reload to see it.",
            cancellationToken);
        // A superseded or overwritten review will never be decided, so its paused run is rejected
        // to free its checkpoints; best effort, since the review is already settled here.
        // Not the request's token: a closed tab after the save must not leave a run paused for good.
        foreach (var threadId in releasedThreads)
        {
            await _agentGraphClient.DecideReviewAsync(threadId, approved: false, CancellationToken.None);
        }

        return review;
    }

    // Applies the user's decision on a paused review. Agents only propose reminders; the API creates
    // the approved ones itself, after checking each application belongs to the user. The paused graph
    // run is then resumed so its trace ends with the decision, which is best effort: the decision
    // already stands in careeroc.
    public async Task<UserMemoryEntryDto> ApplyReviewDecisionAsync(
        UserMemoryEntryDto resolvedEntry,
        CancellationToken cancellationToken = default)
    {
        if (resolvedEntry is not
            {
                Kind: ReviewMemoryKind,
                Key: ReviewMemoryKey,
                SourceType: "career_orchestrator_langgraph",
                ConfirmationStatus: "confirmed" or "rejected"
            })
        {
            return resolvedEntry;
        }

        ResumeMailboxReviewDto? review;
        try
        {
            review = JsonSerializer.Deserialize<ResumeMailboxReviewDto>(resolvedEntry.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return resolvedEntry;
        }

        if (review is not { Status: "review_required" })
        {
            return resolvedEntry;
        }

        // The decision is already saved, so its reminders and status are saved before the runtime
        // call and without the request's token: a slow runtime or a closed tab must not lose them.
        var entry = await _dbContext.UserMemoryEntries
            .FirstAsync(candidate => candidate.Id == resolvedEntry.Id, CancellationToken.None);
        var approved = resolvedEntry.ConfirmationStatus == "confirmed";
        int? remindersCreated = approved
            ? await CreateApprovedRemindersAsync(entry.UserId, review.ProposedReminders ?? [], CancellationToken.None)
            : null;
        var decided = review with
        {
            Status = approved ? "completed" : "rejected",
            RequiresHumanReview = false,
            RemindersCreated = remindersCreated
        };
        entry.Value = SerializeReview(decided);
        entry.UpdatedAtUtc = _clock.UtcNow;
        try
        {
            await _dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // A newer review replaced this entry between the confirmation and this save, so these
            // reminders were not created; the paused run is released, and the person reviews the
            // newer one. Rare: two reviews have to finish around the same approval.
            if (review.ThreadId is { Length: > 0 } replacedThread)
            {
                await _agentGraphClient.DecideReviewAsync(replacedThread, approved: false, CancellationToken.None);
            }

            throw new UserFacingException(
                "A newer review replaced this one while your decision was saving, so its reminders were not created. Reload to see the newer review.",
                exception);
        }

        var outcome = review.ThreadId is { Length: > 0 } threadId
            ? await _agentGraphClient.DecideReviewAsync(threadId, approved, cancellationToken)
            : CareerAgentGraphRunOutcome.Skipped();
        if (outcome.Response is { } resumed)
        {
            entry.Value = SerializeReview(decided with { Trace = BuildGraphTrace(resumed) });
            entry.UpdatedAtUtc = _clock.UtcNow;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The resumed trace is display detail; the decision itself is already saved.
                _dbContext.ChangeTracker.DiscardPendingChanges();
            }
        }

        return resolvedEntry with { Value = entry.Value, UpdatedAtUtc = entry.UpdatedAtUtc };
    }

    private async Task<int> CreateApprovedRemindersAsync(
        Guid userId,
        IReadOnlyList<ProposedReminderDto> proposals,
        CancellationToken cancellationToken)
    {
        var applicationIds = proposals.Select(proposal => proposal.ApplicationId).ToList();
        var applications = await _dbContext.JobApplications
            .Where(application => application.UserId == userId && applicationIds.Contains(application.Id))
            .ToListAsync(cancellationToken);
        // The same rule the gate applied when it proposed them: a reminder that opened since then
        // must not be retitled by the agent's proposal.
        var withOpenReminder = await LoadApplicationsWithOpenRemindersAsync(userId, cancellationToken);
        var workflow = new ApplicationWorkflowService(_dbContext, _clock);
        var created = 0;
        foreach (var proposal in proposals)
        {
            var application = applications.FirstOrDefault(candidate => candidate.Id == proposal.ApplicationId);
            if (application is null ||
                ApplicationWorkflowService.IsTerminalStatus(application.Status) ||
                withOpenReminder.Contains(application.Id))
            {
                continue;
            }

            var plan = new FollowUpTaskPlan(
                true,
                ReminderType.FollowUp,
                proposal.Title,
                _clock.UtcNow.AddDays(Math.Clamp(proposal.DueInDays, 1, 30)),
                "Approved from a career orchestrator review.",
                "career_supervisor");
            if (await workflow.EnsureReminderAsync(application, plan, cancellationToken))
            {
                created++;
            }
        }

        return created;
    }

    private async Task<IReadOnlySet<Guid>> LoadApplicationsWithOpenRemindersAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var applicationIds = await _dbContext.Reminders
            .Where(reminder => reminder.Status == ReminderStatus.Open && reminder.JobApplication.UserId == userId)
            .Select(reminder => reminder.JobApplicationId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return applicationIds.ToHashSet();
    }

    // Read-only: the bounded snapshot the internal graph receives, for an external A2A caller.
    public async Task<CareerAgentGraphReviewRequest?> BuildReviewRequestAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var email = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var mailboxSnapshot = await _resumeMailboxContextBuilder.BuildForUserAsync(user.Id, cancellationToken);
        var withOpenReminders = await LoadApplicationsWithOpenRemindersAsync(user.Id, cancellationToken);
        var agentContext = await _agentContextSnapshotProvider.GetSnapshotAsync(user.Email, cancellationToken);
        return BuildGraphRequest(
            mailboxSnapshot,
            withOpenReminders,
            BuildReview(mailboxSnapshot, agentContext, _clock.UtcNow));
    }

    private CareerAgentGraphReviewRequest BuildGraphRequest(
        ResumeMailboxContextSnapshot mailboxSnapshot,
        IReadOnlySet<Guid> withOpenReminders,
        ResumeMailboxReviewDto baselineReview)
    {
        var latestResume = mailboxSnapshot.ResumeVersions
            .OrderByDescending(version => version.CreatedAtUtc)
            .FirstOrDefault();
        var resumeEvidence = latestResume is null
            ? null
            : new CareerAgentGraphResumeEvidence(
                latestResume.ResumeDocument.Title,
                latestResume.Summary,
                latestResume.Evaluation?.Score ?? 0,
                DeserializeStringList(latestResume.SkillsJson),
                DeserializeStringList(latestResume.ProjectHighlightsJson));
        var context = mailboxSnapshot.Context;

        return new CareerAgentGraphReviewRequest(
            $"resume-mailbox:{Guid.NewGuid():N}",
            _clock.UtcNow,
            baselineReview.Summary,
            baselineReview.Findings,
            baselineReview.NextActions,
            new CareerAgentGraphEvidence(
                baselineReview.Evidence.ResumeVersions,
                baselineReview.Evidence.LatestResumeScore,
                baselineReview.Evidence.TotalApplications,
                baselineReview.Evidence.InterviewOrOfferApplications,
                baselineReview.Evidence.RejectedApplications,
                baselineReview.Evidence.ResumeVersionLinkedToOutcome),
            resumeEvidence,
            mailboxSnapshot.Applications
                .OrderByDescending(application => application.LastActivityAtUtc)
                .Take(20)
                .Select(
                    application => new CareerAgentGraphApplicationEvidence(
                        application.Id,
                        application.Company,
                        application.Role,
                        application.Status.ToString(),
                        application.LastActivityAtUtc,
                        withOpenReminders.Contains(application.Id)))
                .ToList(),
            context.TopTargetRoles,
            context.CompaniesWithPositiveOutcomes,
            context.RepeatedRejectedRoleFamilies,
            context.LatestActionSignals,
            mailboxSnapshot.ConfirmedMemoryFacts.Take(6).ToList());
    }

    private static ResumeMailboxReviewDto ApplyGraphOutcome(
        ResumeMailboxReviewDto baselineReview,
        CareerAgentGraphRunOutcome outcome,
        IReadOnlyList<JobApplication> applications)
    {
        if (outcome.Response is { } graphReview)
        {
            return baselineReview with
            {
                RuntimePath = graphReview.RuntimePath,
                ModelPath = graphReview.ModelPath,
                Summary = graphReview.Summary,
                Findings = graphReview.Findings.Take(8).ToList(),
                NextActions = graphReview.NextActions.Take(4).ToList(),
                ThreadId = graphReview.ThreadId,
                Status = graphReview.Status,
                RequiresHumanReview = graphReview.RequiresHumanReview,
                Confidence = graphReview.Confidence,
                Trace = BuildGraphTrace(graphReview),
                ProposedReminders = MapProposedReminders(graphReview.ProposedReminders, applications)
            };
        }

        if (!outcome.Attempted)
        {
            return baselineReview;
        }

        return baselineReview with
        {
            RuntimePath = "career_orchestrator.resume_mailbox_review.fallback",
            ModelPath = "deterministic.shared_resume_mailbox_context.v1; agent-runtime-fallback",
            Status = "completed_with_fallback",
            Confidence = 0.86m,
            Trace = new ReviewAgentTraceDto(
                baselineReview.GeneratedAtUtc,
                [
                    new ReviewAgentTraceStepDto(
                        "fallback",
                        "deterministic_career_orchestrator",
                        "The LangGraph runtime was unavailable, so the deterministic review was used.",
                        outcome.FailureReason,
                        [])
                ],
                new AgentTraceSummaryDto(
                    baselineReview.ModelPath,
                    "resume_mailbox_review",
                    0.86m,
                    "bounded resume and mailbox context",
                    "deterministic fallback",
                    outcome.FailureReason ?? "agent runtime unavailable"))
        };
    }

    private static IReadOnlyList<ProposedReminderDto> MapProposedReminders(
        IReadOnlyList<CareerAgentGraphReminderProposal>? proposals,
        IReadOnlyList<JobApplication> applications)
    {
        var byId = applications.ToDictionary(application => application.Id);
        return (proposals ?? [])
            .Select(
                proposal => Guid.TryParse(proposal.ApplicationId, out var id) &&
                            byId.TryGetValue(id, out var application) &&
                            !string.IsNullOrWhiteSpace(proposal.Title)
                    ? new ProposedReminderDto(
                        id,
                        application.Company,
                        application.Role,
                        proposal.Title.Trim(),
                        Math.Clamp(proposal.DueInDays, 1, 30))
                    : null)
            .OfType<ProposedReminderDto>()
            .DistinctBy(proposal => proposal.ApplicationId)
            .Take(3)
            .ToList();
    }

    private static ReviewAgentTraceDto BuildGraphTrace(CareerAgentGraphReviewResponse graphReview)
    {
        return new ReviewAgentTraceDto(
            null,
            graphReview.Trace
                .Select(
                    step => new ReviewAgentTraceStepDto(
                        step.Stage,
                        step.AgentName,
                        step.Summary,
                        step.Reason,
                        step.Facts
                            .Select(fact => new ReviewAgentTraceFactDto(fact.Label, fact.Value))
                            .ToList()))
                .ToList(),
            new AgentTraceSummaryDto(
                graphReview.ModelPath,
                "resume_mailbox_review",
                graphReview.Confidence,
                "bounded resume and mailbox context",
                graphReview.RequiresHumanReview ? "human review required" : "evidence gate passed",
                "none"));
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Take(40)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ResumeMailboxReviewDto BuildReview(
        ResumeMailboxContextSnapshot mailboxSnapshot,
        UserAgentContextSnapshotDto agentContext,
        DateTimeOffset generatedAtUtc)
    {
        var resumeVersions = mailboxSnapshot.ResumeVersions
            .OrderByDescending(version => version.CreatedAtUtc)
            .ToList();
        var applications = mailboxSnapshot.Applications;
        var mailboxContext = mailboxSnapshot.Context;
        var latestResume = resumeVersions.FirstOrDefault();
        var latestScore = latestResume?.Evaluation?.Score ?? 0;
        var totalApplications = applications.Count;
        var interviewOrOfferApplications = applications.Count(application =>
            application.Status == ApplicationStatus.Interviewing ||
            application.Status == ApplicationStatus.Offer);
        var rejectedApplications = applications.Count(application => application.Status == ApplicationStatus.Rejected);
        var outcomeResumeVersion = mailboxContext.ResumeVersionLikelyUsedBeforePositiveOutcomes;
        var findings = new List<string>();
        var nextActions = new List<string>();

        if (latestResume is null)
        {
            findings.Add("No resume version is available for mailbox outcome comparison.");
            nextActions.Add("Import or paste a resume before trusting outreach strategy recommendations.");
        }
        else
        {
            findings.Add($"Latest resume score is {latestScore}/100 from {latestResume.ResumeDocument.Title}.");
        }

        if (mailboxContext.TopTargetRoles.Count > 0)
        {
            findings.Add($"Top mailbox role pattern is {mailboxContext.TopTargetRoles[0]}.");
        }

        if (agentContext.MemoryEntryCount > 0)
        {
            findings.Add($"Orchestrator context includes {agentContext.MemoryEntryCount} confirmed long-term memory item{(agentContext.MemoryEntryCount == 1 ? "" : "s")}.");
        }
        else
        {
            nextActions.Add("Confirm durable user memory so the orchestrator can personalize resume and mailbox strategy.");
        }

        if (totalApplications == 0)
        {
            findings.Add("No application history is available yet.");
            nextActions.Add("Backfill Gmail or add applications so resume changes can be compared against outcomes.");
        }
        else if (interviewOrOfferApplications > 0)
        {
            findings.Add($"{interviewOrOfferApplications} application outcome has interview or offer signal.");
            if (outcomeResumeVersion is not null)
            {
                findings.Add($"Best available resume-outcome link points to version {outcomeResumeVersion.Value:N}.");
            }

            if (mailboxContext.CompaniesWithPositiveOutcomes.Count > 0)
            {
                findings.Add($"Positive outcome context includes {mailboxContext.CompaniesWithPositiveOutcomes[0]}.");
            }
        }
        else if (latestScore < 70)
        {
            findings.Add("Applications exist but no interview or offer signal is visible, and the latest resume score is below 70.");
            nextActions.Add("Improve resume evidence density before increasing application volume.");
        }
        else
        {
            findings.Add("Applications exist but no interview or offer signal is visible yet.");
            nextActions.Add("Compare company and role targeting before assuming the resume is the main blocker.");
        }

        if (rejectedApplications >= 5 && interviewOrOfferApplications == 0)
        {
            findings.Add($"{rejectedApplications} rejected applications with no high-signal outcomes suggest a targeting or resume mismatch.");
            nextActions.Add("Review rejected roles against resume skills and rewrite the top project for the target role.");
        }

        if (mailboxContext.RepeatedRejectedRoleFamilies.Count > 0)
        {
            findings.Add($"Repeated rejection pattern: {mailboxContext.RepeatedRejectedRoleFamilies[0]}.");
            nextActions.Add("Use repeated rejection patterns as role-fit evidence before creating the next targeted resume version.");
        }

        if (latestScore >= 80 && interviewOrOfferApplications > 0)
        {
            findings.Add("Resume and mailbox outcomes are aligned enough for targeted versioning.");
            nextActions.Add("Create a role-specific resume version for the companies generating replies.");
        }

        if (nextActions.Count == 0)
        {
            nextActions.Add("Keep collecting mailbox outcomes and rerun the orchestrator after new interviews or rejections.");
        }

        var summary = BuildSummary(latestScore, totalApplications, interviewOrOfferApplications);
        return new ResumeMailboxReviewDto(
            generatedAtUtc,
            "career_orchestrator.resume_mailbox_review",
            "deterministic.shared_resume_mailbox_context.v1",
            summary,
            findings.Take(8).ToList(),
            nextActions.Take(4).ToList(),
            new ResumeMailboxReviewEvidenceDto(
                resumeVersions.Count,
                latestScore,
                totalApplications,
                interviewOrOfferApplications,
                rejectedApplications,
                outcomeResumeVersion));
    }

    private static string BuildSummary(int latestScore, int totalApplications, int interviewOrOfferApplications)
    {
        if (totalApplications == 0)
        {
            return "Career orchestrator needs application history before comparing resume quality to mailbox outcomes.";
        }

        if (interviewOrOfferApplications > 0 && latestScore >= 75)
        {
            return "Resume evidence and mailbox outcomes are beginning to align.";
        }

        if (interviewOrOfferApplications == 0)
        {
            return "No high-signal mailbox outcomes are visible yet; resume and targeting both need review.";
        }

        return "Mailbox outcomes exist, but the latest resume still needs stronger evidence before scaling outreach.";
    }

    // Returns the runtime threads of the reviews this replaces, so their paused runs can be released.
    private async Task<IReadOnlyList<string>> UpsertReviewMemoryAsync(
        Guid userId,
        ResumeMailboxReviewDto review,
        CancellationToken cancellationToken)
    {
        var releasedThreads = new List<string>();
        var nowUtc = _clock.UtcNow;
        var value = SerializeReview(review);
        // The runtime allows a longer summary than this column; the full text stays in the value.
        var storedSummary = review.Summary.Length > UserMemoryEntry.MaxSummaryLength
            ? review.Summary[..UserMemoryEntry.MaxSummaryLength]
            : review.Summary;
        var usesLangGraph = review.RuntimePath.StartsWith("langgraph.", StringComparison.Ordinal);
        var sourceType = usesLangGraph ? "career_orchestrator_langgraph" : "career_orchestrator";
        var confidence = review.Confidence ?? 0.86m;
        var confirmationStatus = review.RequiresHumanReview ? "pending" : "confirmed";
        // A gated review is its own pending entry, so the last confirmed review stays in place until
        // the user decides; confirming it later replaces that review through the proposal flow.
        var entry = await _dbContext.UserMemoryEntries
            .Where(
                candidate =>
                    candidate.UserId == userId &&
                    candidate.Kind == ReviewMemoryKind &&
                    candidate.Key == ReviewMemoryKey &&
                    candidate.ConfirmationStatus == confirmationStatus)
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (confirmationStatus == "confirmed" && usesLangGraph)
        {
            // A newer agent review that passed the gate supersedes any review still waiting for a
            // decision; approving the old one from an open tab would copy it over this one. A
            // fallback review does not, so a runtime outage leaves a pending agent review in place.
            var superseded = await _dbContext.UserMemoryEntries
                .Where(
                    candidate =>
                        candidate.UserId == userId &&
                        candidate.Kind == ReviewMemoryKind &&
                        candidate.Key == ReviewMemoryKey &&
                        candidate.ConfirmationStatus == "pending")
                .ToListAsync(cancellationToken);
            foreach (var pending in superseded)
            {
                pending.ConfirmationStatus = "rejected";
                pending.UpdatedAtUtc = nowUtc;
                AddThread(releasedThreads, pending.Value);
            }
        }

        if (entry is null)
        {
            _dbContext.UserMemoryEntries.Add(
                new UserMemoryEntry
                {
                    UserId = userId,
                    Kind = ReviewMemoryKind,
                    Key = ReviewMemoryKey,
                    Value = value,
                    Summary = storedSummary,
                    SourceType = sourceType,
                    Confidence = (double)confidence,
                    ConfirmationStatus = confirmationStatus,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            return releasedThreads;
        }

        // A newer pending review overwrites the older one, whose paused run nobody can decide now.
        if (confirmationStatus == "pending")
        {
            AddThread(releasedThreads, entry.Value);
        }

        entry.Value = value;
        entry.Summary = storedSummary;
        entry.SourceType = sourceType;
        entry.Confidence = (double)confidence;
        entry.UpdatedAtUtc = nowUtc;
        return releasedThreads;
    }

    private static void AddThread(List<string> threads, string storedReview)
    {
        try
        {
            if (JsonSerializer.Deserialize<ResumeMailboxReviewDto>(storedReview, JsonOptions) is
                { Status: "review_required", ThreadId: { Length: > 0 } threadId })
            {
                threads.Add(threadId);
            }
        }
        catch (JsonException)
        {
            // A stored review that does not parse names no thread to release.
        }
    }

    private static string SerializeReview(ResumeMailboxReviewDto review)
    {
        var value = JsonSerializer.Serialize(review, JsonOptions);
        // The trace is display detail; the findings and decision state must still be stored.
        return value.Length > UserMemoryEntry.MaxStoredValueLength
            ? JsonSerializer.Serialize(review with { Trace = null }, JsonOptions)
            : value;
    }

}
