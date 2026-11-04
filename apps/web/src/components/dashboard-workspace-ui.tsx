import type { Route } from "next";
import Link from "next/link";
import type {
  ApplicationCard,
  DashboardResponse,
  PendingAction,
} from "@/lib/dashboard";
import type { InboxAccountSetup } from "@/lib/inbox-accounts";
import { looksLikeParserError } from "@/lib/parser-quality";

export type SnoozePreset = "1h" | "tomorrow" | "next-week";

export function ApplicationsOverviewSection({
  dashboard,
  coldStart,
}: {
  dashboard: DashboardResponse;
  coldStart: boolean;
}) {
  const metrics = [
    {
      label: "Needs action",
      focus: "needs-action",
      value: dashboard.metrics.needsAction,
      color: "var(--accent)",
      tooltip: "Open reminders and deadlines waiting on you.",
      highlight: true,
    },
    {
      label: "Waiting",
      focus: "waiting",
      value: dashboard.metrics.waitingOnEmployer,
      color: "var(--green)",
      tooltip: "You replied. Now waiting on the employer.",
    },
    {
      label: "Needs review",
      focus: "needs-review",
      value: dashboard.metrics.needsReview,
      color: "var(--brand-blue-ink)",
      tooltip: "Applications with linked evidence that needs manual review.",
    },
    {
      label: "Interviews",
      focus: "interviewing",
      value: dashboard.metrics.interviews,
      color: "var(--blue)",
      tooltip: "In interview scheduling or interview execution.",
    },
    {
      label: "Offers",
      focus: "offer",
      value: dashboard.metrics.offers,
      color: "var(--green)",
      tooltip: "Reached an offer stage.",
    },
    {
      label: "Active",
      focus: "active",
      value: dashboard.metrics.activeApplications,
      color: "var(--text-primary)",
      tooltip: "Open applications still moving through the pipeline.",
    },
    {
      label: "Rejected",
      focus: "rejected",
      value: dashboard.metrics.rejections,
      color: "var(--red)",
      tooltip: "Closed by the employer. Hidden from the active view.",
    },
  ];
  const headline = coldStart
    ? "Connect Gmail to start the loop"
    : "Your recruiting command center";

  return (
    <section className="card app-workspace-panel p-3 sm:p-4">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex min-w-0 items-start gap-4">
          <div className="min-w-0">
            <p className="eyebrow">Today</p>
            <h1 className="mt-1 text-base font-semibold text-[var(--text-primary)] sm:mt-2 sm:text-xl">
              {headline}
            </h1>
          </div>
        </div>
        <div className="flex w-full flex-col gap-2 sm:w-auto sm:flex-row sm:items-center sm:justify-end">
          {coldStart ? (
            <Link href="/app/inbox#connect-inbox" className="btn btn-secondary btn-sm justify-center">
              Set up Gmail
            </Link>
          ) : (
            <Link href="/app/applications?view=needs-action" className="btn btn-primary btn-sm justify-center max-lg:min-h-9">
              <span className="lg:hidden">Today’s queue</span>
              <span className="hidden lg:inline">Open today’s queue</span>
            </Link>
          )}
          {dashboard.metrics.reviewQueue > 0 ? (
            <Link
              href="/app/review"
              className="inline-flex min-h-9 items-center justify-center gap-2 rounded-md border border-[var(--border)] bg-white/70 px-3 font-mono text-[0.64rem] font-semibold uppercase tracking-[0.08em] text-[var(--text-primary)] transition hover:border-[var(--brand-blue)]/35 hover:bg-white"
            >
              Email review{" "}
              <span className="rounded-full border border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] px-2 py-0.5 text-[0.64rem] text-[var(--brand-blue-ink)]">
                {dashboard.metrics.reviewQueue}
              </span>
            </Link>
          ) : null}
        </div>
      </div>

      <div className="mt-3 grid grid-cols-[repeat(2,minmax(0,1fr))] gap-2 sm:mt-4 sm:grid-cols-[repeat(3,minmax(0,1fr))] xl:grid-cols-[repeat(7,minmax(0,1fr))]">
        {metrics.map((metric) =>
          coldStart ? (
            <div
              key={metric.label}
              className="flex min-w-0 items-center justify-between gap-2 rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2 opacity-70 sm:block sm:py-2.5"
              aria-disabled="true"
              title={metric.tooltip}
            >
              <p className="min-w-0 truncate font-mono text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-[var(--text-tertiary)] lg:text-[0.72rem] lg:tracking-[0.16em]">{metric.label}</p>
              <p
                className="text-base font-semibold text-[var(--text-tertiary)] sm:mt-1 sm:text-lg"
                aria-label="Waiting for first sync"
              >
                &mdash;
              </p>
            </div>
          ) : (
            <Link
              key={metric.label}
              href={`/app/applications?view=${metric.focus}`}
              title={metric.tooltip}
              className={`flex min-w-0 items-center justify-between gap-2 rounded-md border px-3 py-2 transition hover:border-[var(--border-hover)] hover:bg-[rgba(255,255,255,0.62)] sm:block sm:py-2.5 ${
                metric.highlight && (metric.value ?? 0) > 0
                  ? "border-[var(--accent-soft)] bg-[rgba(255,255,255,0.62)] ring-1 ring-[var(--accent-soft)]"
                  : "border-[var(--border)] bg-[rgba(255,255,255,0.42)]"
              }`}
            >
              <p className="min-w-0 truncate font-mono text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-[var(--text-tertiary)] lg:text-[0.72rem] lg:tracking-[0.16em]">{metric.label}</p>
              <p
                className="text-base font-semibold tabular-nums sm:mt-1 sm:text-lg"
                style={{
                  color:
                    metric.value === null || metric.value === 0
                      ? "var(--text-tertiary)"
                      : metric.color,
                }}
              >
                {metric.value ?? "—"}
              </p>
            </Link>
          ),
        )}
      </div>
    </section>
  );
}

