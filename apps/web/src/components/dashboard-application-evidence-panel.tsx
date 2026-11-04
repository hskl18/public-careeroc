"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { StatusPill } from "@/components/status-pill";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import { hasUnknownRole as parserHasUnknownRole } from "@/lib/parser-quality";
import type {
  ApplicationCard,
  ApplicationLatestEvidence,
  DashboardResponse,
  PendingAction,
  RecentActivity,
} from "@/lib/dashboard";

type DerivedNextAction = {
  text: string;
  due: string | null;
  tone: "action" | "waiting" | "review" | "muted";
  reminder?: PendingAction;
};

type PriorityMeta = {
  label: string;
  toneClass: string;
  reason: string;
};

type EvidencePreview = {
  summary: string;
  subject?: string | null;
  sender?: string | null;
  snippet?: string | null;
  category: string;
  occurredAtUtc: string;
};

type DashboardApplicationEvidencePanelProps = {
  application: ApplicationCard;
  dashboard: DashboardResponse;
  timeZoneId: string;
  pendingCompletionKey: string | null;
  actionNotice: string | null;
  actionError: string | null;
  onCompleteAction: (
    application: ApplicationCard,
    reminder?: PendingAction,
  ) => void;
};

export function DashboardEmptySelectedPanel({ viewLabel }: { viewLabel: string }) {
  return (
    <div className="flex min-h-full flex-col">
      <div>
        <p className="eyebrow">Selected</p>
        <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
          No application selected
        </h2>
        <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
          {viewLabel} has no matching applications right now.
        </p>
      </div>

      <div className="mt-4 rounded-md border border-dashed border-[var(--border)] bg-[rgba(255,255,255,0.32)] p-4">
        <p className="text-sm leading-6 text-[var(--text-tertiary)]">
          Select another filter or wait for new synced evidence to populate this
          panel.
        </p>
      </div>
    </div>
  );
}

