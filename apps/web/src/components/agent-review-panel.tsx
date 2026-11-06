"use client";

import { useRouter } from "next/navigation";
import { useRef, useState } from "react";
import { findLatestAgentReview, type AgentReview } from "@/lib/agent-review";
import type { UserMemoryEntry } from "@/lib/profile";
import { readErrorMessage } from "@/lib/client-fetch";
import type { DashboardResponse } from "@/lib/dashboard";

const cellClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5";

function readableName(value: string) {
  return value.replaceAll("_", " ");
}

function TraceSteps({ review }: { review: AgentReview }) {
  const steps = review.trace?.steps ?? [];
  if (steps.length === 0) return null;
  return (
    <ol className="mt-3 divide-y divide-[var(--border)] border-t border-[var(--border)]">
      {steps.map((step, index) => (
        <li key={`${step.stage}-${step.agentName}-${index}`} className="min-w-0">
          <details className="group py-2">
            <summary className="flex items-baseline gap-2">
              <h3 className="shrink-0 text-sm font-medium text-[var(--text-primary)]">
                {readableName(step.agentName)}
              </h3>
              <span className="min-w-0 flex-1 truncate text-xs text-[var(--text-tertiary)] group-open:invisible">
                {step.summary}
              </span>
            </summary>
            <div className="mt-1 space-y-1 text-xs text-[var(--text-secondary)]">
              <p>{step.summary}</p>
              {step.reason ? <p className="break-words text-[var(--text-tertiary)]">{step.reason}</p> : null}
              {step.facts.length > 0 ? (
                <ul className="space-y-0.5">
                  {step.facts.map((fact, factIndex) => (
                    <li key={`${fact.label}-${factIndex}`} className="break-words">
                      <span className="font-medium">{fact.label}:</span> {fact.value}
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          </details>
        </li>
      ))}
    </ol>
  );
}

function PendingDecision({
  review,
  isDeciding,
  onDecide,
}: {
  review: AgentReview;
  isDeciding: boolean;
  onDecide: (action: "confirm" | "reject") => void;
}) {
  return (
    <div className="mt-3 rounded-md border border-[var(--yellow)]/35 bg-[var(--yellow-soft)] px-3 py-2.5">
      <p className="text-sm font-semibold text-[var(--text-primary)]">Waiting for your approval</p>
      {review.proposedReminders.length > 0 ? (
        <ul className="mt-1 divide-y divide-[var(--yellow)]/20">
          {review.proposedReminders.map((reminder) => (
            <li
              key={`${reminder.applicationId}:${reminder.title}`}
              className="flex items-baseline justify-between gap-3 py-1.5"
            >
              <span className="min-w-0 text-sm text-[var(--text-primary)]">{reminder.title}</span>
              <span className="shrink-0 text-xs text-[var(--text-secondary)]">
                in {reminder.dueInDays} {reminder.dueInDays === 1 ? "day" : "days"}
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      {review.pendingEntryId ? (
        <div className="mt-2 flex flex-wrap gap-2">
          <button
            type="button"
            className="btn btn-primary btn-sm"
            onClick={() => onDecide("confirm")}
            disabled={isDeciding}
          >
            {isDeciding ? "Saving..." : "Approve"}
          </button>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => onDecide("reject")}
            disabled={isDeciding}
          >
            Reject
          </button>
        </div>
      ) : null}
    </div>
  );
}

// The API skips proposals for rejected or closed applications when it applies an approval, so the
// replay counts the same way, from the recorded application list.
async function countRemindersTheApiWouldCreate(review: AgentReview, proxyPrefix: string | null) {
  const response = proxyPrefix ? await fetch(`${proxyPrefix}/api/dashboard?view=all`).catch(() => null) : null;
  if (!response?.ok) return review.proposedReminders.length;
  const dashboard = (await response.json()) as DashboardResponse;
  const closed = new Set(
    dashboard.applications
      .filter((application) => application.status === "Rejected" || application.status === "Closed")
      .map((application) => application.id),
  );
  return review.proposedReminders.filter((reminder) => !closed.has(reminder.applicationId)).length;
}

// The hosted demo has no backend to resume, so a decision there replays the end of the graph locally.
function replayDecision(
  review: AgentReview,
  action: "confirm" | "reject",
  remindersCreated: number,
): AgentReview {
  const approved = action === "confirm";
  return {
    ...review,
    status: approved ? "completed" : "rejected",
    requiresHumanReview: false,
    pendingEntryId: null,
    pendingUpdatedAtUtc: null,
    remindersCreated: approved ? remindersCreated : null,
    trace: {
      generatedAtUtc: review.trace?.generatedAtUtc ?? null,
      summary: review.trace?.summary ?? null,
      steps: [
        ...(review.trace?.steps ?? []),
        {
          stage: "human_review",
          agentName: "human_reviewer",
          summary: approved
            ? `The user approved this review and ${remindersCreated} ${
                remindersCreated === 1 ? "reminder" : "reminders"
              }.`
            : "The user rejected this review; nothing from it is saved.",
          reason: "Replayed in the browser. A full run resumes the paused graph from its checkpoint.",
          facts: [],
        },
      ],
    },
  };
}

export function AgentReviewPanel({
  proxyPrefix,
  initialEntries,
  readOnlyDemo = false,
}: {
  proxyPrefix: string | null;
  initialEntries: UserMemoryEntry[];
  readOnlyDemo?: boolean;
}) {
  const router = useRouter();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [review, setReview] = useState(() => findLatestAgentReview(initialEntries));
  const [isRunning, setIsRunning] = useState(false);
  const [isDeciding, setIsDeciding] = useState(false);
  const [wasReplayed, setWasReplayed] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function reload() {
    const [confirmed, pending] = await Promise.all([
      fetch(`${proxyPrefix}/api/profile/memory`, { cache: "no-store" }),
      fetch(`${proxyPrefix}/api/profile/memory/proposals`, { cache: "no-store" }),
    ]);
    if (!confirmed.ok) throw new Error(await readErrorMessage(confirmed));
    if (!pending.ok) throw new Error(await readErrorMessage(pending));
    setReview(
      findLatestAgentReview([
        ...((await confirmed.json()) as UserMemoryEntry[]),
        ...((await pending.json()) as UserMemoryEntry[]),
      ]),
    );
  }

  async function run() {
    setIsRunning(true);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/orchestrator/resume-mailbox-review`, {
        method: "POST",
      });
      if (!response.ok) throw new Error(await readErrorMessage(response));
      // An unread body holds the connection open; the stored review below is what gets shown.
      await response.text();
      await reload();
      setNotice("Review updated.");
      router.refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The review could not run.");
    } finally {
      setIsRunning(false);
    }
  }

  async function decide(action: "confirm" | "reject") {
    if (!review?.pendingEntryId) return;
    if (readOnlyDemo) {
      const created = action === "confirm" ? await countRemindersTheApiWouldCreate(review, proxyPrefix) : 0;
      setReview(replayDecision(review, action, created));
      setWasReplayed(true);
      return;
    }

    setIsDeciding(true);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(
        `${proxyPrefix}/api/profile/memory/proposals/${review.pendingEntryId}`,
        {
          method: "PATCH",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ action, expectedUpdatedAtUtc: review.pendingUpdatedAtUtc }),
        },
      );
      if (!response.ok) {
        // A newer review may have replaced this one, so show what is actually waiting now.
        const message = await readErrorMessage(response);
        await reload();
        throw new Error(message);
      }
      await response.text();
      await reload();
      setNotice(action === "confirm" ? "Review approved." : "Review rejected.");
      router.refresh();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "The decision could not be saved.");
    } finally {
      setIsDeciding(false);
    }
  }

  return (
    <>
      <button
        type="button"
        className="card flex w-full items-center gap-3 px-3 py-2 text-left sm:px-4"
        onClick={() => dialogRef.current?.showModal()}
      >
        <span className="eyebrow shrink-0">Multi-agent review</span>
        <span className="hidden min-w-0 flex-1 truncate text-sm text-[var(--text-secondary)] sm:block">
          {review?.summary ?? "No review has run yet"}
        </span>
        {review?.requiresHumanReview ? (
          <span className="badge ml-auto shrink-0 bg-[var(--yellow-soft)] text-[var(--text-secondary)]">
            Needs your approval
          </span>
        ) : null}
      </button>

      <dialog
        ref={dialogRef}
        aria-label="Multi-agent review"
        className="m-auto max-h-[90dvh] w-[min(48rem,calc(100vw-2rem))] overflow-y-auto bg-transparent backdrop:bg-[var(--backdrop)]"
        onClick={(event) => {
          if (event.target === dialogRef.current) dialogRef.current?.close();
        }}
      >
        <section className="card p-4">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <h2 className="line-clamp-2 min-w-0 flex-1 text-base font-semibold text-[var(--text-primary)]" title={review?.summary}>
              {review?.summary ?? "No review has run yet"}
            </h2>
            <div className="flex flex-wrap items-center gap-2">
              {readOnlyDemo ? (
                <span className="badge bg-[var(--yellow-soft)] text-[var(--text-secondary)]">
                  Recorded run
                </span>
              ) : (
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => void run()}
                  disabled={isRunning || !proxyPrefix}
                >
                  {isRunning ? "Reviewing..." : "Run review"}
                </button>
              )}
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={() => dialogRef.current?.close()}
              >
                Close
              </button>
            </div>
          </div>

          {error ? <div role="alert" className="alert alert-error mt-3">{error}</div> : null}
          {notice && !error ? <div className="alert alert-success mt-3">{notice}</div> : null}
          <p role="status" className="sr-only">{notice ?? ""}</p>

          {review ? (
            <>
              <p className="mt-1 break-words text-xs leading-5 text-[var(--text-tertiary)]">
                {review.modelPath}
                {review.confidence === null
                  ? null
                  : ` · confidence ${Math.round(review.confidence * 100)}%`}
              </p>

              {review.modelPath.startsWith("deterministic/") ? (
                <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
                  No model: deterministic suite.
                </p>
              ) : null}

                  {review.requiresHumanReview ? (
                <PendingDecision
                  review={review}
                  isDeciding={isDeciding}
                  onDecide={(action) => void decide(action)}
                />
              ) : review.remindersCreated !== null ? (
                <p className="mt-3 text-sm text-[var(--text-secondary)]">
                  You approved this review; it added {review.remindersCreated}{" "}
                  {review.remindersCreated === 1 ? "reminder" : "reminders"}.
                </p>
              ) : null}
              {wasReplayed ? (
                <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                  Replayed in this read-only demo. Nothing was saved.
                </p>
              ) : null}

              <TraceSteps review={review} />

              <details className="border-t border-[var(--border)] py-2">
                <summary className="text-sm font-medium text-[var(--text-primary)]">
                  Findings and next actions
                </summary>
                <div className="mt-2 grid gap-2 lg:grid-cols-2">
                  {(
                    [
                      ["Findings", review.findings],
                      ["Next actions", review.nextActions],
                    ] as const
                  ).map(([title, items]) => (
                    <div key={title} className={cellClass}>
                      <p className="eyebrow">{title}</p>
                      <ul className="mt-2 space-y-1.5 text-sm leading-5 text-[var(--text-secondary)]">
                        {items.map((item, index) => (
                          <li key={`${index}:${item}`}>{item}</li>
                        ))}
                      </ul>
                    </div>
                  ))}
                </div>
              </details>
            </>
          ) : null}
        </section>
      </dialog>
    </>
  );
}
