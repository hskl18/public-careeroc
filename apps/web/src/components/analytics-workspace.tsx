"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { LocalTime } from "@/components/local-time";
import {
  analyticsProviderOptions,
  analyticsRangeOptions,
  type AnalyticsProvider,
  type AnalyticsRange,
  type AnalyticsResponse,
} from "@/lib/analytics";
import {
  BreakdownTable,
  FunnelChart,
  InsightCard,
  STAGE_COLORS,
  STAGE_DRILL_VIEW,
  buildAnalyticsHref,
  buildApplicationsDrillHref,
  formatPercent,
  formatRelativeTime,
  mapServerInsights,
  sortInsightsByImportance,
  type BreakdownSort,
} from "./analytics-workspace-ui";

type AnalyticsWorkspaceProps = {
  initialAnalytics: AnalyticsResponse;
  source: "api" | "fallback";
  selectedRange: AnalyticsRange;
  selectedProvider: AnalyticsProvider;
};

export function AnalyticsWorkspace({
  initialAnalytics,
  source,
  selectedRange,
  selectedProvider,
}: AnalyticsWorkspaceProps) {
  const router = useRouter();
  const [companySort, setCompanySort] = useState<BreakdownSort>("volume");
  const [roleSort, setRoleSort] = useState<BreakdownSort>("volume");
  const [filterUnknown, setFilterUnknown] = useState(true);
  const { metrics, companyBreakdown, roleBreakdown } = initialAnalytics;

  const funnelStages = useMemo(() => {
    return (initialAnalytics.funnel ?? []).map((stage, index) => ({
      key: stage.key,
      label: stage.label,
      value: stage.count,
      href: buildApplicationsDrillHref(
        "",
        STAGE_DRILL_VIEW[stage.key] ?? "active",
      ),
      color: STAGE_COLORS[index % STAGE_COLORS.length] ?? "var(--text-primary)",
      description:
        index === 0
          ? "Top of the funnel."
          : `${formatPercent(stage.continuationRateFromPreviousStage * 100)} continuation from the previous stage.`,
    }));
  }, [initialAnalytics.funnel]);
  const insights = useMemo(() => {
    return sortInsightsByImportance(mapServerInsights(initialAnalytics.insights));
  }, [initialAnalytics.insights]);

  const summaryCards = [
    {
      label: "Avg 1st reply",
      value: formatRelativeTime(metrics.avgTimeToFirstResponseHours),
      color:
        metrics.avgTimeToFirstResponseHours === null
          ? "var(--text-tertiary)"
          : "var(--brand-green-ink)",
      tooltip:
        metrics.avgTimeToFirstResponseHours === null
          ? "No reply data yet."
          : `${metrics.avgTimeToFirstResponseHours.toFixed(1)} hours average across applications with at least one inbound email.`,
    },
  ];

  return (
    <main className="app-scroll-main analytics-main">
      <div className="analytics-shell mx-auto flex h-full min-h-0 w-full max-w-[104rem] flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-5">
        <section className="analytics-panel shrink-0 p-3 sm:p-4">
          <div className="flex flex-col gap-3 xl:flex-row xl:items-center">
            <div className="min-w-0 flex-1">
              <p className="eyebrow">Analytics</p>
              <h1 className="mt-1 text-lg font-semibold text-[var(--text-primary)]">
                Where is the effort actually working?
              </h1>
              {initialAnalytics.lastSyncAtUtc ? (
                <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                  As of <LocalTime value={initialAnalytics.lastSyncAtUtc} format="shortDateTime" />
                </p>
              ) : null}
            </div>

            <div className="flex shrink-0 flex-wrap items-center gap-2 xl:justify-end">
              <span
                className={`badge ${
                  source === "api"
                    ? "border-[var(--brand-green)]/25 bg-[var(--green-soft)] text-[var(--brand-green-ink)]"
                    : "border-[var(--brand-blue)]/20 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]"
                }`}
              >
                {source === "api" ? "Live" : "Offline"}
              </span>
              {summaryCards.map((card) => (
                <div
                  key={card.label}
                  className="analytics-stat flex min-h-9 items-center gap-2 rounded-md border border-[var(--brand-blue)]/14 px-3 py-1"
                  title={card.tooltip}
                >
                  <p className="eyebrow">{card.label}</p>
                  <p
                    className="text-sm font-semibold tabular-nums leading-tight"
                    style={{ color: card.color }}
                  >
                    {card.value}
                  </p>
                </div>
              ))}
              <select
                aria-label="Analytics range"
                className="analytics-filter-select h-7 w-[6.6rem] rounded-md border border-[var(--border)] bg-white/88 px-2 pr-6 font-mono font-semibold uppercase tracking-[0.06em] text-[var(--text-primary)] outline-none transition hover:border-[var(--brand-blue)]/35 focus:border-[var(--accent)]"
                value={selectedRange}
                onChange={(event) =>
                  router.push(
                    buildAnalyticsHref(
                      event.target.value as AnalyticsRange,
                      selectedProvider,
                    ),
                  )
                }
              >
                {analyticsRangeOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
              <select
                aria-label="Mail source"
                className="analytics-filter-select h-7 w-[6.8rem] rounded-md border border-[var(--border)] bg-white/88 px-2 pr-6 font-mono font-semibold uppercase tracking-[0.06em] text-[var(--text-primary)] outline-none transition hover:border-[var(--brand-blue)]/35 focus:border-[var(--accent)]"
                value={selectedProvider}
                onChange={(event) =>
                  router.push(
                    buildAnalyticsHref(
                      selectedRange,
                      event.target.value as AnalyticsProvider,
                    ),
                  )
                }
              >
                {analyticsProviderOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.value === "all" ? "All mail" : option.label}
                  </option>
                ))}
              </select>
              {metrics.applicationsCount > 0 ? (
                <label
                  className="inline-flex min-h-9 items-center gap-2 rounded-md border border-[var(--brand-blue)]/14 bg-white/60 px-2.5 text-[11px] font-medium text-[var(--text-secondary)]"
                  title="Hide rows with unknown company or role labels."
                >
                  <input
                    type="checkbox"
                    checked={filterUnknown}
                    onChange={(event) => setFilterUnknown(event.target.checked)}
                    className="h-3.5 w-3.5"
                  />
                  Unknown
                </label>
              ) : null}
            </div>
          </div>

          {source === "fallback" &&
            (selectedRange !== "all" || selectedProvider !== "all") && (
              <div className="alert alert-warning mt-3">
                Filters inactive — API unavailable.
              </div>
            )}
        </section>

        {metrics.applicationsCount === 0 && (
          <div className="analytics-panel shrink-0 p-5">
            <p className="text-sm text-[var(--text-tertiary)]">
              No applications yet — analytics will populate as records are
              created.
            </p>
          </div>
        )}

        {metrics.applicationsCount > 0 ? (
          <div className="grid gap-4 xl:grid-cols-[1.4fr_1fr]">
            <section className="analytics-panel flex flex-col p-3 sm:p-4 lg:min-h-[13.25rem]">
              <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="eyebrow">Funnel</p>
                  <h2 className="mt-1 text-base font-semibold text-[var(--text-primary)]">
                    Applied → Response → Interview → Offer
                  </h2>
                </div>
                {metrics.responseRates ? (
                  <p className="text-right text-[11px] leading-6 text-[var(--text-tertiary)]">
                    Human {formatPercent(metrics.responseRates.humanResponseRate)}
                    {" "}· auto receipt {formatPercent(metrics.responseRates.autoReceiptRate)}
                  </p>
                ) : null}
              </div>
              <FunnelChart stages={funnelStages} />
            </section>

            <InsightCard insights={insights} />
          </div>
        ) : null}

        <div className="grid min-h-0 items-start gap-4 xl:grid-cols-2">
          <BreakdownTable
            eyebrow="Companies"
            rows={companyBreakdown}
            sort={companySort}
            onChangeSort={setCompanySort}
            drillKind="company"
            filterUnknown={filterUnknown}
          />
          <BreakdownTable
            eyebrow="Roles"
            rows={roleBreakdown}
            sort={roleSort}
            onChangeSort={setRoleSort}
            drillKind="role"
            filterUnknown={filterUnknown}
          />
        </div>
      </div>
    </main>
  );
}
