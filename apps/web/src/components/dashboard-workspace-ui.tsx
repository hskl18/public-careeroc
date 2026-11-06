"use client";

import type { Route } from "next";
import Link from "next/link";
import { useState } from "react";
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
      href: "/app/applications?view=needs-action" as Route,
      value: dashboard.metrics.needsAction,
      color: "var(--accent)",
      tooltip: "Open reminders and deadlines waiting on you.",
      highlight: true,
    },
    {
      label: "Email review",
      href: "/app/review" as Route,
      value: dashboard.metrics.reviewQueue,
      color: "var(--brand-blue-ink)",
      tooltip: "Emails held for a person to check.",
    },
    {
      label: "Active",
      href: "/app/applications?view=active" as Route,
      value: dashboard.metrics.activeApplications,
      color: "var(--text-primary)",
      tooltip: "Open applications still moving through the pipeline.",
    },
    {
      label: "Interviews",
      href: "/app/applications?view=interviewing" as Route,
      value: dashboard.metrics.interviews,
      color: "var(--blue)",
      tooltip: "In interview scheduling or interview execution.",
    },
    {
      label: "Offers",
      href: "/app/applications?view=offer" as Route,
      value: dashboard.metrics.offers,
      color: "var(--green)",
      tooltip: "Reached an offer stage.",
    },
    {
      label: "Rejected",
      href: "/app/applications?view=rejected" as Route,
      value: dashboard.metrics.rejections,
      color: "var(--red)",
      tooltip: "Closed by the employer. Hidden from the active view.",
    },
  ];

  return (
    <section>
      <h1 className="sr-only">Dashboard</h1>
      {coldStart ? (
        <div className="mb-2 flex items-center justify-between gap-3">
          <p className="text-sm font-medium text-[var(--text-primary)]">
            No mail yet
          </p>
          <Link href="/app/inbox#connect-inbox" className="btn btn-secondary btn-sm">
            Connect Gmail
          </Link>
        </div>
      ) : null}
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
        {metrics.map((metric) =>
          coldStart ? (
            <div
              key={metric.label}
              className="metric-tile opacity-70"
              aria-disabled="true"
              title={metric.tooltip}
            >
              <span className="min-w-0 truncate">{metric.label}</span>
              <span className="metric-tile-value" aria-label="Waiting for first sync">
                &mdash;
              </span>
            </div>
          ) : (
            <Link
              key={metric.label}
              href={metric.href}
              title={metric.tooltip}
              className={`metric-tile ${
                metric.highlight && (metric.value ?? 0) > 0 ? "ring-1 ring-[var(--accent-soft)]" : ""
              }`}
            >
              <span className="min-w-0 truncate">{metric.label}</span>
              <span
                className="metric-tile-value"
                style={{
                  color:
                    metric.value === null || metric.value === 0
                      ? "var(--text-tertiary)"
                      : metric.color,
                }}
              >
                {metric.value ?? "—"}
              </span>
            </Link>
          ),
        )}
      </div>
    </section>
  );
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
  const isCompletelyEmpty =
    pendingActions.length === 0 && attentionAccounts.length === 0;
  // The reminder whose actions are swapped for snooze durations; one at a time.
  const [choosingSnoozeFor, setChoosingSnoozeFor] = useState<string | null>(null);

  return (
    <section className="card flex h-full min-h-0 flex-col p-0">
      <div className="flex shrink-0 items-center justify-between gap-3 px-4 pt-3">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">To do</h2>
        <span className="text-xs tabular-nums text-[var(--text-tertiary)]">
          {pendingActions.length + attentionAccounts.length}
        </span>
      </div>

      {coldStart && isCompletelyEmpty ? (
        <div className="px-5 pt-4 pb-5">
          <div className="card-elevated p-5">
            <p className="text-sm font-medium text-[var(--text-primary)]">
              Nothing yet
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
              All clear
            </p>
          </div>
        </div>
      ) : (
        <div className="workspace-scroll-region fill-rows min-h-0 flex-1 divide-y divide-[var(--border)] px-4 pt-1 pb-2">
          {attentionAccounts.map((account) => (
            <article key={`setup:${account.id}`} className="py-2.5">
              <div className="flex min-w-0 items-baseline justify-between gap-3">
                <p className="min-w-0 truncate text-sm font-medium text-[var(--text-primary)]">
                  {account.emailAddress}
                </p>
                <Link
                  href="/app/inbox#connect-inbox"
                  className="shrink-0 text-xs font-medium text-[var(--accent)] hover:underline"
                >
                  Open inbox setup
                </Link>
              </div>
              <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                {account.connectionStatus} · {account.nextStep}
              </p>
            </article>
          ))}

          {pendingActions.map((action) => {
            const linkedApplication = applications.find(
              (candidate) => candidate.id === action.jobApplicationId,
            );
            const parserError =
              linkedApplication?.requiresReview ??
              looksLikeParserError(action.company);
            const completing = pendingReminderKey === `${action.id}:Completed`;
            const dismissing = pendingReminderKey === `${action.id}:Dismissed`;
            const snoozing = pendingReminderKey?.startsWith(`${action.id}:snooze:`) ?? false;
            const reminderActionsDisabled =
              !liveData || !canPersistReminders || pendingReminderKey !== null;
            const detailHref = parserError
              ? "/app/review"
              : (`/app/applications/${action.jobApplicationId}` as Route);

            return (
              <article key={action.id} className="min-w-0 py-2.5">
                <div className="flex min-w-0 items-baseline justify-between gap-3">
                  <Link
                    href={detailHref}
                    title={
                      parserError
                        ? "The company name looks like a parser error. Fix it in the review queue."
                        : undefined
                    }
                    className="min-w-0 break-words text-sm font-medium leading-snug text-[var(--text-primary)] hover:underline"
                  >
                    {parserError ? "⚠ " : ""}
                    {action.title}
                  </Link>
                  {action.deadlineFound ? (
                    <span className="shrink-0 text-xs font-medium text-[var(--accent)]">
                      Due {formatDateTime(action.dueAtUtc)}
                    </span>
                  ) : null}
                </div>

                <div className="mt-1 flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-1">
                  <p className="min-w-0 truncate text-xs text-[var(--text-tertiary)]">
                    {action.title.includes(action.company)
                      ? action.role
                      : `${action.company} · ${action.role}`}
                  </p>
                  {choosingSnoozeFor === action.id ? (
                    // Shown in place of the row's actions, so nothing floats over the next row
                    // or is clipped by the card; a select that acted on change snoozed on an arrow key.
                    <div
                      role="group"
                      aria-label={`Snooze ${action.title} for`}
                      className="flex shrink-0 flex-wrap items-center gap-3 text-xs font-medium text-[var(--text-tertiary)]"
                      onKeyDown={(event) => {
                        if (event.key === "Escape") setChoosingSnoozeFor(null);
                      }}
                    >
                      {(["1h", "tomorrow", "next-week"] as const).map((preset, index) => (
                        <button
                          key={preset}
                          type="button"
                          autoFocus={index === 0}
                          className="hover:text-[var(--text-primary)] hover:underline disabled:opacity-50 max-sm:min-h-6"
                          disabled={reminderActionsDisabled}
                          onClick={() => {
                            setChoosingSnoozeFor(null);
                            onReminderSnooze(action, preset);
                          }}
                        >
                          {getSnoozePresetLabel(preset)}
                        </button>
                      ))}
                      <button
                        type="button"
                        className="hover:text-[var(--text-primary)] hover:underline max-sm:min-h-6"
                        onClick={() => setChoosingSnoozeFor(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  ) : (
                    <div className="flex shrink-0 items-center gap-3 text-xs font-medium text-[var(--text-tertiary)]">
                      {action.gmailUrl ? (
                        <a
                          href={action.gmailUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="hover:text-[var(--text-primary)] hover:underline max-sm:min-h-6"
                        >
                          Gmail
                        </a>
                      ) : null}
                      <button
                        type="button"
                        className="hover:text-[var(--text-primary)] hover:underline disabled:opacity-50 max-sm:min-h-6"
                        onClick={() => onReminderAction(action, "Completed")}
                        disabled={reminderActionsDisabled}
                      >
                        {completing ? "…" : "Done"}
                      </button>
                      <button
                        type="button"
                        className="hover:text-[var(--text-primary)] hover:underline disabled:opacity-50 max-sm:min-h-6"
                        aria-label={`Snooze ${action.title}`}
                        onClick={() => setChoosingSnoozeFor(action.id)}
                        disabled={reminderActionsDisabled}
                      >
                        {snoozing ? "…" : "Snooze"}
                      </button>
                      <button
                        type="button"
                        className="hover:text-[var(--text-primary)] hover:underline disabled:opacity-50 max-sm:min-h-6"
                        onClick={() => onReminderAction(action, "Dismissed")}
                        disabled={reminderActionsDisabled}
                      >
                        {dismissing ? "…" : "Dismiss"}
                      </button>
                    </div>
                  )}
                </div>
              </article>
            );
          })}
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
    <div className="fixed bottom-20 right-5 z-50 w-[min(24rem,calc(100vw-2.5rem))] animate-in sm:bottom-5">
      <div className="flex items-start gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-3 pr-2 text-[var(--green)] shadow-lg">
        <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--green)] text-[var(--on-accent)]">
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
