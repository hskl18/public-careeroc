using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Application.Services;
using Careeroc.Application.Services.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace Careeroc.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddOptions<DemoOptions>();
        services.AddScoped<InboxTriageAgentService>();
        services.AddScoped<WorkflowExtractionAgentService>();
        services.AddScoped<RecruiterIdentityAgentService>();
        services.AddScoped<ScamCheckerAgentService>();
        services.AddSingleton<IReviewEvidenceModel>(NullReviewEvidenceModel.Instance);
        services.AddSingleton<IResumeEvaluationModel>(NullResumeEvaluationModel.Instance);
        services.AddSingleton<IResumeFitEnricher>(NullResumeFitEnricher.Instance);
        services.AddSingleton<ICareerAgentGraphClient>(NullCareerAgentGraphClient.Instance);
        services.AddSingleton<IUserAgentContextSnapshotProvider>(NullUserAgentContextSnapshotProvider.Instance);
        services.AddSingleton<IModelCallEventRecorder>(NullModelCallEventRecorder.Instance);
        services.AddSingleton<IInboxAttachmentDownloader>(NoopInboxAttachmentDownloader.Instance);
        services.AddScoped<ReviewEvidenceAgentService>();
        services.AddScoped<FollowUpTaskAgentService>();
        services.AddScoped<InboxSetupService>();
        services.AddScoped<AnalyticsQueryService>();
        services.AddScoped<EmailSyncService>();
        services.AddScoped<InboxSyncCoordinator>();
        services.AddScoped<DashboardInboxCheckService>();
        services.AddScoped<EmailAnalysisMaintenanceService>();
        services.AddScoped<ApplicationWorkflowService>();
        services.AddScoped<RecruitingContactGraphService>();
        services.AddScoped<RecruitingContactQueryService>();
        services.AddScoped<ApplicationDetailQueryService>();
        services.AddScoped<ApplicationArtifactEvidenceService>();
        services.AddScoped<IEmailAttachmentEvidenceImporter, EmailAttachmentEvidenceImportService>();
        services.AddScoped<ApplicationLookupQueryService>();
        services.AddScoped<EmailIntelligenceService>();
        services.AddScoped<ReminderEvidenceRefreshService>();
        services.AddScoped<DashboardQueryService>();
        services.AddScoped<UserProfileService>();
        services.AddScoped<UserAgentContextSnapshotService>();
        services.AddScoped<IUserAgentContextSnapshotProvider>(
            serviceProvider => serviceProvider.GetRequiredService<UserAgentContextSnapshotService>());
        services.AddScoped<UserMemoryProposalService>();
        services.AddScoped<ResumeMailboxContextBuilder>();
        services.AddScoped<ResumeIntelligenceService>();
        services.AddScoped<ResumeFitService>();
        services.AddScoped<ResumeSuggestionService>();
        services.AddScoped<CareerOrchestratorService>();
        services.AddScoped<ManualReviewQueryService>();
        services.AddScoped<ManualReviewService>();
        services.AddScoped<ReminderService>();
        services.AddScoped<DemoWorkspaceService>();
        services.AddScoped<PersonalAccessTokenService>();
        services.AddScoped<AgentQueryService>();
        return services;
    }
}