type TaskTypeMeta = {
  label: string;
  toneClass: string;
  primaryActionLabel: string;
  reason: string;
};

function describeTaskType(action: PendingAction): TaskTypeMeta {
  switch (action.taskType) {
    case "assessment":
      return {
        label: "OA",
        toneClass: "bg-[var(--violet-soft)] text-[var(--violet)]",
        primaryActionLabel: action.gmailUrl ? "Open OA email" : "Open detail",
        reason: "An OA invitation is waiting for a response.",
      };
    case "interview":
      return {
        label: "Interview",
        toneClass: "bg-[var(--accent-soft)] text-[var(--accent-ink)]",
        primaryActionLabel: action.gmailUrl ? "Open interview email" : "Open detail",
        reason: "Interview logistics need confirmation or prep.",
      };
    case "offer":
      return {
        label: "Offer",
        toneClass: "bg-[var(--green-soft)] text-[var(--green)]",
        primaryActionLabel: action.gmailUrl ? "Open offer email" : "Open detail",
        reason: "Offer awaiting your reply.",
      };
    case "reply":
      return {
        label: "Reply",
        toneClass: "bg-[var(--blue-soft)] text-[var(--blue)]",
        primaryActionLabel: action.gmailUrl ? "Open Gmail" : "Open detail",
        reason: "A recruiter is waiting on a reply.",
      };
    case "follow_up":
      return {
        label: "Follow up",
        toneClass: "bg-[var(--blue-soft)] text-[var(--blue)]",
        primaryActionLabel: action.gmailUrl ? "Open Gmail" : "Open detail",
        reason: "Promised follow-up still owed.",
      };
    default:
      return {
        label: "Task",
        toneClass: "bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]",
        primaryActionLabel: action.gmailUrl ? "Open Gmail" : "Open detail",
        reason: "Open follow-up still active.",
      };
  }
}

function describeDeadline(
  action: PendingAction,
  formatDateTime: (value: string | null) => string,
) {
  if (action.deadlineFound) {
    return {
      label: `Due ${formatDateTime(action.dueAtUtc)}`,
      tone: "text-[var(--accent)]",
    };
  }
  const fallback =
    action.deadlineHint ??
    (action.receivedAtUtc
      ? `No deadline found · received ${formatDateTime(action.receivedAtUtc)}`
      : "No deadline found in the email");

  return {
    label: fallback,
    tone: "text-[var(--text-tertiary)]",
  };
}

export function getSnoozeUntilUtc(preset: SnoozePreset) {
  const now = new Date();
  switch (preset) {
    case "1h":
      return new Date(now.getTime() + 60 * 60 * 1000).toISOString();
    case "tomorrow":
      return new Date(now.getTime() + 24 * 60 * 60 * 1000).toISOString();
    case "next-week":
      return new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000).toISOString();
  }
}

export function getSnoozePresetLabel(preset: SnoozePreset) {
  switch (preset) {
    case "1h":
      return "1h";
    case "tomorrow":
      return "Tomorrow";
    case "next-week":
      return "Next week";
  }
}

