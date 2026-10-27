using System.Diagnostics;
using Careeroc.Application;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Careeroc.Application.Services;

public sealed class EmailIntelligenceService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IEmailProcessingQueue _emailProcessingQueue;
    private readonly InboxTriageAgentService _inboxTriageAgentService;
    private readonly WorkflowExtractionAgentService _workflowExtractionAgentService;
    private readonly RecruiterIdentityAgentService _recruiterIdentityAgentService;
    private readonly ScamCheckerAgentService _scamCheckerAgentService;
    private readonly ReviewEvidenceAgentService _reviewEvidenceAgentService;
    private readonly FollowUpTaskAgentService _followUpTaskAgentService;
    private readonly ApplicationWorkflowService _applicationWorkflowService;
    private readonly RecruitingContactGraphService _recruitingContactGraphService;
    private readonly IEmailAttachmentEvidenceImporter _emailAttachmentEvidenceImporter;
    private readonly IModelCallEventRecorder _modelCallEventRecorder;
    private readonly ILeaseManager _leaseManager;
    private readonly IClock _clock;
    private readonly ILogger<EmailIntelligenceService> _logger;

    internal EmailIntelligenceService(
        ICareerocDbContext dbContext,
        IEmailProcessingQueue emailProcessingQueue,
        IRecruitingEmailParser recruitingEmailParser,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock,
        ILogger<EmailIntelligenceService> logger,
        ILeaseManager? leaseManager = null)
        : this(
            dbContext,
            emailProcessingQueue,
            new InboxTriageAgentService(),
            new WorkflowExtractionAgentService(recruitingEmailParser),
            new RecruiterIdentityAgentService(),
            new ScamCheckerAgentService(),
            new ReviewEvidenceAgentService(),
            new FollowUpTaskAgentService(clock),
            applicationWorkflowService,
            new RecruitingContactGraphService(dbContext, clock),
            clock,
            logger,
            leaseManager: leaseManager)
    {
    }

    [ActivatorUtilitiesConstructor]
    public EmailIntelligenceService(
        ICareerocDbContext dbContext,
        IEmailProcessingQueue emailProcessingQueue,
        InboxTriageAgentService inboxTriageAgentService,
        WorkflowExtractionAgentService workflowExtractionAgentService,
        RecruiterIdentityAgentService recruiterIdentityAgentService,
        ScamCheckerAgentService scamCheckerAgentService,
        ReviewEvidenceAgentService reviewEvidenceAgentService,
        FollowUpTaskAgentService followUpTaskAgentService,
        ApplicationWorkflowService applicationWorkflowService,
        RecruitingContactGraphService recruitingContactGraphService,
        IClock clock,
        ILogger<EmailIntelligenceService> logger,
        IEmailAttachmentEvidenceImporter? emailAttachmentEvidenceImporter = null,
        IModelCallEventRecorder? modelCallEventRecorder = null,
        ILeaseManager? leaseManager = null)
    {
        _dbContext = dbContext;
        _emailProcessingQueue = emailProcessingQueue;
        _inboxTriageAgentService = inboxTriageAgentService;
        _workflowExtractionAgentService = workflowExtractionAgentService;
        _recruiterIdentityAgentService = recruiterIdentityAgentService;
        _scamCheckerAgentService = scamCheckerAgentService;
        _reviewEvidenceAgentService = reviewEvidenceAgentService;
        _followUpTaskAgentService = followUpTaskAgentService;
        _applicationWorkflowService = applicationWorkflowService;
        _recruitingContactGraphService = recruitingContactGraphService;
        _emailAttachmentEvidenceImporter = emailAttachmentEvidenceImporter ?? NoopEmailAttachmentEvidenceImporter.Instance;
        _modelCallEventRecorder = modelCallEventRecorder ?? NullModelCallEventRecorder.Instance;
        _leaseManager = leaseManager ?? NoopLeaseManager.Instance;
        _clock = clock;
        _logger = logger;
    }

    public async Task<EmailProcessingResult> ProcessPendingAsync(
        int batchSize = 25,
        CancellationToken cancellationToken = default)
    {
        return await ProcessPendingAsync(batchSize, null, cancellationToken);
    }

    public async Task<EmailProcessingResult> ProcessPendingAsync(
        int batchSize,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        using var activity = CareerocTelemetry.ActivitySource.StartActivity("email.process_pending");
        activity?.SetTag("email.batch_size", batchSize);
        activity?.SetTag("user.email", userEmail);

        var jobsProcessed = 0;
        var applicationsCreated = 0;
        var applicationsUpdated = 0;
        var remindersCreated = 0;

        for (var processedInBatch = 0; processedInBatch < batchSize; processedInBatch++)
        {
            var jobs = await _emailProcessingQueue.DequeuePendingBatchAsync(
                1,
                userEmail,
                cancellationToken);
            if (jobs.Count == 0)
            {
                break;
            }

            var job = jobs[0];
            try
            {
                using var correlationScope = CareerocTelemetry.BeginCorrelationScope(
                    job.CorrelationId,
                    job.RunId ?? job.InboxEmailId.ToString("N"));
                using var jobActivity = CareerocTelemetry.ActivitySource.StartActivity("email.process_job");
                CareerocTelemetry.TagCurrentActivity();
                jobActivity?.SetTag("email.job_id", job.Id.ToString());
                jobActivity?.SetTag("email.inbox_email_id", job.InboxEmailId.ToString());
                jobActivity?.SetTag("email.job_run_id", CareerocTelemetry.CurrentRunId);

                var inboxEmail = await _dbContext.InboxEmails
                    .Include(email => email.InboxAccount)
                    .ThenInclude(account => account.User)
                    .FirstOrDefaultAsync(email => email.Id == job.InboxEmailId, cancellationToken);

                if (inboxEmail is null)
                {
                    throw new InvalidOperationException($"Inbox email {job.InboxEmailId} was not found.");
                }

                // Two workers matching one user's emails at once could each create the same application.
                await using var userLease = await _leaseManager.TryAcquireAsync(
                    $"email-processing:{inboxEmail.InboxAccount.UserId:N}",
                    cancellationToken);
                if (userLease is null)
                {
                    await _emailProcessingQueue.DeferAsync(job.Id, TimeSpan.FromSeconds(15), cancellationToken, job.LockedAtUtc);
                    continue;
                }

                var triageDecision = _inboxTriageAgentService.Triage(inboxEmail);
                if (!triageDecision.IsRecruitingRelated &&
                    triageDecision.ShortcutRuleId is null &&
                    await FindThreadApplicationIdAsync(inboxEmail, ApplicationDefaults.UnknownCompany, cancellationToken) is not null)
                {
                    // Short replies ("see you Friday") rarely carry recruiting keywords, but their thread does.
                    triageDecision = new InboxTriageDecision(
                        true,
                        "Continues an email thread that is already linked to an application.",
                        "Inbox Triage Agent",
                        "Escalated a reply in a tracked recruiting thread to workflow extraction.");
                }

                if (!triageDecision.IsRecruitingRelated)
                {
                    jobActivity?.SetTag("email.triage", "non_recruiting");
                    if (!string.IsNullOrWhiteSpace(triageDecision.ShortcutRuleId))
                    {
                        await _modelCallEventRecorder.RecordAsync(
                            new ModelCallEventRecord(
                                _clock.UtcNow,
                                "deterministic",
                                triageDecision.ShortcutRuleId,
                                "triage_shortcut",
                                "filtered",
                                0,
                                inboxEmail.InboxAccount.User.Email,
                                inboxEmail.InboxAccountId,
                                inboxEmail.Id,
                                CareerocTelemetry.CurrentRunId,
                                CorrelationId: CareerocTelemetry.CurrentCorrelationId),
                            cancellationToken);
                    }

                    var processedAtUtc = _clock.UtcNow;
                    inboxEmail.JobApplicationId = null;
                    inboxEmail.Category = null;
                    inboxEmail.ActionRequired = false;
                    inboxEmail.DueDateUtc = null;
                    inboxEmail.ExtractedCompany = null;
                    inboxEmail.ExtractedRole = null;
                    inboxEmail.ClassificationConfidence = null;
                    inboxEmail.MatchingConfidence = null;
                    inboxEmail.RequiresManualReview = false;
                    inboxEmail.ReviewReason = null;
                    inboxEmail.ReviewedAtUtc = null;
                    inboxEmail.ProcessingNotes =
                        $"{EmailAnalysisVersion.Marker} [{triageDecision.AgentName}] {triageDecision.VisibleOutput} {triageDecision.RoutingReason}";
                    inboxEmail.ProcessingTraceJson = InboxEmailAgentTraceJson.Serialize(
                        BuildTriageOnlyTrace(triageDecision, processedAtUtc));
                    inboxEmail.ProcessedAtUtc = processedAtUtc;

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await _emailProcessingQueue.MarkCompletedAsync(job.Id, cancellationToken, job.LockedAtUtc);
                    jobsProcessed++;

                    _logger.LogInformation(
                        "Ignored email {EmailId} as non-recruiting inbox noise.",
                        inboxEmail.Id);
                    continue;
                }

                var parsed = NormalizeParsedEntities(
                    await _workflowExtractionAgentService.ExtractAsync(inboxEmail, cancellationToken));
                jobActivity?.SetTag("email.category", parsed.Category.ToString());
                jobActivity?.SetTag("email.parsing_source", parsed.ParsingSource);
                var recruiterIdentityDecision = _recruiterIdentityAgentService.Identify(inboxEmail, parsed);
                var scamRiskDecision = _scamCheckerAgentService.Check(inboxEmail, parsed);

                var threadApplicationId = inboxEmail.JobApplicationId is null && !scamRiskDecision.RequiresManualReview
                    ? await FindThreadApplicationIdAsync(inboxEmail, parsed.Company, cancellationToken)
                    : null;
                var match = scamRiskDecision.RequiresManualReview
                    ? new ApplicationMatchResult(
                        null,
                        Created: false,
                        MatchingConfidence: 0m,
                        ReviewReason: scamRiskDecision.Reason ?? "Scam checker blocked workflow automation.")
                    : await _applicationWorkflowService.MatchOrCreateApplicationAsync(
                        inboxEmail.InboxAccount.UserId,
                        inboxEmail.JobApplicationId ?? threadApplicationId,
                        parsed.Company,
                        parsed.Role,
                        parsed.RecommendedStatus,
                        parsed.Category,
                        inboxEmail.ReceivedAtUtc,
                        allowAutoCreate: !ShouldDeferApplicationCreation(parsed),
                        cancellationToken);

                if (threadApplicationId is not null && match.Application is not null)
                {
                    // The thread already names the employer and role; a terse reply should not reopen that question.
                    parsed = parsed with
                    {
                        Company = match.Application.Company,
                        Role = parsed.Role == ApplicationDefaults.UnknownRole ? match.Application.Role : parsed.Role
                    };
                }

                ApplicationStatus? resolvedStatus = null;
                FollowUpTaskPlan? followUpTaskPlan = null;
                var outboundReplyDetected = match.Application is not null &&
                    await HasOutboundReplyAsync(inboxEmail, cancellationToken);
                var effectiveParsed = outboundReplyDetected && parsed.ActionRequired
                    ? parsed with
                    {
                        ActionRequired = false,
                        DueDateUtc = null,
                        WorkflowNote = string.Join(
                            " ",
                            new[]
                            {
                                parsed.WorkflowNote,
                                "Detected a sent Gmail reply in this thread, so no reply reminder is needed."
                            }.Where(note => !string.IsNullOrWhiteSpace(note)))
                    }
                    : parsed;

                var reviewDecision = scamRiskDecision.RequiresManualReview
                    ? new ReviewEvidenceDecision(
                        true,
                        scamRiskDecision.Reason ?? "Scam checker blocked workflow automation.",
                        "Blocked workflow automation until the user reviews possible recruiting scam signals.",
                        "Review Evidence Agent")
                    : await _reviewEvidenceAgentService.EvaluateAsync(
                        effectiveParsed,
                        match,
                        ModelCallContext.FromInboxEmail(inboxEmail),
                        cancellationToken);
                var normalizedDueDateUtc = effectiveParsed.DueDateUtc.NormalizeUtc();
                var processedAtUtcForTrace = _clock.UtcNow;
                var acceptedApplication = reviewDecision.RequiresManualReview
                    ? null
                    : match.Application;

                if (reviewDecision.RequiresManualReview && match is { Created: true, Application: not null })
                {
                    _dbContext.JobApplications.Remove(match.Application);
                }

                if (acceptedApplication is not null)
                {
                    if (match.Created)
                    {
                        applicationsCreated++;
                    }
                    else
                    {
                        applicationsUpdated++;
                    }

                    if (outboundReplyDetected)
                    {
                        await LinkOutboundRepliesAsync(inboxEmail, acceptedApplication.Id, cancellationToken);
                    }

                    resolvedStatus = _applicationWorkflowService.ApplyStatusAndPriority(
                        acceptedApplication,
                        effectiveParsed.Category,
                        effectiveParsed.RecommendedStatus,
                        inboxEmail.ReceivedAtUtc);
                    await _applicationWorkflowService.DismissStaleRemindersAsync(
                        acceptedApplication,
                        cancellationToken);
                }

                inboxEmail.JobApplicationId = acceptedApplication?.Id;
                inboxEmail.Category = effectiveParsed.Category;
                inboxEmail.ContactName = recruiterIdentityDecision.ContactName;
                inboxEmail.ContactEmail = recruiterIdentityDecision.ContactEmail;
                inboxEmail.ContactRoleHint = recruiterIdentityDecision.RoleHint;
                inboxEmail.ContactType = recruiterIdentityDecision.ContactType;
                inboxEmail.ActionRequired = effectiveParsed.ActionRequired;
                inboxEmail.DueDateUtc = normalizedDueDateUtc;
                inboxEmail.ExtractedCompany = effectiveParsed.Company;
                inboxEmail.ExtractedRole = effectiveParsed.Role;
                inboxEmail.ClassificationConfidence = effectiveParsed.ClassificationConfidence;
                inboxEmail.MatchingConfidence = match.MatchingConfidence;
                inboxEmail.RequiresManualReview = reviewDecision.RequiresManualReview;
                inboxEmail.ReviewReason = reviewDecision.Reason;
                inboxEmail.ReviewedAtUtc = reviewDecision.RequiresManualReview ? null : inboxEmail.ReviewedAtUtc;
                inboxEmail.ProcessedAtUtc = processedAtUtcForTrace;

                if (acceptedApplication is not null)
                {
                    jobActivity?.SetTag("application.id", acceptedApplication.Id.ToString());
                    await _recruitingContactGraphService.UpsertFromEmailAsync(
                        inboxEmail,
                        recruiterIdentityDecision,
                        effectiveParsed,
                        cancellationToken);

                    await _applicationWorkflowService.UpsertEmailEventAsync(
                        acceptedApplication,
                        inboxEmail,
                        ApplicationWorkflowService.ToApiLabel(parsed.Category.ToString()),
                        inboxEmail.ReceivedAtUtc,
                        new
                        {
                            TriageAgent = triageDecision.AgentName,
                            TriageOutput = triageDecision.VisibleOutput,
                            triageDecision.RoutingReason,
                            effectiveParsed.Category,
                            effectiveParsed.Company,
                            effectiveParsed.Role,
                            DueDateUtc = normalizedDueDateUtc,
                            effectiveParsed.ClassificationConfidence,
                            effectiveParsed.ParsingSource,
                            effectiveParsed.ParsingModel,
                            recruiterIdentityDecision.ContactName,
                            recruiterIdentityDecision.ContactEmail,
                            recruiterIdentityDecision.RoleHint,
                            ContactType = recruiterIdentityDecision.ContactType?.ToString(),
                            recruiterIdentityDecision.Confidence,
                            ScamRiskLevel = scamRiskDecision.RiskLevel,
                            ScamRiskSignals = scamRiskDecision.Signals,
                            match.MatchingConfidence,
                            ReviewAgent = reviewDecision.AgentName,
                            ReviewOutput = reviewDecision.VisibleOutput,
                            reviewDecision.RequiresManualReview,
                            reviewDecision.Reason,
                            effectiveParsed.WorkflowNote,
                            OutboundReplyDetected = outboundReplyDetected
                        },
                        cancellationToken);
                }

                if (effectiveParsed.ActionRequired &&
                    acceptedApplication is not null &&
                    !ApplicationWorkflowService.IsTerminalStatus(acceptedApplication.Status))
                {
                    followUpTaskPlan = _followUpTaskAgentService.CreatePlan(
                        acceptedApplication,
                        effectiveParsed,
                        normalizedDueDateUtc);
                    var reminderCreated = await _applicationWorkflowService.EnsureReminderAsync(
                        acceptedApplication,
                        followUpTaskPlan,
                        cancellationToken);

                    if (reminderCreated)
                    {
                        remindersCreated++;
                    }
                }

                inboxEmail.ProcessingNotes = BuildProcessingNotes(
                    triageDecision,
                    effectiveParsed,
                    scamRiskDecision,
                    reviewDecision,
                    followUpTaskPlan);
                inboxEmail.ProcessingTraceJson = InboxEmailAgentTraceJson.Serialize(
                    BuildProcessingTrace(
                        triageDecision,
                        effectiveParsed,
                        recruiterIdentityDecision,
                        scamRiskDecision,
                        match,
                        reviewDecision,
                        followUpTaskPlan,
                        normalizedDueDateUtc,
                        resolvedStatus,
                        processedAtUtcForTrace));

                await _dbContext.SaveChangesAsync(cancellationToken);
                var importedAttachmentEvidence = 0;
                if (acceptedApplication is not null)
                {
                    importedAttachmentEvidence = await _emailAttachmentEvidenceImporter.ImportForEmailAsync(
                        inboxEmail.Id,
                        cancellationToken);
                }
                jobActivity?.SetTag("artifact.attachments_imported", importedAttachmentEvidence);

                await _emailProcessingQueue.MarkCompletedAsync(job.Id, cancellationToken, job.LockedAtUtc);
                jobsProcessed++;

                _logger.LogInformation(
                    "Processed email {EmailId} through {TriageAgent}, {ExtractionSource}, and {ReviewAgent}. Category: {Category}. Company: {Company}. Role: {Role}. Resolved application status: {Status}. Imported attachment evidence: {ImportedAttachmentEvidence}.",
                    inboxEmail.Id,
                    triageDecision.AgentName,
                    effectiveParsed.ParsingSource,
                    reviewDecision.AgentName,
                    effectiveParsed.Category,
                    effectiveParsed.Company,
                    effectiveParsed.Role,
                    resolvedStatus,
                    importedAttachmentEvidence);
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                // Drop the half-applied changes first; otherwise recording the failure would try to
                // save them again and throw inside this catch.
                _dbContext.ChangeTracker.DiscardPendingChanges();
                await _emailProcessingQueue.MarkFailedAsync(job.Id, exception.Message, cancellationToken, job.LockedAtUtc);
                _logger.LogError(exception, "Failed to process email job {JobId}.", job.Id);
            }
        }

        return new EmailProcessingResult(jobsProcessed, applicationsCreated, applicationsUpdated, remindersCreated);
    }

    public async Task<EmailProcessingResult> ProcessPendingUntilIdleAsync(
        int batchSize = 25,
        int maxBatches = 40,
        CancellationToken cancellationToken = default)
    {
        return await ProcessPendingUntilIdleAsync(
            batchSize,
            maxBatches,
            null,
            cancellationToken);
    }

    public async Task<EmailProcessingResult> ProcessPendingUntilIdleAsync(
        int batchSize,
        int maxBatches,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var totalJobsProcessed = 0;
        var totalApplicationsCreated = 0;
        var totalApplicationsUpdated = 0;
        var totalRemindersCreated = 0;

        for (var batchIndex = 0; batchIndex < Math.Max(1, maxBatches); batchIndex++)
        {
            var result = await ProcessPendingAsync(batchSize, userEmail, cancellationToken);
            totalJobsProcessed += result.JobsProcessed;
            totalApplicationsCreated += result.ApplicationsCreated;
            totalApplicationsUpdated += result.ApplicationsUpdated;
            totalRemindersCreated += result.RemindersCreated;

            if (result.JobsProcessed == 0 ||
                result.JobsProcessed < batchSize &&
                await _emailProcessingQueue.CountReadyAsync(userEmail, cancellationToken) == 0)
            {
                break;
            }
        }

        return new EmailProcessingResult(
            totalJobsProcessed,
            totalApplicationsCreated,
            totalApplicationsUpdated,
            totalRemindersCreated);
    }

    private static bool ShouldDeferApplicationCreation(ParsedRecruitingEmail parsed)
    {
        if (parsed.Category == EmailCategory.GenericUpdate)
        {
            return true;
        }

        return !ApplicationWorkflowService.CanCreateApplication(parsed.Company, parsed.Role);
    }

    private static ParsedRecruitingEmail NormalizeParsedEntities(ParsedRecruitingEmail parsed)
    {
        return CompanyNameNormalizer.IsUsableCompanyName(parsed.Company)
            ? parsed
            : parsed with { Company = ApplicationDefaults.UnknownCompany };
    }

    private async Task<Guid?> FindThreadApplicationIdAsync(
        InboxEmail inboxEmail,
        string parsedCompany,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inboxEmail.ThreadId))
        {
            return null;
        }

        var threadApplication = await _dbContext.InboxEmails
            .Where(
                email =>
                    email.InboxAccountId == inboxEmail.InboxAccountId &&
                    email.ThreadId == inboxEmail.ThreadId &&
                    email.Id != inboxEmail.Id &&
                    email.JobApplicationId != null)
            .OrderByDescending(email => email.ReceivedAtUtc)
            .Select(email => new { ApplicationId = email.JobApplicationId!.Value, email.JobApplication!.Company })
            .FirstOrDefaultAsync(cancellationToken);
        if (threadApplication is null)
        {
            return null;
        }

        return !CompanyNameNormalizer.IsUsableCompanyName(parsedCompany) ||
               ApplicationWorkflowService.IsSameCompanyIdentity(threadApplication.Company, parsedCompany)
            ? threadApplication.ApplicationId
            : null;
    }

    private async Task<bool> HasOutboundReplyAsync(
        InboxEmail inboxEmail,
        CancellationToken cancellationToken)
    {
        return await _dbContext.InboxEmails
            .AnyAsync(
                email =>
                    email.InboxAccountId == inboxEmail.InboxAccountId &&
                    email.ThreadId == inboxEmail.ThreadId &&
                    email.IsOutbound &&
                    email.ReceivedAtUtc >= inboxEmail.ReceivedAtUtc,
                cancellationToken);
    }

    private async Task<bool> LinkOutboundRepliesAsync(
        InboxEmail inboxEmail,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var outboundReplies = await _dbContext.InboxEmails
            .Where(
                email =>
                    email.InboxAccountId == inboxEmail.InboxAccountId &&
                    email.ThreadId == inboxEmail.ThreadId &&
                    email.IsOutbound &&
                    email.ReceivedAtUtc >= inboxEmail.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

        foreach (var outboundReply in outboundReplies)
        {
            outboundReply.JobApplicationId = applicationId;
        }

        return outboundReplies.Count > 0;
    }

    private static string BuildProcessingNotes(
        InboxTriageDecision triageDecision,
        ParsedRecruitingEmail parsed,
        ScamRiskDecision scamRiskDecision,
        ReviewEvidenceDecision reviewDecision,
        FollowUpTaskPlan? followUpTaskPlan)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(parsed.ParsingModel)
            ? $"Workflow Extraction Agent: {parsed.ParsingSource}"
            : $"Workflow Extraction Agent: {parsed.ParsingSource} ({parsed.ParsingModel})";
        var notes = new List<string>
        {
            EmailAnalysisVersion.Marker,
            $"[{sourceLabel}]",
            triageDecision.VisibleOutput,
            parsed.WorkflowNote,
            $"{scamRiskDecision.VisibleOutput} Risk: {scamRiskDecision.RiskLevel}.",
            $"{reviewDecision.AgentName}: {reviewDecision.VisibleOutput}"
        };

        if (followUpTaskPlan is not null)
        {
            notes.Add($"{followUpTaskPlan.AgentName}: {followUpTaskPlan.Reason}");
        }

        return string.Join(" ", notes.Where(note => !string.IsNullOrWhiteSpace(note)));
    }

    private static InboxEmailAgentTrace BuildTriageOnlyTrace(
        InboxTriageDecision triageDecision,
        DateTimeOffset generatedAtUtc)
    {
        return new InboxEmailAgentTrace(
            generatedAtUtc,
            [
                new AgentTraceStep(
                    "triage",
                    triageDecision.AgentName,
                    triageDecision.VisibleOutput,
                    triageDecision.RoutingReason,
                    [
                        new AgentTraceFact(
                            "Recruiting related",
                            triageDecision.IsRecruitingRelated ? "Yes" : "No")
                    ])
            ]);
    }

    private static InboxEmailAgentTrace BuildProcessingTrace(
        InboxTriageDecision triageDecision,
        ParsedRecruitingEmail parsed,
        RecruiterIdentityDecision recruiterIdentityDecision,
        ScamRiskDecision scamRiskDecision,
        ApplicationMatchResult match,
        ReviewEvidenceDecision reviewDecision,
        FollowUpTaskPlan? followUpTaskPlan,
        DateTimeOffset? normalizedDueDateUtc,
        ApplicationStatus? resolvedStatus,
        DateTimeOffset generatedAtUtc)
    {
        return new InboxEmailAgentTrace(
            generatedAtUtc,
            [
                new AgentTraceStep(
                    "triage",
                    triageDecision.AgentName,
                    triageDecision.VisibleOutput,
                    triageDecision.RoutingReason,
                    [
                        new AgentTraceFact(
                            "Recruiting related",
                            triageDecision.IsRecruitingRelated ? "Yes" : "No")
                    ]),
                new AgentTraceStep(
                    "workflow_extraction",
                    "Workflow Extraction Agent",
                    parsed.WorkflowNote,
                    null,
                    BuildExtractionFacts(parsed, normalizedDueDateUtc)),
                new AgentTraceStep(
                    "recruiter_identity",
                    recruiterIdentityDecision.AgentName,
                    recruiterIdentityDecision.VisibleOutput,
                    recruiterIdentityDecision.Reason,
                    BuildRecruiterIdentityFacts(recruiterIdentityDecision)),
                new AgentTraceStep(
                    "scam_checker",
                    "Scam Checker Agent",
                    scamRiskDecision.VisibleOutput,
                    scamRiskDecision.Reason,
                    BuildScamRiskFacts(scamRiskDecision)),
                new AgentTraceStep(
                    "application_match",
                    "Application Workflow Service",
                    BuildApplicationMatchSummary(match),
                    match.ReviewReason,
                    BuildApplicationMatchFacts(match, resolvedStatus)),
                new AgentTraceStep(
                    "review_evidence",
                    reviewDecision.AgentName,
                    reviewDecision.VisibleOutput,
                    reviewDecision.Reason,
                    [
                        new AgentTraceFact(
                            "Requires manual review",
                            reviewDecision.RequiresManualReview ? "Yes" : "No")
                    ]),
                new AgentTraceStep(
                    "follow_up_task",
                    followUpTaskPlan?.AgentName ?? "Follow-Up Task Agent",
                    BuildFollowUpSummary(parsed, match, followUpTaskPlan),
                    followUpTaskPlan?.Reason,
                    BuildFollowUpFacts(followUpTaskPlan, normalizedDueDateUtc))
            ]);
    }

    private static IReadOnlyList<AgentTraceFact> BuildExtractionFacts(
        ParsedRecruitingEmail parsed,
        DateTimeOffset? normalizedDueDateUtc)
    {
        var facts = new List<AgentTraceFact>
        {
            new("Category", parsed.Category.ToString()),
            new("Recommended status", parsed.RecommendedStatus.ToString()),
            new("Company", parsed.Company),
            new("Role", parsed.Role),
            new("Action required", parsed.ActionRequired ? "Yes" : "No"),
            new("Classification confidence", $"{parsed.ClassificationConfidence:0.00}"),
            new("Parsing source", parsed.ParsingSource)
        };

        if (normalizedDueDateUtc.HasValue)
        {
            facts.Add(new AgentTraceFact("Due date (UTC)", normalizedDueDateUtc.Value.ToString("O")));
        }

        if (!string.IsNullOrWhiteSpace(parsed.ParsingModel))
        {
            facts.Add(new AgentTraceFact("Parsing model", parsed.ParsingModel));
        }

        return facts;
    }

    private static IReadOnlyList<AgentTraceFact> BuildRecruiterIdentityFacts(
        RecruiterIdentityDecision recruiterIdentityDecision)
    {
        var facts = new List<AgentTraceFact>
        {
            new("Confidence", $"{recruiterIdentityDecision.Confidence:0.00}")
        };

        if (!string.IsNullOrWhiteSpace(recruiterIdentityDecision.ContactName))
        {
            facts.Add(new AgentTraceFact("Contact name", recruiterIdentityDecision.ContactName));
        }

        if (!string.IsNullOrWhiteSpace(recruiterIdentityDecision.ContactEmail))
        {
            facts.Add(new AgentTraceFact("Contact email", recruiterIdentityDecision.ContactEmail));
        }

        if (!string.IsNullOrWhiteSpace(recruiterIdentityDecision.RoleHint))
        {
            facts.Add(new AgentTraceFact("Role hint", recruiterIdentityDecision.RoleHint));
        }

        if (recruiterIdentityDecision.ContactType.HasValue)
        {
            facts.Add(new AgentTraceFact("Contact type", recruiterIdentityDecision.ContactType.Value.ToString()));
        }

        return facts;
    }

    private static IReadOnlyList<AgentTraceFact> BuildScamRiskFacts(ScamRiskDecision scamRiskDecision)
    {
        var facts = new List<AgentTraceFact>
        {
            new("Requires manual review", scamRiskDecision.RequiresManualReview ? "Yes" : "No"),
            new("Risk level", scamRiskDecision.RiskLevel),
            new("Confidence", $"{scamRiskDecision.Confidence:0.00}")
        };

        if (scamRiskDecision.Signals.Count > 0)
        {
            facts.Add(new AgentTraceFact("Signals", string.Join("; ", scamRiskDecision.Signals)));
        }

        return facts;
    }

    private static string BuildApplicationMatchSummary(ApplicationMatchResult match)
    {
        if (match.Application is null)
        {
            return "Did not attach this email to an application record.";
        }

        return match.Created
            ? "Created a new application record from this email."
            : "Matched this email to an existing application record.";
    }

    private static IReadOnlyList<AgentTraceFact> BuildApplicationMatchFacts(
        ApplicationMatchResult match,
        ApplicationStatus? resolvedStatus)
    {
        var facts = new List<AgentTraceFact>
        {
            new("Match confidence", $"{match.MatchingConfidence:0.00}")
        };

        if (match.Application is not null)
        {
            facts.Add(new AgentTraceFact("Application company", match.Application.Company));
            facts.Add(new AgentTraceFact("Application role", match.Application.Role));
            facts.Add(new AgentTraceFact("Application id", match.Application.Id.ToString()));
        }

        if (resolvedStatus.HasValue)
        {
            facts.Add(new AgentTraceFact("Resolved status", resolvedStatus.Value.ToString()));
        }

        return facts;
    }

    private static string BuildFollowUpSummary(
        ParsedRecruitingEmail parsed,
        ApplicationMatchResult match,
        FollowUpTaskPlan? followUpTaskPlan)
    {
        if (match.Application is null)
        {
            return "Skipped follow-up planning because no trusted application record was available.";
        }

        if (!parsed.ActionRequired)
        {
            return "Skipped follow-up planning because the extracted workflow state did not require action.";
        }

        if (followUpTaskPlan is null)
        {
            return "Skipped follow-up planning because no plan could be produced.";
        }

        return followUpTaskPlan.ShouldCreate
            ? "Planned a follow-up reminder candidate from the current workflow state."
            : "No reminder was needed after reviewing the workflow state.";
    }

    private static IReadOnlyList<AgentTraceFact> BuildFollowUpFacts(
        FollowUpTaskPlan? followUpTaskPlan,
        DateTimeOffset? normalizedDueDateUtc)
    {
        var facts = new List<AgentTraceFact>();

        if (normalizedDueDateUtc.HasValue)
        {
            facts.Add(new AgentTraceFact("Due date (UTC)", normalizedDueDateUtc.Value.ToString("O")));
        }

        if (followUpTaskPlan is null)
        {
            return facts;
        }

        facts.Add(new AgentTraceFact("Should create reminder", followUpTaskPlan.ShouldCreate ? "Yes" : "No"));
        facts.Add(new AgentTraceFact("Reminder type", followUpTaskPlan.Type.ToString()));

        if (!string.IsNullOrWhiteSpace(followUpTaskPlan.Title))
        {
            facts.Add(new AgentTraceFact("Reminder title", followUpTaskPlan.Title));
        }

        facts.Add(new AgentTraceFact("Reminder due at (UTC)", followUpTaskPlan.DueAtUtc.NormalizeUtc().ToString("O")));
        return facts;
    }
}
