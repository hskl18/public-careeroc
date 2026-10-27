using Careeroc.Contracts.Applications;

namespace Careeroc.Contracts.Dashboard;

public sealed record DashboardResponse(
    DashboardMetricsDto Metrics,
    InboxOverviewDto Inbox,
    IReadOnlyList<ApplicationSummaryDto> Applications,
    IReadOnlyList<PendingActionDto> PendingActions,
    IReadOnlyList<RecentActivityDto> RecentActivity,
    string? NextCursor = null);