export function AttentionSection({
  liveData,
  canPersistReminders,
  pendingActions,
  applications,
  attentionAccounts,
  pendingReminderKey,
  coldStart,
  formatDateTime,
  onReminderAction,
  onReminderSnooze,
}: {
  liveData: boolean;
  canPersistReminders: boolean;
  pendingActions: PendingAction[];
  applications: ApplicationCard[];
  attentionAccounts: InboxAccountSetup[];
  pendingReminderKey: string | null;
  coldStart: boolean;
  formatDateTime: (value: string | null) => string;
  onReminderAction: (
    action: PendingAction,
    status: "Completed" | "Dismissed",
  ) => void;
  onReminderSnooze: (action: PendingAction, preset: SnoozePreset) => void;
}) {
  const subListEmpty =
    pendingActions.length === 0 && attentionAccounts.length === 0;
  const isCompletelyEmpty = subListEmpty;

  return (
    <section className="card app-workspace-panel flex h-full min-h-0 flex-col p-0">
      <div className="shrink-0 px-5 pt-5">
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="eyebrow">Needs attention</p>
            <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
              What needs action now
            </h2>
          </div>
          <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
            {pendingActions.length + attentionAccounts.length}
          </span>
        </div>
      </div>

      {coldStart && isCompletelyEmpty ? (
        <div className="px-5 pt-4 pb-5">
          <div className="card-elevated p-5">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              Reminders and follow-ups land here
            </p>
            <p className="mt-1 text-xs leading-6 text-[var(--text-tertiary)]">
              After your first sync, this card surfaces assessment deadlines,
              interviews to confirm, and recruiter replies you owe.
            </p>
            <div className="mt-4">
              <Link
                href="/app/inbox#connect-inbox"
                className="btn btn-primary btn-sm"
              >
                Connect Gmail
              </Link>
            </div>
          </div>
        </div>
      ) : isCompletelyEmpty ? (
        <div className="px-5 pt-4 pb-5">
          <div className="card-elevated p-6">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              Nothing is blocked right now
            </p>
            <p className="mt-1 text-xs leading-6 text-[var(--text-tertiary)]">
              New reminders, broken inbox connections, and verification work will
              show up here.
            </p>
          </div>
        </div>
      ) : subListEmpty ? (
        <div className="px-5 pt-4 pb-5 text-xs text-[var(--text-tertiary)]">
          Nothing else needs action right now.
        </div>
      ) : (
        <div className="workspace-scroll-region min-h-0 flex-1 px-5 pt-4 pb-5">
          <div className="space-y-3">
          {attentionAccounts.map((account) => (
            <article key={`setup:${account.id}`} className="card-elevated p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-[var(--text-primary)]">
                    {account.emailAddress}
                  </p>
                  <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                    {account.connectionStatus}
                  </p>
                </div>
                <span className="badge border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]">
                  Setup
                </span>
              </div>
              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                {account.nextStep}
              </p>
              <div className="mt-4">
                <Link href="/app/inbox#connect-inbox" className="btn btn-secondary btn-sm">
                  Open inbox setup
                </Link>
              </div>
            </article>
          ))}

          {pendingActions.map((action) => {
            const taskType = describeTaskType(action);
            const deadline = describeDeadline(action, formatDateTime);
            const linkedApplication = applications.find(
              (candidate) => candidate.id === action.jobApplicationId,
            );
            const parserError =
              linkedApplication?.requiresReview ??
              looksLikeParserError(action.company);
            const completing = pendingReminderKey === `${action.id}:Completed`;
            const dismissing = pendingReminderKey === `${action.id}:Dismissed`;
            const snoozingPreset = (["1h", "tomorrow", "next-week"] as const)
              .find((preset) => pendingReminderKey === `${action.id}:snooze:${preset}`);
            const busy = pendingReminderKey !== null;

            const reminderActionsDisabled =
              !liveData || !canPersistReminders || busy;
            const detailHref = parserError
              ? "/app/review"
              : (`/app/applications/${action.jobApplicationId}` as Route);
            const detailLabel = parserError ? "Review" : "Detail";
            return (
              <article key={action.id} className="card-elevated min-w-0 px-3 py-2.5">
                {/* Header row: badges on left, deadline on right */}
                <div className="flex min-w-0 flex-wrap items-center gap-1.5">
                  <span className={`badge ${taskType.toneClass}`}>
                    {taskType.label}
                  </span>
                  {parserError ? (
                    <span
                      className="badge border-[var(--brand-blue)]/18 bg-[var(--blue-soft)] text-[var(--brand-blue-ink)]"
                      title="The company name looks like a parser error — fix it in the review queue."
                    >
                      ⚠
                    </span>
                  ) : null}
                  <span
                    className={`min-w-0 max-w-full truncate text-[11px] font-medium sm:ml-auto ${deadline.tone}`}
                    title={deadline.label}
                  >
                    {deadline.label}
                  </span>
                </div>

                {/* Title is the detail link; identity sits beside it */}
                <Link
                  href={detailHref}
                  className="mt-2 block min-w-0 break-words text-sm font-semibold leading-snug text-[var(--text-primary)] hover:underline"
                >
                  {action.title}
                </Link>
                <p className="mt-0.5 min-w-0 break-words text-xs text-[var(--text-secondary)]">
                  <span className="font-medium text-[var(--text-primary)]">
                    {action.company}
                  </span>
                  {" · "}
                  {action.role}
                </p>

                {/* Single-row action footer */}
                <div className="mt-2.5 flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2">
                  {action.gmailUrl ? (
                    <a
                      href={action.gmailUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="btn btn-primary btn-sm min-w-0 max-w-full"
                    >
                      {taskType.primaryActionLabel}
                    </a>
                  ) : (
                    <Link
                      href={detailHref}
                      className="btn btn-primary btn-sm min-w-0 max-w-full"
                    >
                      Open {detailLabel.toLowerCase()}
                    </Link>
                  )}

                  <div className="inline-flex min-w-0 max-w-full overflow-hidden rounded-md border border-[var(--border)] sm:ml-auto">
                    {(["1h", "tomorrow", "next-week"] as const).map(
                      (preset, index) => (
                        <button
                          key={preset}
                          type="button"
                          className={`px-2 py-1 text-[11px] font-medium text-[var(--text-secondary)] transition hover:bg-[var(--surface-overlay)] hover:text-[var(--text-primary)] disabled:opacity-50 ${
                            index > 0
                              ? "border-l border-[var(--border)]"
                              : ""
                          }`}
                          onClick={() => onReminderSnooze(action, preset)}
                          disabled={reminderActionsDisabled}
                          title={`Snooze ${getSnoozePresetLabel(preset)}`}
                        >
                          {snoozingPreset === preset
                            ? "…"
                            : getSnoozePresetLabel(preset)}
                        </button>
                      ),
                    )}
                  </div>
                  <button
                    type="button"
                    className="text-[11px] font-medium text-[var(--text-tertiary)] underline-offset-2 transition-colors hover:text-[var(--text-primary)] hover:underline disabled:opacity-50"
                    onClick={() => onReminderAction(action, "Completed")}
                    disabled={reminderActionsDisabled}
                    title="Mark done"
                  >
                    {completing ? "…" : "Done"}
                  </button>
                  <button
                    type="button"
                    className="text-[11px] font-medium text-[var(--text-tertiary)] underline-offset-2 transition-colors hover:text-[var(--text-primary)] hover:underline disabled:opacity-50"
                    onClick={() => onReminderAction(action, "Dismissed")}
                    disabled={reminderActionsDisabled}
                    title="Dismiss"
                  >
                    {dismissing ? "…" : "Dismiss"}
                  </button>
                </div>
              </article>
            );
          })}
          </div>
        </div>
      )}
    </section>
  );
}

export function SuccessToast({
  message,
  onDismiss,
}: {
  message: string;
  onDismiss: () => void;
}) {
  return (
    <div className="fixed bottom-5 right-5 z-50 w-[min(24rem,calc(100vw-2.5rem))] animate-in">
      <div className="flex items-start gap-3 rounded-lg border border-[rgba(36,122,90,0.22)] bg-[rgba(247,255,251,0.96)] p-3 pr-2 text-[var(--green)] shadow-[0_18px_48px_rgba(17,33,45,0.16)] backdrop-blur-xl">
        <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--green)] text-white">
          <svg
            aria-hidden="true"
            viewBox="0 0 20 20"
            className="h-3.5 w-3.5"
            fill="none"
          >
            <path
              d="M5 10.4 8.2 13.5 15 6.5"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </span>
        <p className="min-w-0 flex-1 text-sm font-medium leading-6 text-[var(--text-primary)]">
          {message}
        </p>
        <button
          type="button"
          aria-label="Dismiss notification"
          className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[var(--text-tertiary)] transition-colors hover:bg-[var(--surface-overlay)] hover:text-[var(--text-primary)]"
          onClick={onDismiss}
        >
          <span aria-hidden="true" className="text-base leading-none">
            x
          </span>
        </button>
      </div>
    </div>
  );
}
