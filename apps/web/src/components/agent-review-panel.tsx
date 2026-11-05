"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { LocalTime } from "@/components/local-time";
import { findLatestAgentReview, type AgentReview } from "@/lib/agent-review";
import type { UserMemoryEntry } from "@/lib/profile";

const cellClass =
  "rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2.5";

function readableName(value: string) {
  return value.replaceAll("_", " ");
}

async function readErrorMessage(response: Response) {
  const payload = (await response.json().catch(() => null)) as { error?: string } | null;
  return payload?.error ?? `Request failed with ${response.status}`;
}

function TraceSteps({ review }: { review: AgentReview }) {
  const steps = review.trace?.steps ?? [];
  if (steps.length === 0) return null;
  return (
    <ol className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
      {steps.map((step, index) => (
        <li key={`${step.stage}-${step.agentName}-${index}`} className={`min-w-0 ${cellClass}`}>
          <div className="flex items-baseline justify-between gap-2">
            <h3 className="truncate text-sm font-semibold text-[var(--text-primary)]">
              {readableName(step.agentName)}
            </h3>
            <span className="shrink-0 text-[10px] font-medium uppercase tracking-[0.12em] text-[var(--text-tertiary)]">
              {readableName(step.stage)}
            </span>
          </div>
          <p className="mt-1 text-xs leading-5 text-[var(--text-secondary)]">{step.summary}</p>
          {step.reason ? (
            <p className="mt-1 break-words text-[11px] leading-4 text-[var(--text-tertiary)]">
              {step.reason}
            </p>
          ) : null}
          {step.facts.length > 0 ? (
            <details className="mt-1.5">
              <summary className="text-[11px] font-medium text-[var(--text-secondary)]">
                {step.facts.length} {step.facts.length === 1 ? "fact" : "facts"}
              </summary>
              <ul className="mt-1 space-y-1 text-[11px] leading-4 text-[var(--text-secondary)]">
                {step.facts.map((fact, factIndex) => (
                  <li key={`${fact.label}-${factIndex}`} className="break-words">
                    <span className="font-medium">{fact.label}:</span> {fact.value}
                  </li>
                ))}
              </ul>
            </details>
          ) : null}
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
      <p className="mt-0.5 text-xs leading-5 text-[var(--text-secondary)]">
        {review.proposedReminders.length > 0
          ? "The agents propose these reminders. Nothing is created until you approve."
          : "The evidence gate kept this review out of confirmed memory until you approve it."}
      </p>
      {review.proposedReminders.length > 0 ? (
        <ul className="mt-2 grid gap-2 lg:grid-cols-3">
          {review.proposedReminders.map((reminder) => (
            <li
              key={reminder.applicationId}
              className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2"
            >
              <p className="text-sm font-medium text-[var(--text-primary)]">{reminder.title}</p>
              <p className="mt-0.5 text-xs text-[var(--text-secondary)]">
                {reminder.company} · due {reminder.dueInDays}{" "}
                {reminder.dueInDays === 1 ? "day" : "days"} after approval
              </p>
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

// The hosted demo has no backend to resume, so a decision there replays the end of the graph locally.
function replayDecision(review: AgentReview, action: "confirm" | "reject"): AgentReview {
  const approved = action === "confirm";
  return {
    ...review,
    status: approved ? "completed" : "rejected",
    requiresHumanReview: false,
    pendingEntryId: null,
    pendingUpdatedAtUtc: null,
    remindersCreated: approved ? review.proposedReminders.length : null,
    trace: {
      generatedAtUtc: review.trace?.generatedAtUtc ?? null,
      summary: review.trace?.summary ?? null,
      steps: [
        ...(review.trace?.steps ?? []),
        {
          stage: "human_review",
          agentName: "human_reviewer",
          summary: approved
            ? `The user approved this review and ${review.proposedReminders.length} ${
                review.proposedReminders.length === 1 ? "reminder" : "reminders"
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
      setReview(replayDecision(review, action));
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
    <section className="card app-workspace-panel p-3 sm:p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="eyebrow">Multi-agent review</p>
          <h2 className="mt-1 text-lg font-semibold text-[var(--text-primary)]">
            {review?.summary ?? "No review has run yet"}
          </h2>
        </div>
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
        </div>
      </div>

      {error ? <div className="alert alert-error mt-3">{error}</div> : null}
      {notice && !error ? <div className="alert alert-success mt-3">{notice}</div> : null}

      {review ? (
        <>
          <p className="mt-1 break-words text-xs leading-5 text-[var(--text-tertiary)]">
            <span>{review.modelPath}</span> · <span>{review.runtimePath}</span>
            {review.confidence === null ? null : (
              <>
                {" · confidence "}
                <span>{Math.round(review.confidence * 100)}%</span>
              </>
            )}
            {" · "}
            {readableName(review.status)} · <LocalTime value={review.generatedAtUtc} />
          </p>

          {review.modelPath.startsWith("deterministic/") ? (
            <p className="mt-1 text-xs leading-5 text-[var(--text-tertiary)]">
              This run used the deterministic suite: the same graph and gate, with no model.
            </p>
          ) : null}

          <TraceSteps review={review} />

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

          <details className="mt-3">
            <summary className="text-xs font-semibold text-[var(--text-secondary)]">
              Findings and next actions
            </summary>
            <div className="mt-2 grid gap-2 lg:grid-cols-2">
              {[
                ["Findings", review.findings],
                ["Next actions", review.nextActions],
              ].map(([title, items]) => (
                <div key={title as string} className={cellClass}>
                  <p className="eyebrow">{title as string}</p>
                  <ul className="mt-2 space-y-1.5 text-sm leading-5 text-[var(--text-secondary)]">
                    {(items as string[]).map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          </details>
        </>
      ) : null}
    </section>
  );
}
