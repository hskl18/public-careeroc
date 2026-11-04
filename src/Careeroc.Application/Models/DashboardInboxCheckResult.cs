using Careeroc.Contracts.Dashboard;

namespace Careeroc.Application.Models;

public sealed record DashboardInboxCheckResult(
    bool CacheHit,
    EmailSyncResult Sync,
    int StaleEmailsQueued,
    EmailProcessingResult Processing,
    DashboardResponse Dashboard);
