"use client";

import type { RefObject } from "react";
import { StatusPill } from "@/components/status-pill";
import { SortableHeaderButton } from "@/components/workspace-controls";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import { hasUnknownRole as parserHasUnknownRole } from "@/lib/parser-quality";
import type { ApplicationCard, PendingAction } from "@/lib/dashboard";
import { useHydrated } from "@/lib/use-hydrated";

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
  priority: "Priority",
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
      <div className="hidden shrink-0 border-b border-[var(--border)] bg-[rgba(255,255,255,0.32)] px-4 py-2.5 md:grid md:items-center md:gap-3 md:[grid-template-columns:minmax(10rem,1fr)_minmax(10rem,1fr)_minmax(10.5rem,0.9fr)_minmax(12rem,1.2fr)_6.5rem]">
        <SortHeader column="company" sortState={sortState} onSort={onSort} />
        <SortHeader column="role" sortState={sortState} onSort={onSort} />
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
        className="workspace-scroll-region min-h-0 flex-1 divide-y divide-[var(--border)] overflow-x-hidden max-md:space-y-3 max-md:divide-y-0 max-md:p-3"
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
  const lastActivityRelative = hydrated ? relativeFromNow(application.lastActivityAtUtc) : null;

  return (
    <button
      type="button"
      aria-pressed={selected}
      aria-label={`Open or select ${application.company} application`}
      className={`grid w-full min-w-0 items-start gap-3 px-4 py-3.5 text-left transition hover:bg-[rgba(255,255,255,0.55)] max-md:rounded-md max-md:border max-md:border-[var(--border)] max-md:bg-white/55 max-md:p-3 md:items-center md:gap-3 md:[grid-template-columns:minmax(10rem,1fr)_minmax(10rem,1fr)_minmax(10.5rem,0.9fr)_minmax(12rem,1.2fr)_6.5rem] ${
        selected ? "bg-[var(--surface)] shadow-[inset_4px_0_0_var(--accent)]" : ""
      }`}
      onClick={() => onOpenApplication(application.id)}
    >
      <div className="min-w-0">
        <p className="flex items-center gap-1.5 truncate text-sm font-semibold text-[var(--text-primary)]">
          <span className="truncate">{application.company}</span>
          {parserError ? (
            <span
              className="badge border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]"
              title="Looks like a parser error - open Review queue to fix."
            >
              Review
            </span>
          ) : null}
        </p>
        <p className="mt-1 truncate text-[11px] text-[var(--text-tertiary)]">
          {application.updatedFrom?.toLowerCase() === "email"
            ? `Received ${formatTimestamp(application.receivedAtUtc, timeZoneId)}`
            : `${application.updatedFrom} · ${formatTimestamp(application.receivedAtUtc, timeZoneId)}`}
        </p>
      </div>
      <div className="min-w-0">
        <p
          className={`truncate text-sm font-medium ${
            hasUnknownRole(application)
              ? "text-[var(--brand-blue-ink)]"
              : "text-[var(--text-secondary)]"
          }`}
        >
          {hasUnknownRole(application) ? "Role pending" : application.role}
        </p>
        <p className="mt-1 truncate text-[11px] text-[var(--text-tertiary)]">
          Last activity{" "}
          {lastActivityRelative ??
            formatTimestamp(application.lastActivityAtUtc, timeZoneId)}
          {application.hasOutboundReply ? (
            <span className="ml-2 text-[var(--green)]">· Replied</span>
          ) : null}
        </p>
      </div>
      <div className="min-w-0">
        <StatusPill status={application.status} />
      </div>
      <div className="min-w-0">
        <p className={`truncate text-sm font-medium ${nextActionToneClass(nextAction.tone)}`}>
          {nextAction.text}
        </p>
        {nextAction.due ? (
          <div className="mt-1 flex min-w-0 flex-wrap items-center gap-1.5">
            <p className="truncate text-[11px] text-[var(--text-tertiary)]">
              Due {dueRelative ?? formatTimestamp(nextAction.due, timeZoneId)}
            </p>
            {duePill ? (
              <span className={`badge px-1.5 py-0.5 text-[0.6rem] ${duePill.className}`}>
                {duePill.label}
              </span>
            ) : null}
          </div>
        ) : null}
      </div>
      <div className="flex md:justify-end">
        <span className={`badge ${priority.toneClass}`} title={priority.reason}>
          {priority.label}
        </span>
      </div>
      <span className="text-[11px] font-medium text-[var(--accent)] md:hidden">
        Open detail →
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

type DerivedNextAction = {
  text: string;
  due: string | null;
  tone: "action" | "waiting" | "review" | "muted";
  reminder?: PendingAction;
};

function deriveNextAction(
  application: ApplicationCard,
  reminders: PendingAction[],
): DerivedNextAction {
  return {
    text: application.nextAction.title,
    due: application.nextAction.dueAtUtc,
    tone: application.nextAction.tone,
    reminder:
      reminders.find(
        (reminder) => reminder.id === application.nextAction.sourceReminderId,
      ) ?? undefined,
  };
}

function describePriority(application: ApplicationCard) {
  const label = application.priorityLabel;
  return {
    label,
    toneClass: getPriorityToneClass(label),
    reason: application.priorityReason,
  };
}

function getPriorityToneClass(label: string) {
  const normalized = label.toLowerCase();
  if (normalized === "urgent") {
    return "bg-[var(--red-soft)] text-[var(--red-strong)]";
  }
  if (normalized === "high") {
    return "bg-[var(--yellow-soft)] text-[var(--yellow)]";
  }
  if (normalized === "medium") {
    return "bg-[var(--blue-soft)] text-[var(--blue)]";
  }
  if (normalized === "low") {
    return "bg-[var(--surface-overlay)] text-[var(--text-secondary)]";
  }
  return "bg-[var(--surface-overlay)] text-[var(--text-tertiary)]";
}

function nextActionToneClass(tone: DerivedNextAction["tone"]) {
  switch (tone) {
    case "action":
      return "text-[var(--accent)]";
    case "review":
      return "text-[var(--brand-blue-ink)]";
    case "waiting":
      return "text-[var(--green)]";
    default:
      return "text-[var(--text-secondary)]";
  }
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
