import type { components } from "./api-schema";

type Schemas = components["schemas"];

export type DashboardMetrics = Schemas["DashboardMetricsDto"];
export type InboxOverview = Schemas["InboxOverviewDto"];
export type ApplicationCard = Schemas["ApplicationSummaryDto"];
export type ApplicationNextAction = Schemas["ApplicationSummaryNextActionDto"];
export type ApplicationLatestEvidence = Schemas["ApplicationSummaryEvidenceDto"];
export type PendingAction = Schemas["PendingActionDto"];
export type RecentActivity = Schemas["RecentActivityDto"];
export type DashboardResponse = Schemas["DashboardResponse"];

export type DashboardView =
  | "needs-action"
  | "needs-review"
  | "active"
  | "waiting"
  | "interviewing"
  | "offer"
  | "rejected"
  | "all";
