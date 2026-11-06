"use client";

import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
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
  sortInsightsByImportance,
  type BreakdownSort,
} from "./analytics-workspace-ui";

type AnalyticsWorkspaceProps = {
  initialAnalytics: AnalyticsResponse;
  source: "api" | "fallback";
  selectedRange: AnalyticsRange;
  selectedProvider: AnalyticsProvider;
  readOnlyDemo: boolean;
};

export function AnalyticsWorkspace({
  initialAnalytics,
  source,
  selectedRange,
  selectedProvider,
  readOnlyDemo,
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
    return sortInsightsByImportance(initialAnalytics.insights ?? []);
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
    <main className="app-scroll-main">
      <div className="workspace-shell flex min-h-0 w-full flex-col gap-3 px-3 py-4 animate-in sm:gap-4 sm:px-5 sm:py-5">
        <section className="shrink-0">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              {/* The title fills the row's left side, as on Settings and Memory. */}
              <h1 className="mr-auto text-xl font-semibold text-[var(--text-primary)]">Analytics</h1>
              {summaryCards.map((card) => (
                <div
                  key={card.label}
                  className="bg-[var(--surface-elevated)] flex min-h-9 items-center gap-2 rounded-md border border-[var(--brand-blue)]/14 px-3 py-1"
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
                className="input h-7 w-[6.6rem] py-0 pr-6 pl-2 text-xs"
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
              {readOnlyDemo ? null : (
                <select
                  aria-label="Mail source"
                  className="input h-7 w-[6.8rem] py-0 pr-6 pl-2 text-xs"
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
              )}
              {metrics.applicationsCount > 0 ? (
                <label
                  className="inline-flex min-h-9 items-center gap-2 rounded-md border border-[var(--brand-blue)]/14 bg-[var(--surface-strong)] px-2.5 text-xs font-medium text-[var(--text-secondary)]"
                >
                  <input
                    type="checkbox"
                    checked={filterUnknown}
                    onChange={(event) => setFilterUnknown(event.target.checked)}
                    className="h-3.5 w-3.5"
                  />
                  Hide unknown
                </label>
              ) : null}
            </div>
          </div>

          {source === "fallback" &&
            (selectedRange !== "all" || selectedProvider !== "all") && (
              <div role="status" className="alert alert-warning mt-3">
                API offline.
              </div>
            )}
        </section>

        {metrics.applicationsCount === 0 && (
          <div className="card shrink-0 p-5">
            <p className="text-sm text-[var(--text-tertiary)]">
              No applications yet.
            </p>
          </div>
        )}

        {metrics.applicationsCount > 0 ? (
          <div className="grid shrink-0 gap-3 xl:grid-cols-[1.4fr_1fr]">
            <section className="card flex flex-col p-4">
              <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
                <h2 className="text-base font-semibold text-[var(--text-primary)]">Funnel</h2>
              </div>
              <FunnelChart stages={funnelStages} />
            </section>

            <InsightCard insights={insights} />
          </div>
        ) : null}

        <div className="grid min-h-0 flex-1 gap-3 overflow-y-auto xl:grid-cols-2 xl:overflow-hidden xl:[grid-template-rows:minmax(0,1fr)]">
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