export function DashboardApplicationEvidencePanel({
  application,
  dashboard,
  timeZoneId,
  pendingCompletionKey,
  actionNotice,
  actionError,
  onCompleteAction,
}: DashboardApplicationEvidencePanelProps) {
  const nextAction = deriveNextAction(application, dashboard.pendingActions);
  const priority = describePriority(application);
  const parserError = application.requiresReview;
  const evidence = getEvidenceForApplication(application);
  const evidenceCopy = humanizeEvidence(application, evidence);
  const [evidenceExpanded, setEvidenceExpanded] = useState(false);
  const evidenceIsLong = evidenceCopy.length > 240;
  const linkedReminders = dashboard.pendingActions
    .filter((reminder) => reminder.jobApplicationId === application.id)
    .filter((reminder) => reminder.status !== "Completed");
  const primaryCompletableReminder = nextAction.reminder ?? linkedReminders[0];
  const canCompleteNextAction =
    nextAction.tone === "action" &&
    (Boolean(primaryCompletableReminder) || application.status === "Oa Pending");
  const primaryCompletionKey = `${application.id}:${
    primaryCompletableReminder?.id ?? "stage"
  }:Completed`;
  const completingPrimaryAction = pendingCompletionKey === primaryCompletionKey;
  const relatedActivity = dashboard.recentActivity
    .filter((activity) => activity.jobApplicationId === application.id)
    .sort(
      (left, right) =>
        new Date(right.occurredAtUtc).getTime() -
        new Date(left.occurredAtUtc).getTime(),
    );

  return (
    <div className="space-y-4">
      <div>
        <p className="eyebrow">Selected</p>
        <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
          {application.company}
        </h2>
        <p className="mt-1 text-sm leading-6 text-[var(--text-secondary)]">
          {parserHasUnknownRole(application.role) ? "Role pending" : application.role}
        </p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <StatusPill status={application.status} />
          <span className={`badge ${priority.toneClass}`} title={priority.reason}>
            {priority.label}
          </span>
          {parserError ? (
            <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
              Review
            </span>
          ) : null}
          {application.hasOutboundReply ? (
            <span className="badge bg-[var(--green-soft)] text-[var(--green)]">
              Replied
            </span>
          ) : null}
        </div>
      </div>

      <div className="info-tile">
        <p className="eyebrow">Next action</p>
        <p className={`mt-2 text-sm font-medium ${getNextActionToneClass(nextAction.tone)}`}>
          {nextAction.text}
        </p>
        {nextAction.due ? (
          <p className="mt-1 text-[11px] text-[var(--text-tertiary)]">
            Due {formatTimestamp(nextAction.due, timeZoneId)}
          </p>
        ) : null}
        <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
          Priority: {priority.label} - {priority.reason}
        </p>
        {canCompleteNextAction ? (
          <button
            type="button"
            className="btn btn-secondary btn-block btn-sm mt-3"
            disabled={pendingCompletionKey !== null}
            onClick={() =>
              onCompleteAction(application, primaryCompletableReminder)
            }
            title="Use after you have completed this task outside careeroc."
          >
            {completingPrimaryAction ? (
              <>
                <span className="spinner" /> Saving
              </>
            ) : application.status === "Oa Pending" ? (
              "Mark assessment done"
            ) : (
              "Mark task done"
            )}
          </button>
        ) : null}
        {actionNotice ? (
          <p className="mt-2 text-[11px] font-medium text-[var(--green)]">
            {actionNotice}
          </p>
        ) : null}
        {actionError ? (
          <p className="mt-2 text-[11px] font-medium text-[var(--red)]">
            {actionError}
          </p>
        ) : null}
      </div>

      <div className="info-tile">
        <p className="eyebrow">Decision evidence</p>
        <p
          className={`mt-2 text-sm leading-6 text-[var(--text-primary)] ${
            evidenceIsLong && !evidenceExpanded
              ? "line-clamp-4 [display:-webkit-box] [-webkit-box-orient:vertical] overflow-hidden"
              : ""
          }`}
        >
          {evidenceCopy}
        </p>
        {evidenceIsLong ? (
          <button
            type="button"
            className="mt-1 text-[11px] font-medium text-[var(--accent)] hover:underline"
            onClick={() => setEvidenceExpanded((value) => !value)}
          >
            {evidenceExpanded ? "Show less" : "Show more"}
          </button>
        ) : null}
        {evidence ? (
          <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
            {evidence.category} ·{" "}
            {formatTimestamp(evidence.occurredAtUtc, timeZoneId)}
          </p>
        ) : null}
      </div>

      <div className="grid gap-2">
        <Link
          href={`/app/applications/${application.id}`}
          className="btn btn-primary btn-block btn-sm"
        >
          Open detail
        </Link>
        {application.gmailUrl ? (
          <a
            href={application.gmailUrl}
            target="_blank"
            rel="noreferrer"
            className="btn btn-secondary btn-block btn-sm"
          >
            Open Gmail thread
          </a>
        ) : null}
        {parserError ? (
          <Link href="/app/review" className="btn btn-secondary btn-block btn-sm">
            Fix in Email review
          </Link>
        ) : null}
      </div>

      <EvidenceList
        title="Open tasks"
        count={linkedReminders.length}
        empty="No open task for this record."
      >
        {linkedReminders.map((reminder) => (
          <article key={reminder.id} className="info-tile">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {reminder.title}
            </p>
            <p className="mt-1 text-[11px] text-[var(--text-tertiary)]">
              {reminder.hasExplicitDueDate
                ? `Due ${formatTimestamp(reminder.dueAtUtc, timeZoneId)}`
                : "Unknown due date"}{" "}
              · {reminder.source}
            </p>
            <button
              type="button"
              className="mt-3 text-xs font-medium text-[var(--text-tertiary)] underline-offset-2 transition-colors hover:text-[var(--text-primary)] hover:underline disabled:opacity-50"
              disabled={pendingCompletionKey !== null}
              onClick={() => onCompleteAction(application, reminder)}
              title="Mark this open task as completed."
            >
              {pendingCompletionKey === `${application.id}:${reminder.id}:Completed` ? (
                <>
                  <span className="spinner" /> Saving
                </>
              ) : (
                "Mark done"
              )}
            </button>
          </article>
        ))}
      </EvidenceList>

      <EvidenceList
        title="Recent evidence"
        count={relatedActivity.length}
        empty="No recent event details for this record."
      >
        {relatedActivity.slice(0, 4).map((activity) => (
          <article key={activity.id} className="info-tile">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              {humanizeRecentActivity(activity)}
            </p>
            <p className="mt-1 text-[11px] text-[var(--text-tertiary)]">
              {activity.category} · {formatTimestamp(activity.occurredAtUtc, timeZoneId)}
            </p>
          </article>
        ))}
      </EvidenceList>
    </div>
  );
}

function EvidenceList({
  title,
  count,
  empty,
  children,
}: {
  title: string;
  count: number;
  empty: string;
  children: ReactNode;
}) {
  return (
    <div className="border-t border-[var(--border)] pt-4">
      <div className="flex items-center justify-between gap-3">
        <p className="eyebrow">{title}</p>
        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {count}
        </span>
      </div>
      {count === 0 ? (
        <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
          {empty}
        </p>
      ) : (
        <div className="mt-3 space-y-2">{children}</div>
      )}
    </div>
  );
}

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "Waiting for first sync");
}

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

function describePriority(application: ApplicationCard): PriorityMeta {
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

function getNextActionToneClass(tone: DerivedNextAction["tone"]) {
  switch (tone) {
    case "action":
      return "text-[var(--accent)]";
    case "review":
      return "text-[var(--yellow)]";
    case "waiting":
      return "text-[var(--green)]";
    default:
      return "text-[var(--text-primary)]";
  }
}

function toEvidencePreview(
  evidence: ApplicationLatestEvidence,
): EvidencePreview {
  return {
    summary: evidence.summary,
    subject: evidence.subject,
    sender: evidence.sender,
    snippet: evidence.snippet,
    category: evidence.category,
    occurredAtUtc: evidence.occurredAtUtc,
  };
}

function getEvidenceForApplication(application: ApplicationCard) {
  return application.latestEvidence
    ? toEvidencePreview(application.latestEvidence)
    : null;
}

function humanizeEvidence(
  application: ApplicationCard,
  evidence: EvidencePreview | null,
) {
  if (!evidence) {
    return `No decision evidence attached yet for ${application.company}.`;
  }
  if (evidence.summary) {
    return evidence.summary;
  }
  return `Decision evidence attached for ${application.company}.`;
}

function humanizeRecentActivity(activity: RecentActivity) {
  return activity.description.trim();
}
