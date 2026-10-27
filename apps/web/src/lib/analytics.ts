export type AnalyticsRange = "all" | "30d" | "90d";
export type AnalyticsProvider = "all" | "gmail";

export type AnalyticsMetrics = {
  applicationsCount: number;
  uniqueCompanies: number;
  rolesCount: number;
  replyRate: number;
  interviewRate: number;
  offerRate: number;
  avgTimeToFirstResponseHours: number | null;
  responseRates?: AnalyticsResponseRates | null;
};

export type AnalyticsResponseRates = {
  anyResponseRate: number;
  autoReceiptRate: number;
  humanResponseRate: number;
  assessmentRate: number;
  interviewRate: number;
  offerRate: number;
  rejectionRate: number;
};

export type AnalyticsBreakdownItem = {
  label: string;
  applicationsCount: number;
  replyRate: number;
  interviewRate: number;
  offerRate: number;
  avgTimeToFirstResponseHours: number | null;
  responseRates?: AnalyticsResponseRates | null;
};

export type AnalyticsFunnelStage = {
  key: string;
  label: string;
  count: number;
  continuationRateFromPreviousStage: number;
};

export type AnalyticsInsight = {
  id: string;
  tone: "warning" | "win" | "info";
  headline: string;
  body: string;
  drillHref: string | null;
};

export type AnalyticsResponse = {
  metrics: AnalyticsMetrics;
  companyBreakdown: AnalyticsBreakdownItem[];
  roleBreakdown: AnalyticsBreakdownItem[];
  funnel?: AnalyticsFunnelStage[] | null;
  sourceBreakdown?: AnalyticsBreakdownItem[] | null;
  locationBreakdown?: AnalyticsBreakdownItem[] | null;
  seniorityBreakdown?: AnalyticsBreakdownItem[] | null;
  insights?: AnalyticsInsight[] | null;
  lastSyncAtUtc?: string | null;
};

export const analyticsRangeOptions: Array<{
  value: AnalyticsRange;
  label: string;
}> = [
  { value: "all", label: "All time" },
  { value: "30d", label: "Last 30 days" },
  { value: "90d", label: "Last 90 days" },
];

export const analyticsProviderOptions: Array<{
  value: AnalyticsProvider;
  label: string;
}> = [
  { value: "all", label: "All Gmail inboxes" },
  { value: "gmail", label: "Gmail" },
];

export function normalizeAnalyticsRange(value: string | string[] | undefined): AnalyticsRange {
  const normalized = Array.isArray(value) ? value[0] : value;

  return normalized === "30d" || normalized === "90d" ? normalized : "all";
}

export function normalizeAnalyticsProvider(
  value: string | string[] | undefined,
): AnalyticsProvider {
  const normalized = Array.isArray(value) ? value[0] : value;

  return normalized === "gmail" ? normalized : "all";
}
