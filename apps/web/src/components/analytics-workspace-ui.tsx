import type { Route } from "next";
import Link from "next/link";
import { memo, useMemo } from "react";
import type {
  AnalyticsBreakdownItem,
  AnalyticsInsight,
  AnalyticsProvider,
  AnalyticsRange,
} from "@/lib/analytics";

const SAMPLE_SIZE_THRESHOLD = 10;
const STRATEGY_REVIEW_THRESHOLD = SAMPLE_SIZE_THRESHOLD;
const KNOWN_GOOD_SINGLE_WORD_ROLES = new Set([
  "ai",
  "backend",
  "cloud",
  "data",
  "devops",
  "frontend",
  "fullstack",
  "infra",
  "ml",
  "research",
  "robotics",
]);

export const STAGE_DRILL_VIEW: Record<string, string> = {
  applied: "all",
  assessment: "needs-action",
  interview: "interviewing",
  offer: "offer",
  phone_screen: "active",
  recruiter_screen: "active",
  responded: "active",
};

export const STAGE_COLORS = [
  "var(--brand-navy)",
  "var(--brand-blue)",
  "var(--brand-blue-ink)",
  "var(--accent)",
  "var(--brand-green)",
];

export type BreakdownSort =
  | "volume"
  | "reply"
  | "interview"
  | "offer"
  | "needs-strategy";

type BreakdownKind = "company" | "role";

export function formatPercent(value: number) {
  return `${value.toFixed(value % 1 === 0 ? 0 : 1)}%`;
}

export function formatRelativeTime(value: number | null) {
  if (value === null) return "No data";
  const totalHours = Math.max(0, value);
  if (totalHours < 1) {
    const minutes = Math.round(totalHours * 60);
    return `${minutes}m`;
  }
  if (totalHours < 24) {
    return `${totalHours.toFixed(totalHours % 1 === 0 ? 0 : 1)}h`;
  }
  const days = Math.floor(totalHours / 24);
  let normalizedDays = days;
  let remainderHours = Math.round(totalHours - days * 24);
  if (remainderHours === 24) {
    normalizedDays += 1;
    remainderHours = 0;
  }
  if (remainderHours === 0) {
    return `${normalizedDays}d`;
  }
  return `${normalizedDays}d ${remainderHours}h`;
}

export function buildAnalyticsHref(
  range: AnalyticsRange,
  provider: AnalyticsProvider,
): Route {
  const searchParams = new URLSearchParams();
  if (range !== "all") searchParams.set("range", range);
  if (provider !== "all") searchParams.set("provider", provider);
  const href = `/app/analytics${searchParams.size > 0 ? `?${searchParams.toString()}` : ""}`;
  return href as Route;
}

export function buildApplicationsDrillHref(
  query: string,
  view?: string,
): Route {
  const searchParams = new URLSearchParams();
  if (query) searchParams.set("q", query);
  if (view) searchParams.set("view", view);
  return `/app/applications${searchParams.size > 0 ? `?${searchParams.toString()}` : ""}` as Route;
}

function buildBreakdownDrillHref(
  label: string,
  drillKind: BreakdownKind,
): Route {
  // Every application for that company or role, not only the default needs-action view.
  const searchParams = new URLSearchParams({ view: "all" });
  switch (drillKind) {
    case "company":
      searchParams.set("company", label);
      break;
    case "role":
      searchParams.set("roleCanonical", label);
      break;
  }

  return `/app/applications?${searchParams.toString()}` as Route;
}

const UNKNOWN_TOKENS = [
  "unknown",
  "other / unknown",
  "other/unknown",
  "uncategorized",
  "unclassified",
  "n/a",
];

function isUnknownLabel(label: string) {
  const normalized = label.trim().toLowerCase();
  if (!normalized) return true;
  return UNKNOWN_TOKENS.some((token) => normalized.includes(token));
}

function isLowConfidenceRoleLabel(label: string) {
  if (isUnknownLabel(label)) return true;
  // Single generic words like "Intern", "Engineer", "recruiter".
  const trimmed = label.trim();
  if (!trimmed) return true;
  if (
    trimmed.split(/\s+/).length === 1 &&
    trimmed.length <= 12 &&
    !KNOWN_GOOD_SINGLE_WORD_ROLES.has(trimmed.toLowerCase())
  ) {
    return true;
  }
  return false;
}

