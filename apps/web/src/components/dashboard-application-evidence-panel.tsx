"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { StatusPill } from "@/components/status-pill";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import { hasUnknownRole as parserHasUnknownRole } from "@/lib/parser-quality";
import type {
  ApplicationCard,
  DashboardResponse,
  PendingAction,
  RecentActivity,
} from "@/lib/dashboard";
import { deriveNextAction, describePriority, nextActionToneClass } from "@/lib/application-display";

type DashboardApplicationEvidencePanelProps = {
  application: ApplicationCard;
  dashboard: DashboardResponse;
  timeZoneId: string;
  pendingCompletionKey: string | null;
  actionNotice: string | null;
  actionError: string | null;
  onCompleteAction: (
    application: ApplicationCard,
    reminderId: string | undefined,
    completesStage: boolean,
  ) => void;
};

export function DashboardEmptySelectedPanel({ viewLabel }: { viewLabel: string }) {
  return (
    <p className="text-sm text-[var(--text-tertiary)]">Nothing in {viewLabel}.</p>
  );
}

function isAssessmentDeadline(reminder: PendingAction) {
  return reminder.source === "Assessment Deadline";
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
  const linkedReminders = dashboard.pendingActions
    .filter((reminder) => reminder.jobApplicationId === application.id)
    .filter((reminder) => reminder.status !== "Completed");
  const primaryCompletableReminder = nextAction.reminder ?? linkedReminders[0];
  // The dashboard carries only its first six open reminders; the next action names its reminder
  // even when that one is not among them.
  const primaryReminderId =
    primaryCompletableReminder?.id ?? application.nextAction.sourceReminderId ?? undefined;
  const canCompleteNextAction =
    nextAction.tone === "action" &&
    (Boolean(primaryReminderId) || application.status === "Oa Pending");
  const primaryCompletionKey = `${application.id}:${primaryReminderId ?? "stage"}:Completed`;
  // Finishing the assessment reminder, or the stage itself when no task is attached, moves an
  // assessment stage back to Applied; any other task leaves it. The reminder's own type decides:
  // taskType also says "assessment" for a follow-up whose evidence email is the OA invitation. A
  // reminder outside the dashboard's six has no known type, so it leaves the stage too.
  const primaryCompletesStage = primaryCompletableReminder
    ? isAssessmentDeadline(primaryCompletableReminder)
    : !primaryReminderId;
  const completingPrimaryAction = pendingCompletionKey === primaryCompletionKey;
  const otherReminders = linkedReminders.filter(
    (reminder) => reminder.id !== primaryCompletableReminder?.id,
  );
  const relatedActivity = dashboard.recentActivity
    .filter((activity) => activity.jobApplicationId === application.id)
    .sort(
      (left, right) =>
        new Date(right.occurredAtUtc).getTime() -
        new Date(left.occurredAtUtc).getTime(),
    );

  return (
    <div className="space-y-3">
      <div>
        <h2 className="text-base font-semibold text-[var(--text-primary)]">
          {application.company}
        </h2>
        <p className="text-xs text-[var(--text-tertiary)]">
          {parserHasUnknownRole(application.role) ? "Role pending" : application.role}
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          <StatusPill status={application.status} />
          <span className={`badge ${priority.toneClass}`} title={priority.reason}>
            {priority.label}
          </span>
          {parserError ? (
            <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">Review</span>
          ) : null}
          {application.hasOutboundReply ? (
            <span className="badge bg-[var(--green-soft)] text-[var(--green)]">Replied</span>
          ) : null}
        </div>
      </div>

      <div className="info-tile">
        <p className={`text-sm font-medium ${nextActionToneClass(nextAction.tone)}`}>
          {nextAction.text}
        </p>
        {nextAction.due ? (
          <p className="mt-0.5 text-xs text-[var(--text-tertiary)]">
            Due {formatTimestamp(nextAction.due, timeZoneId)}
          </p>
        ) : null}
        {canCompleteNextAction ? (
          <button
            type="button"
            className="btn btn-secondary btn-sm mt-2"
            disabled={pendingCompletionKey !== null}
            onClick={() => onCompleteAction(application, primaryReminderId, primaryCompletesStage)}
          >
            {completingPrimaryAction ? (
              <>
                <span className="spinner" /> Saving
              </>
            ) : (
              "Mark done"
            )}
          </button>
        ) : null}
        {actionNotice ? (
          <p className="mt-2 text-xs font-medium text-[var(--green)]">{actionNotice}</p>
        ) : null}
        {actionError ? (
          <p className="mt-2 text-xs font-medium text-[var(--red)]">{actionError}</p>
        ) : null}
      </div>

      <div className="flex flex-wrap gap-2">
        <Link href={`/app/applications/${application.id}`} className="btn btn-primary btn-sm">
          Open
        </Link>
        {application.gmailUrl ? (
          <a
            href={application.gmailUrl}
            target="_blank"
            rel="noreferrer"
            className="btn btn-secondary btn-sm"
          >
            Gmail
          </a>
        ) : null}
        {parserError ? (
          <Link href="/app/review" className="btn btn-secondary btn-sm">
            Fix in review
          </Link>
        ) : null}
      </div>

      {otherReminders.length > 0 ? (
        <EvidenceList title="Other tasks">
          {otherReminders.map((reminder) => (
            <li key={reminder.id} className="flex items-baseline justify-between gap-3 py-1.5">
              <span className="min-w-0 text-sm text-[var(--text-secondary)]">{reminder.title}</span>
              <button
                type="button"
                className="shrink-0 text-xs font-medium text-[var(--text-tertiary)] hover:text-[var(--text-primary)] hover:underline disabled:opacity-50 max-sm:min-h-6"
                disabled={pendingCompletionKey !== null}
                onClick={() => onCompleteAction(application, reminder.id, isAssessmentDeadline(reminder))}
              >
                {pendingCompletionKey === `${application.id}:${reminder.id}:Completed` ? "…" : "Done"}
              </button>
            </li>
          ))}
        </EvidenceList>
      ) : null}

      {relatedActivity.length > 0 ? (
        <EvidenceList title="Evidence">
          {relatedActivity.slice(0, 4).map((activity) => (
            <li key={activity.id} className="py-1.5">
              <p className="text-sm text-[var(--text-secondary)]">{humanizeRecentActivity(activity)}</p>
              <p className="text-xs text-[var(--text-tertiary)]">
                {formatTimestamp(activity.occurredAtUtc, timeZoneId)}
              </p>
            </li>
          ))}
        </EvidenceList>
      ) : null}
    </div>
  );
}

function EvidenceList({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="border-t border-[var(--border)] pt-2">
      <h3 className="text-sm font-semibold text-[var(--text-primary)]">{title}</h3>
      <ul className="mt-1 divide-y divide-[var(--border)]">{children}</ul>
    </div>
  );
}

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "Waiting for first sync");
}


function humanizeRecentActivity(activity: RecentActivity) {
  return activity.description.trim();
}
