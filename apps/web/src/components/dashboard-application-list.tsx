"use client";

import type { RefObject } from "react";
import { StatusPill } from "@/components/status-pill";
import { SortableHeaderButton } from "@/components/workspace-controls";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import { hasUnknownRole as parserHasUnknownRole } from "@/lib/parser-quality";
import type { ApplicationCard, PendingAction } from "@/lib/dashboard";
import { useHydrated } from "@/lib/use-hydrated";
import { deriveNextAction, describePriority, nextActionToneClass } from "@/lib/application-display";

export type DashboardSortColumn =
  | "company"
  | "role"
  | "stage"
  | "next-action"
  | "priority";

export type DashboardSortDirection = "desc" | "asc";

const SORT_LABEL: Record<DashboardSortColumn, string> = {
  company: "Company",
  role: "Role",
  stage: "Stage",
  "next-action": "Next action",
  priority: "Urgency",
};

type DashboardApplicationListProps = {
  applications: ApplicationCard[];
  activeApplicationId: string | null;
  pendingActions: PendingAction[];
  sortState: {
    column: DashboardSortColumn | null;
    direction: DashboardSortDirection;
  };
  listRef: RefObject<HTMLDivElement | null>;
  timeZoneId: string;
  onSort: (column: DashboardSortColumn) => void;
  onOpenApplication: (applicationId: string) => void;
};

export function DashboardApplicationList({
  applications,
  activeApplicationId,
  pendingActions,
  sortState,
  listRef,
  timeZoneId,
  onSort,
  onOpenApplication,
}: DashboardApplicationListProps) {
  return (
    <>
      <div className="hidden shrink-0 border-b border-[var(--border)] bg-[var(--surface-strong)] px-4 py-2.5 md:grid md:items-center md:gap-3 md:[grid-template-columns:minmax(0,1.2fr)_8rem_minmax(0,1.6fr)_6rem]">
        <SortHeader column="company" sortState={sortState} onSort={onSort} />
        <SortHeader column="stage" sortState={sortState} onSort={onSort} />
        <SortHeader column="next-action" sortState={sortState} onSort={onSort} />
        <SortHeader
          column="priority"
          align="right"
          sortState={sortState}
          onSort={onSort}
        />
      </div>
      <div
        ref={listRef}
        className="workspace-scroll-region fill-rows min-h-0 flex-1 divide-y divide-[var(--border)] overflow-x-hidden max-md:space-y-3 max-md:divide-y-0 max-md:p-3"
      >
        {applications.map((application) => (
          <ApplicationRow
            key={application.id}
            application={application}
            selected={activeApplicationId === application.id}
            pendingActions={pendingActions}
            timeZoneId={timeZoneId}
            onOpenApplication={onOpenApplication}
          />
        ))}
      </div>
    </>
  );
}

function ApplicationRow({
  application,
  selected,
  pendingActions,
  timeZoneId,
  onOpenApplication,
}: {
  application: ApplicationCard;
  selected: boolean;
  pendingActions: PendingAction[];
  timeZoneId: string;
  onOpenApplication: (applicationId: string) => void;
}) {
  const nextAction = deriveNextAction(application, pendingActions);
  const priority = describePriority(application);
  const parserError = application.requiresReview;
  // Relative times depend on the current time, so they appear once the page has hydrated.
  const hydrated = useHydrated();
  const dueRelative = hydrated ? relativeFromNow(nextAction.due) : null;
  const duePill = hydrated ? getDuePill(nextAction.due) : null;

  return (
    <button
      type="button"
      aria-pressed={selected}
      className={`grid w-full min-w-0 items-start gap-3 px-4 py-2.5 text-left transition hover:bg-[var(--surface-overlay)] max-md:rounded-md max-md:border max-md:border-[var(--border)] max-md:bg-[var(--surface-strong)] max-md:p-3 md:items-center md:gap-3 md:[grid-template-columns:minmax(0,1.2fr)_8rem_minmax(0,1.6fr)_6rem] ${
        selected ? "bg-[var(--surface)] shadow-[inset_4px_0_0_var(--accent)]" : ""
      }`}
      onClick={() => onOpenApplication(application.id)}
    >
      <span className="block min-w-0">
        <span className="flex items-center gap-1.5 truncate text-sm font-medium text-[var(--text-primary)]">
          <span className="truncate">{application.company}</span>
          {parserError ? (
            <span
              className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]"
              title="The company or role looks misread. Open it to fix."
            >
              Fix
            </span>
          ) : null}
        </span>
        <span className="mt-0.5 block truncate text-xs text-[var(--text-tertiary)]">
          {hasUnknownRole(application) ? "Role pending" : application.role}
        </span>
      </span>
      <span className="block min-w-0">
        <StatusPill status={application.status} />
      </span>
      <span className="block min-w-0">
        <span className={`block truncate text-sm ${nextActionToneClass(nextAction.tone)}`}>
          {nextAction.text}
        </span>
        {nextAction.due ? (
          <span className="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
            <span className="block truncate text-xs text-[var(--text-tertiary)]">
              Due {dueRelative ?? formatTimestamp(nextAction.due, timeZoneId)}
            </span>
            {duePill ? (
              <span className={`badge px-1.5 py-0.5 text-xs ${duePill.className}`}>
                {duePill.label}
              </span>
            ) : null}
          </span>
        ) : null}
      </span>
      <span className="flex md:justify-end">
        <span className={`badge ${priority.toneClass}`} title={priority.reason}>
          {priority.label}
        </span>
      </span>
    </button>
  );
}

function SortHeader({
  column,
  align = "left",
  sortState,
  onSort,
}: {
  column: DashboardSortColumn;
  align?: "left" | "right";
  sortState: {
    column: DashboardSortColumn | null;
    direction: DashboardSortDirection;
  };
  onSort: (column: DashboardSortColumn) => void;
}) {
  return (
    <SortableHeaderButton
      column={column}
      label={SORT_LABEL[column]}
      activeColumn={sortState.column}
      direction={sortState.direction}
      align={align}
      onSort={onSort}
    />
  );
}


function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(
    value,
    timeZoneId,
    "Waiting for first sync",
  );
}

function relativeFromNow(value: string | null) {
  if (!value) return null;
  const target = new Date(value).getTime();
  if (Number.isNaN(target)) return null;
  const diffMs = target - Date.now();
  const diffDays = Math.round(diffMs / (1000 * 60 * 60 * 24));
  if (diffDays === 0) return "today";
  if (diffDays === 1) return "tomorrow";
  if (diffDays === -1) return "yesterday";
  if (diffDays > 0 && diffDays <= 14) return `in ${diffDays}d`;
  if (diffDays < 0 && diffDays >= -30) return `${Math.abs(diffDays)}d ago`;
  return null;
}

function getDuePill(value: string | null) {
  if (!value) return null;
  const target = new Date(value).getTime();
  if (Number.isNaN(target)) return null;

  const diffHours = (target - Date.now()) / (1000 * 60 * 60);
  if (diffHours < 0) {
    return {
      label: "Overdue",
      className: "bg-[var(--red-soft)] text-[var(--red)]",
    };
  }
  if (diffHours <= 24) {
    return {
      label: "Due today",
      className: "bg-[var(--red-soft)] text-[var(--red)]",
    };
  }
  if (diffHours <= 72) {
    return {
      label: "Due soon",
      className: "bg-[var(--yellow-soft)] text-[var(--yellow)]",
    };
  }
  return null;
}

function hasUnknownRole(application: ApplicationCard) {
  return parserHasUnknownRole(application.role);
}