function sortBreakdown(
  rows: AnalyticsBreakdownItem[],
  sort: BreakdownSort,
): AnalyticsBreakdownItem[] {
  const copy = [...rows];
  switch (sort) {
    case "reply":
      return copy.sort(
        (left, right) =>
          right.replyRate - left.replyRate ||
          right.applicationsCount - left.applicationsCount,
      );
    case "interview":
      return copy.sort(
        (left, right) =>
          right.interviewRate - left.interviewRate ||
          right.applicationsCount - left.applicationsCount,
      );
    case "offer":
      return copy.sort(
        (left, right) =>
          right.offerRate - left.offerRate ||
          right.applicationsCount - left.applicationsCount,
      );
    case "needs-strategy":
      return copy
        .filter(
          (row) =>
            row.applicationsCount >= STRATEGY_REVIEW_THRESHOLD &&
            row.interviewRate === 0,
        )
        .sort(
          (left, right) => right.applicationsCount - left.applicationsCount,
        );
    case "volume":
    default:
      return copy.sort(
        (left, right) => right.applicationsCount - left.applicationsCount,
      );
  }
}

export type FunnelStage = {
  key: string;
  label: string;
  value: number;
  href: Route;
  color: string;
  description: string;
};

const INSIGHT_TONE_ORDER: Record<AnalyticsInsight["tone"], number> = {
  warning: 0,
  win: 1,
  info: 2,
};

export function sortInsightsByImportance(insights: AnalyticsInsight[]): AnalyticsInsight[] {
  return [...insights].sort(
    (left, right) => INSIGHT_TONE_ORDER[left.tone] - INSIGHT_TONE_ORDER[right.tone],
  );
}

function RateCell({
  value,
  color,
  lowSample,
}: {
  value: number;
  color: string;
  lowSample: boolean;
}) {
  return (
    <div className="flex items-center justify-end gap-2">
      <div className="relative h-1.5 w-10 overflow-hidden rounded-full bg-[var(--surface-overlay)] min-[1500px]:w-14">
        <div
          className="h-full rounded-full"
          style={{
            width: `${Math.min(Math.max(value, 0), 100)}%`,
            backgroundColor: color,
            opacity: lowSample ? 0.55 : 1,
          }}
        />
      </div>
      {/* Low samples show only on the bar; faded text would fall below readable contrast. */}
      <span className="text-xs tabular-nums" style={{ color }}>
        {formatPercent(value)}
      </span>
    </div>
  );
}

type BreakdownTableProps = {
  eyebrow: string;
  rows: AnalyticsBreakdownItem[];
  sort: BreakdownSort;
  onChangeSort: (sort: BreakdownSort) => void;
  drillKind: BreakdownKind;
  filterUnknown: boolean;
};

function getMobileOutcome(row: AnalyticsBreakdownItem) {
  if (row.offerRate > 0) {
    return {
      label: "Offer",
      value: row.offerRate,
      color: "var(--green)",
    };
  }
  if (row.interviewRate > 0) {
    return {
      label: "Interview",
      value: row.interviewRate,
      color: "var(--accent)",
    };
  }
  if (row.replyRate > 0) {
    return {
      label: "Reply",
      value: row.replyRate,
      color: "var(--blue)",
    };
  }
  return {
    label: "Reply",
    value: 0,
    color: "var(--text-tertiary)",
  };
}

function BreakdownSortHeader({
  label,
  value,
  sort,
  align = "right",
  title,
  onChangeSort,
}: {
  label: string;
  value: BreakdownSort;
  sort: BreakdownSort;
  align?: "left" | "right";
  title?: string;
  onChangeSort: (sort: BreakdownSort) => void;
}) {
  const active = sort === value;

  return (
    <button
      type="button"
      className={`eyebrow inline-flex items-center gap-1.5 hover:text-[var(--text-primary)] ${
        align === "right" ? "justify-end text-right" : "justify-start text-left"
      }`}
      aria-pressed={active}
      aria-label={`Sort by ${label}`}
      title={title ?? `Sort by ${label}`}
      onClick={() => onChangeSort(value)}
    >
      <span>{label}</span>
      <span
        aria-hidden="true"
        className={active ? "text-[var(--accent)]" : "text-[var(--text-tertiary)]"}
      >
        {active ? "↓" : "↕"}
      </span>
    </button>
  );
}

