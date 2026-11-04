namespace Careeroc.Contracts.Dashboard;

public sealed record DashboardMetricsDto(
    int TotalApplications,
    int ActiveApplications,
    int NeedsAction,
    int NeedsReview,
    int WaitingOnEmployer,
    int Interviews,
    int Offers,
    int Rejections,
    int ReviewQueue,
    int ClosedApplications = 0);
