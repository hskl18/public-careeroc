using Careeroc.Application.Abstractions;
using Careeroc.Application.Services;
using Careeroc.Application.Services.Agents;
using Careeroc.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Careeroc.Tests;

// Production classes take every dependency in one constructor, so the app never runs with a
// silent stand-in. Tests build them here, with the null objects for the parts a test does not use.
internal static class TestServices
{
    public static EmailIntelligenceService EmailIntelligenceService(
        ICareerocDbContext dbContext,
        IEmailProcessingQueue emailProcessingQueue,
        IRecruitingEmailParser recruitingEmailParser,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock,
        ILogger<EmailIntelligenceService> logger,
        ILeaseManager? leaseManager = null) =>
        new(
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
            NullModelCallEventRecorder.Instance,
            leaseManager ?? NoopLeaseManager.Instance);

    public static EmailSyncService EmailSyncService(
        ICareerocDbContext dbContext,
        IInboxEmailSource inboxEmailSource,
        IEmailProcessingQueue emailProcessingQueue,
        IClock clock,
        ILogger<EmailSyncService> logger) =>
        new(dbContext, inboxEmailSource, emailProcessingQueue, NoopLeaseManager.Instance, clock, logger);

    public static EmailSyncService EmailSyncService(
        ICareerocDbContext dbContext,
        IInboxEmailSource inboxEmailSource,
        IEmailProcessingQueue emailProcessingQueue,
        ILeaseManager leaseManager,
        IClock clock,
        ILogger<EmailSyncService> logger) =>
        new(dbContext, inboxEmailSource, emailProcessingQueue, leaseManager, clock, logger);

    public static CareerOrchestratorService CareerOrchestratorService(ICareerocDbContext dbContext, IClock clock) =>
        CareerOrchestratorService(dbContext, NullUserAgentContextSnapshotProvider.Instance, clock);

    public static CareerOrchestratorService CareerOrchestratorService(
        ICareerocDbContext dbContext,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        IClock clock) =>
        CareerOrchestratorService(
            dbContext,
            agentContextSnapshotProvider,
            new ResumeMailboxContextBuilder(dbContext, clock),
            clock);

    public static CareerOrchestratorService CareerOrchestratorService(
        ICareerocDbContext dbContext,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ResumeMailboxContextBuilder resumeMailboxContextBuilder,
        IClock clock) =>
        CareerOrchestratorService(
            dbContext,
            agentContextSnapshotProvider,
            resumeMailboxContextBuilder,
            NullCareerAgentGraphClient.Instance,
            clock);

    public static CareerOrchestratorService CareerOrchestratorService(
        ICareerocDbContext dbContext,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ResumeMailboxContextBuilder resumeMailboxContextBuilder,
        ICareerAgentGraphClient agentGraphClient,
        IClock clock) =>
        new(dbContext, agentContextSnapshotProvider, resumeMailboxContextBuilder, agentGraphClient, clock);

    public static ManualReviewService ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        IClock clock) =>
        ManualReviewService(dbContext, applicationWorkflowService, new FollowUpTaskAgentService(clock), clock);

    public static ManualReviewService ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        FollowUpTaskAgentService followUpTaskAgentService,
        IClock clock) =>
        ManualReviewService(
            dbContext,
            applicationWorkflowService,
            followUpTaskAgentService,
            NullRecruitingEmailGuidanceStore.Instance,
            clock);

    public static ManualReviewService ManualReviewService(
        ICareerocDbContext dbContext,
        ApplicationWorkflowService applicationWorkflowService,
        FollowUpTaskAgentService followUpTaskAgentService,
        IRecruitingEmailGuidanceStore guidanceStore,
        IClock clock) =>
        new(dbContext, applicationWorkflowService, followUpTaskAgentService, guidanceStore, clock);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        OllamaRecruitingEmailParser(httpClient, heuristicParser, options, NullRecruitingEmailGuidanceStore.Instance, logger);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        IRecruitingEmailGuidanceStore guidanceStore,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        OllamaRecruitingEmailParser(
            httpClient,
            heuristicParser,
            options,
            guidanceStore,
            NullUserAgentContextSnapshotProvider.Instance,
            logger);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        IRecruitingEmailGuidanceStore guidanceStore,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        OllamaRecruitingEmailParser(
            httpClient,
            heuristicParser,
            options,
            guidanceStore,
            agentContextSnapshotProvider,
            OllamaModelCircuitBreaker.Disabled,
            NullModelCallEventRecorder.Instance,
            logger);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        IRecruitingEmailGuidanceStore guidanceStore,
        OllamaModelCircuitBreaker circuitBreaker,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        OllamaRecruitingEmailParser(
            httpClient,
            heuristicParser,
            options,
            guidanceStore,
            circuitBreaker,
            NullModelCallEventRecorder.Instance,
            logger);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        IRecruitingEmailGuidanceStore guidanceStore,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        OllamaRecruitingEmailParser(
            httpClient,
            heuristicParser,
            options,
            guidanceStore,
            NullUserAgentContextSnapshotProvider.Instance,
            circuitBreaker,
            modelCallEventRecorder,
            logger);

    public static OllamaRecruitingEmailParser OllamaRecruitingEmailParser(
        HttpClient httpClient,
        HeuristicRecruitingEmailParser heuristicParser,
        IOptions<OllamaEmailExtractionOptions> options,
        IRecruitingEmailGuidanceStore guidanceStore,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        ILogger<OllamaRecruitingEmailParser> logger) =>
        new(
            httpClient,
            heuristicParser,
            options,
            guidanceStore,
            agentContextSnapshotProvider,
            circuitBreaker,
            modelCallEventRecorder,
            logger);

    public static OllamaReviewEvidenceModel OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        ILogger<OllamaReviewEvidenceModel> logger) =>
        OllamaReviewEvidenceModel(httpClient, options, OllamaModelCircuitBreaker.Disabled, logger);

    public static OllamaReviewEvidenceModel OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        ILogger<OllamaReviewEvidenceModel> logger) =>
        OllamaReviewEvidenceModel(httpClient, options, circuitBreaker, NullModelCallEventRecorder.Instance, logger);

    public static OllamaReviewEvidenceModel OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ILogger<OllamaReviewEvidenceModel> logger) =>
        new(httpClient, options, circuitBreaker, NullModelCallEventRecorder.Instance, agentContextSnapshotProvider, logger);

    public static OllamaReviewEvidenceModel OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        ILogger<OllamaReviewEvidenceModel> logger) =>
        new(httpClient, options, circuitBreaker, modelCallEventRecorder, NullUserAgentContextSnapshotProvider.Instance, logger);

    public static OllamaReviewEvidenceModel OllamaReviewEvidenceModel(
        HttpClient httpClient,
        IOptions<OllamaEmailExtractionOptions> options,
        OllamaModelCircuitBreaker circuitBreaker,
        IModelCallEventRecorder modelCallEventRecorder,
        IUserAgentContextSnapshotProvider agentContextSnapshotProvider,
        ILogger<OllamaReviewEvidenceModel> logger) =>
        new(httpClient, options, circuitBreaker, modelCallEventRecorder, agentContextSnapshotProvider, logger);
}
