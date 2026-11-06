import type { components } from "./api-schema";

export type AnalyticsRange = "all" | "30d" | "90d";
export type AnalyticsProvider = "all" | "gmail";

type Schemas = components["schemas"];

export type AnalyticsMetrics = Schemas["AnalyticsMetricsDto"];
export type AnalyticsResponseRates = Schemas["AnalyticsResponseRatesDto"];
export type AnalyticsBreakdownItem = Schemas["AnalyticsBreakdownItemDto"];
export type AnalyticsFunnelStage = Schemas["AnalyticsFunnelStageDto"];
export type AnalyticsInsight = Schemas["AnalyticsInsightDto"];
export type AnalyticsResponse = Schemas["AnalyticsResponse"];

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