export const BreakdownTable = memo(function BreakdownTable({
  eyebrow,
  rows,
  sort,
  onChangeSort,
  drillKind,
  filterUnknown,
}: BreakdownTableProps) {
  const visibleRows = useMemo(
    () =>
      filterUnknown
        ? rows.filter((row) => !isUnknownLabel(row.label))
        : rows,
    [filterUnknown, rows],
  );
  const hiddenCount = rows.length - visibleRows.length;
  const sortedRows = useMemo(
    () => sortBreakdown(visibleRows, sort),
    [visibleRows, sort],
  );

  return (
    <div className="card flex min-h-0 flex-col overflow-hidden p-0 xl:h-full">
      <div className="shrink-0 border-b border-[var(--border)] px-4 py-2.5">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-base font-semibold text-[var(--text-primary)]">{eyebrow}</h2>
          <span className="text-xs tabular-nums text-[var(--text-tertiary)]">{sortedRows.length}</span>
          {hiddenCount > 0 ? (
            <Link
              href={"/app/review" as Route}
              className="badge border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)] hover:underline max-sm:min-h-6"
              title={`${hiddenCount} ${drillKind} row${hiddenCount === 1 ? "" : "s"} hidden because the ${drillKind} could not be identified. Fix in review queue.`}
            >
              {hiddenCount} hidden
            </Link>
          ) : null}
        </div>
      </div>

      {sortedRows.length === 0 ? (
        <p className="p-4 text-sm text-[var(--text-tertiary)]">
          {sort === "needs-strategy"
            ? `Nothing flagged yet — no companies have ${STRATEGY_REVIEW_THRESHOLD}+ applications without an interview.`
            : "No data yet."}
        </p>
      ) : (
        <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
          <div className="space-y-2 p-3 lg:hidden">
            {sortedRows.map((row) => {
              const lowQuality =
                drillKind === "role" && isLowConfidenceRoleLabel(row.label);
              const outcome = getMobileOutcome(row);

              return (
                <Link
                  key={row.label}
                  href={buildBreakdownDrillHref(row.label, drillKind)}
                  className="block rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-3 py-2.5 transition-colors hover:border-[var(--brand-blue)]/30 hover:bg-[var(--surface-overlay)]"
                  title={row.label}
                >
                  <div className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-semibold text-[var(--text-primary)]">
                        {row.label}
                      </p>
                      <p className="mt-0.5 text-xs text-[var(--text-tertiary)]">
                        {row.applicationsCount} app
                        {row.applicationsCount === 1 ? "" : "s"}
                      </p>
                    </div>
                    <div className="text-right">
                      <p
                        className=" text-base font-semibold tabular-nums"
                        style={{ color: outcome.color }}
                      >
                        {formatPercent(outcome.value)}
                      </p>
                      <p className=" text-xs font-semibold text-[var(--text-tertiary)]">
                        {outcome.label}
                      </p>
                    </div>
                  </div>
                  {lowQuality ? (
                    <span
                      className="mt-2 inline-flex rounded-full border border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] px-2 py-0.5 text-xs font-semibold text-[var(--brand-blue-ink)]"
                      title="Generic or low-confidence label — these distort analytics."
                    >
                      Review label
                    </span>
                  ) : null}
                </Link>
              );
            })}
          </div>

          <table className="hidden h-full w-full table-fixed text-sm lg:table">
            <colgroup>
              <col className="w-[34%]" />
              <col className="w-[10%]" />
              <col className="w-[18%]" />
              <col className="w-[18%]" />
              <col className="w-[18%]" />
            </colgroup>
            <thead>
              <tr className="h-px bg-[var(--surface-elevated)]">
                <th className="text-left px-3 py-3 text-xs font-medium text-[var(--text-tertiary)]">
                  Name
                </th>
                <th className="px-3 py-3 text-right">
                  <BreakdownSortHeader
                    label="Apps"
                    value="volume"
                    sort={sort}
                    onChangeSort={onChangeSort}
                    title="Sort by application volume"
                  />
                </th>
                <th className="px-3 py-3 text-right">
                  <BreakdownSortHeader
                    label="Reply"
                    value="reply"
                    sort={sort}
                    onChangeSort={onChangeSort}
                    title="Reply = any inbound email after applying. Includes auto-receipts, recruiter replies, assessments, interviews, offers, and rejections."
                  />
                </th>
                <th className="px-3 py-3 text-right">
                  <BreakdownSortHeader
                    label="Interview"
                    value="interview"
                    sort={sort}
                    onChangeSort={onChangeSort}
                    title="Sort by interview conversion"
                  />
                </th>
                <th className="px-3 py-3 text-right">
                  <BreakdownSortHeader
                    label="Offer"
                    value="offer"
                    sort={sort}
                    onChangeSort={onChangeSort}
                    title="Sort by offer conversion"
                  />
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border)]">
              {sortedRows.map((row) => {
                  const lowSample = row.applicationsCount < SAMPLE_SIZE_THRESHOLD;
                  const lowQuality =
                  drillKind === "role" && isLowConfidenceRoleLabel(row.label);
                return (
                  <tr
                    key={row.label}
                    className="transition-colors hover:bg-[var(--surface-overlay)]"
                  >
                    <td
                      className="max-w-[14rem] truncate px-3 py-3 font-medium text-[var(--text-primary)]"
                      title={row.label}
                    >
                      <Link
                        href={buildBreakdownDrillHref(row.label, drillKind)}
                        className="hover:underline"
                      >
                        {row.label}
                      </Link>
                      {lowQuality ? (
                        <span
                          className="badge ml-2 border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]"
                          title="Generic or low-confidence label — these distort analytics."
                        >
                          Review
                        </span>
                      ) : null}
                    </td>
                    <td className="px-3 py-3 text-right tabular-nums text-[var(--text-secondary)]">
                      {row.applicationsCount}
                    </td>
                    <td className="px-3 py-3">
                      <RateCell
                        value={row.replyRate}
                        color="var(--blue)"
                        lowSample={lowSample}
                      />
                    </td>
                    <td className="px-3 py-3">
                      <RateCell
                        value={row.interviewRate}
                        color="var(--accent)"
                        lowSample={lowSample}
                      />
                    </td>
                    <td className="px-3 py-3">
                      <RateCell
                        value={row.offerRate}
                        color="var(--green)"
                        lowSample={lowSample}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
});

BreakdownTable.displayName = "BreakdownTable";

export function FunnelChart({
  stages,
}: {
  stages: FunnelStage[];
}) {
  const headValue = stages[0]?.value ?? 0;
  return (
    <div className="grid w-full flex-1 gap-2 lg:grid-cols-5 lg:items-center lg:gap-3 xl:gap-5">
      {stages.map((stage, index) => {
        const previous = index === 0 ? null : stages[index - 1];
        const continuationRate =
          previous && previous.value > 0
            ? (stage.value / previous.value) * 100
            : null;
        const widthRatio =
          headValue > 0 ? Math.max(8, (stage.value / headValue) * 100) : 8;
        return (
          <Link
            key={stage.key}
            href={stage.href}
            title={stage.description}
            className="group grid gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-3 transition-colors hover:border-[var(--brand-blue)]/35 hover:bg-[var(--surface-overlay)] lg:min-h-[7.15rem] lg:grid-rows-[auto_1fr_auto] lg:gap-3"
          >
            <div className="flex items-center justify-between gap-3">
              <p className=" text-xs text-[var(--text-tertiary)]">
                {stage.label}
              </p>
              <p
                className="text-base font-semibold tabular-nums"
                style={{ color: stage.color }}
              >
                {stage.value}
              </p>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-[var(--surface-overlay)] lg:h-2">
              <div
                className="h-full rounded-full transition-all duration-500"
                style={{
                  width: `${widthRatio}%`,
                  backgroundColor: stage.color,
                }}
              />
            </div>
            <p className="text-xs leading-4 text-[var(--text-tertiary)] lg:text-xs lg:leading-5">
              {previous && continuationRate !== null
                ? formatPercent(continuationRate)
                : "100%"}
            </p>
          </Link>
        );
      })}
    </div>
  );
}

export function InsightCard({ insights }: { insights: AnalyticsInsight[] }) {
  return (
    <div className="card p-4">
      <h2 className="text-base font-semibold text-[var(--text-primary)]">Insights</h2>
      {insights.length === 0 ? (
        <p className="mt-2 text-sm text-[var(--text-secondary)]">
          Not enough data yet.
        </p>
      ) : (
        <ul className="mt-2 divide-y divide-[var(--border)]">
          {insights.map((insight) => {
            const dotClass =
              insight.tone === "warning"
                ? "bg-[var(--brand-blue)]"
                : insight.tone === "win"
                  ? "bg-[var(--green)]"
                  : "bg-[var(--text-tertiary)]";
            const content = (
              <div className="text-sm" title={insight.body}>
                <p className="text-[var(--text-secondary)]">
                  {insight.headline}
                </p>
              </div>
            );
            return (
              <li key={insight.id} className="flex gap-2 py-2">
                <span
                  aria-hidden="true"
                  className={`mt-2 h-1.5 w-1.5 shrink-0 rounded-full ${dotClass}`}
                />
                <span className="flex-1">{content}</span>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
