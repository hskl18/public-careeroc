"use client";

import Link from "next/link";
import { useCallback, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  formatMemoryKind,
  MemoryEmptyState,
  MemoryArchiveItem,
  MemoryCountTile,
  MemoryEntryCard,
  MemorySectionShell,
  PendingProposalCard,
  SourceEvidencePanel,
  tryFormatMemoryJson,
  type MemoryEditDraft,
} from "@/components/profile-memory-review-sections";
import { LocalTime } from "@/components/local-time";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import type { ProposedReminder, ResumeMailboxReview, UserMemoryEntry } from "@/lib/profile";

// A review waiting for approval is a pending memory entry; deciding on it goes through that entry.
type StrategyReview = ResumeMailboxReview & { pendingEntryId: string | null };

type ProfileMemoryPanelProps = {
  proxyPrefix: string | null;
  email: string;
  displayName: string;
  initialMemory: UserMemoryEntry[];
  initialPendingMemory?: UserMemoryEntry[];
};

function validateMemoryValue(value: string): string | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return "Memory value is required.";
  }
  if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) {
    return null;
  }
  try {
    JSON.parse(trimmed);
    return null;
  } catch {
    return "This looks like JSON, but it is not valid JSON.";
  }
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload?.error) return payload.error;
  } catch {}
  return `Request failed with ${response.status}`;
}

function isRejectedOrExpired(entry: UserMemoryEntry, referenceTimeMs: number) {
  const status = entry.confirmationStatus.toLowerCase();
  if (status === "rejected" || status === "expired") return true;
  if (!entry.expiresAtUtc) return false;
  const expiresAt = Date.parse(entry.expiresAtUtc);
  return Number.isFinite(expiresAt) && expiresAt < referenceTimeMs;
}

function isPending(entry: UserMemoryEntry) {
  return entry.confirmationStatus.toLowerCase() === "pending";
}

function normalizeStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === "string" && item.trim().length > 0)
    : [];
}

function normalizeAgentTrace(value: unknown): ResumeMailboxReview["trace"] {
  if (!value || typeof value !== "object") return null;
  const candidate = value as { steps?: unknown; generatedAtUtc?: unknown; summary?: unknown };
  if (!Array.isArray(candidate.steps)) return null;

  const steps = candidate.steps.flatMap((step) => {
    if (!step || typeof step !== "object") return [];
    const item = step as Record<string, unknown>;
    if (
      typeof item.stage !== "string" ||
      typeof item.agentName !== "string" ||
      typeof item.summary !== "string"
    ) {
      return [];
    }
    const facts = Array.isArray(item.facts)
      ? item.facts.flatMap((fact) => {
          if (!fact || typeof fact !== "object") return [];
          const factItem = fact as Record<string, unknown>;
          return typeof factItem.label === "string" && typeof factItem.value === "string"
            ? [{ label: factItem.label, value: factItem.value }]
            : [];
        })
      : [];
    return [
      {
        stage: item.stage,
        agentName: item.agentName,
        summary: item.summary,
        reason: typeof item.reason === "string" ? item.reason : null,
        facts,
      },
    ];
  });

  return {
    generatedAtUtc:
      typeof candidate.generatedAtUtc === "string" ? candidate.generatedAtUtc : null,
    steps,
    summary: null,
  };
}

function normalizeProposedReminders(value: unknown): ProposedReminder[] {
  if (!Array.isArray(value)) return [];
  return value.flatMap((item) => {
    if (!item || typeof item !== "object") return [];
    const candidate = item as Record<string, unknown>;
    return typeof candidate.applicationId === "string" &&
      typeof candidate.company === "string" &&
      typeof candidate.role === "string" &&
      typeof candidate.title === "string" &&
      typeof candidate.dueInDays === "number"
      ? [
          {
            applicationId: candidate.applicationId,
            company: candidate.company,
            role: candidate.role,
            title: candidate.title,
            dueInDays: candidate.dueInDays,
          },
        ]
      : [];
  });
}

function parseStoredStrategyReview(entry: UserMemoryEntry): StrategyReview | null {
  if (entry.kind !== "orchestrator" || entry.key !== "orchestrator.resume_mailbox_bridge") {
    return null;
  }

  try {
    const parsed = JSON.parse(entry.value) as Partial<ResumeMailboxReview>;
    const evidence = parsed.evidence;
    if (!parsed.summary || !evidence) return null;
    return {
      generatedAtUtc: parsed.generatedAtUtc ?? entry.updatedAtUtc,
      runtimePath: parsed.runtimePath ?? "career_orchestrator.resume_mailbox_review",
      modelPath: parsed.modelPath ?? "deterministic.shared_resume_mailbox_context.v1",
      summary: parsed.summary,
      findings: normalizeStringArray(parsed.findings),
      nextActions: normalizeStringArray(parsed.nextActions),
      evidence: {
        resumeVersions: Number(evidence.resumeVersions ?? 0),
        latestResumeScore: Number(evidence.latestResumeScore ?? 0),
        totalApplications: Number(evidence.totalApplications ?? 0),
        interviewOrOfferApplications: Number(evidence.interviewOrOfferApplications ?? 0),
        rejectedApplications: Number(evidence.rejectedApplications ?? 0),
        resumeVersionLinkedToOutcome: evidence.resumeVersionLinkedToOutcome ?? null,
      },
      threadId: parsed.threadId ?? null,
      status: parsed.status ?? "completed",
      requiresHumanReview: parsed.requiresHumanReview ?? false,
      confidence: parsed.confidence ?? null,
      trace: normalizeAgentTrace(parsed.trace),
      proposedReminders: normalizeProposedReminders(parsed.proposedReminders),
      remindersCreated:
        typeof parsed.remindersCreated === "number" ? parsed.remindersCreated : null,
      pendingEntryId: isPending(entry) ? entry.id : null,
    };
  } catch {
    return null;
  }
}

function findLatestStrategyReview(entries: UserMemoryEntry[]): StrategyReview | null {
  return entries
    .map(parseStoredStrategyReview)
    .filter((review): review is StrategyReview => review !== null)
    .sort((a, b) => b.generatedAtUtc.localeCompare(a.generatedAtUtc))[0] ?? null;
}

function formatStrategyDate(value: string) {
  return Number.isFinite(Date.parse(value)) ? <LocalTime value={value} /> : "Not recorded";
}

function shortVersionId(value: string | null) {
  return value ? value.slice(0, 8) : "None";
}

function formatStrategyStatus(value: string) {
  return value.replaceAll("_", " ");
}

function downloadJsonFile(filename: string, value: unknown) {
  const blob = new Blob([JSON.stringify(value, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function StrategySnapshotPanel({
  review,
  isReviewing,
  isDeciding,
  onRun,
  onDecide,
}: {
  review: StrategyReview | null;
  isReviewing: boolean;
  isDeciding: boolean;
  onRun: () => void;
  onDecide: (action: "confirm" | "reject") => void;
}) {
  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="eyebrow">Strategy snapshot</p>
          <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
            Resume + mailbox orchestrator
          </h2>
        </div>
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={onRun}
          disabled={isReviewing}
        >
          {isReviewing ? "Reviewing..." : "Run review"}
        </button>
      </div>

      {review ? (
        <div className="mt-4 space-y-4">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-sm font-semibold text-[var(--text-primary)]">
                {review.summary}
              </p>
              <div className="flex flex-wrap items-center gap-2">
                <span className="badge bg-[var(--blue-soft)] text-[var(--brand-blue)]">
                  {formatStrategyStatus(review.status)}
                </span>
                <span className="badge bg-[var(--surface-strong)] text-[var(--text-tertiary)]">
                  {formatStrategyDate(review.generatedAtUtc)}
                </span>
              </div>
            </div>
            <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              <StrategyMeta label="Runtime" value={review.runtimePath} />
              <StrategyMeta label="Model path" value={review.modelPath} />
              <StrategyMeta
                label="Confidence"
                value={review.confidence === null ? "Not recorded" : `${Math.round(review.confidence * 100)}%`}
              />
              <StrategyMeta
                label="Thread"
                value={review.threadId ? review.threadId.slice(-12) : "Not checkpointed"}
              />
            </div>
          </div>

          {review.requiresHumanReview ? (
            <div className="rounded-md border border-[var(--yellow)]/35 bg-[var(--yellow-soft)] px-4 py-3">
              <p className="text-sm font-semibold text-[var(--text-primary)]">
                Waiting for your approval
              </p>
              <p className="mt-1 text-xs leading-5 text-[var(--text-secondary)]">
                {review.proposedReminders.length > 0
                  ? "The agents propose these reminders. Nothing is created until you approve."
                  : "The evidence gate kept this review out of confirmed memory until you approve it."}
              </p>
              {review.proposedReminders.length > 0 ? (
                <ul className="mt-3 space-y-2">
                  {review.proposedReminders.map((reminder) => (
                    <li
                      key={reminder.applicationId}
                      className="rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2"
                    >
                      <p className="text-sm font-medium text-[var(--text-primary)]">
                        {reminder.title}
                      </p>
                      <p className="mt-0.5 text-xs text-[var(--text-secondary)]">
                        {reminder.company} · {reminder.role} · due {reminder.dueInDays}{" "}
                        {reminder.dueInDays === 1 ? "day" : "days"} after approval
                      </p>
                    </li>
                  ))}
                </ul>
              ) : null}
              {review.pendingEntryId ? (
                <div className="mt-3 flex flex-wrap gap-2">
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
          ) : review.remindersCreated !== null ? (
            <p className="text-sm text-[var(--text-secondary)]">
              You approved this review; it added {review.remindersCreated}{" "}
              {review.remindersCreated === 1 ? "reminder" : "reminders"} to your applications.
            </p>
          ) : null}

          <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
            <MemoryCountTile label="Resumes" value={review.evidence.resumeVersions} />
            <MemoryCountTile label="Score" value={review.evidence.latestResumeScore} />
            <MemoryCountTile label="Applications" value={review.evidence.totalApplications} />
            <MemoryCountTile
              label="Signals"
              value={review.evidence.interviewOrOfferApplications}
              tone="var(--green)"
            />
            <MemoryCountTile
              label="Rejected"
              value={review.evidence.rejectedApplications}
              tone="var(--red)"
            />
            <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2">
              <p className="eyebrow">Outcome resume</p>
              <p className="mt-1 text-lg font-semibold tabular-nums text-[var(--text-primary)]">
                {shortVersionId(review.evidence.resumeVersionLinkedToOutcome)}
              </p>
            </div>
          </div>

          <div className="grid gap-3 lg:grid-cols-2">
            <StrategyList title="Findings" items={review.findings} />
            <StrategyList title="Next actions" items={review.nextActions} />
          </div>

          {review.trace && review.trace.steps.length > 0 ? (
            <div className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
              <p className="eyebrow">Agent run trace</p>
              <ol className="mt-3 grid gap-2 lg:grid-cols-2">
                {review.trace.steps.map((step, index) => (
                  <li
                    key={`${step.stage}-${step.agentName}-${index}`}
                    className="rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-3"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <p className="text-xs font-semibold text-[var(--text-primary)]">
                        {step.agentName.replaceAll("_", " ")}
                      </p>
                      <span className="text-[10px] font-medium uppercase tracking-[0.12em] text-[var(--text-tertiary)]">
                        {step.stage.replaceAll("_", " ")}
                      </span>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-[var(--text-secondary)]">
                      {step.summary}
                    </p>
                    {step.reason ? (
                      <p className="mt-2 text-[11px] leading-4 text-[var(--text-tertiary)]">
                        {step.reason}
                      </p>
                    ) : null}
                  </li>
                ))}
              </ol>
            </div>
          ) : null}

          <div className="grid gap-3 sm:grid-cols-2">
            <Link
              href="/app/resume"
              className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4 transition hover:border-[var(--brand-blue)]/35 hover:bg-[var(--blue-soft)]"
            >
              <p className="eyebrow">Evidence link</p>
              <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
                Open resume context
              </p>
              <p className="mt-1 text-xs leading-5 text-[var(--text-secondary)]">
                Review versions, latest score, and extracted resume structure.
              </p>
            </Link>
            <Link
              href="/app/applications"
              className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4 transition hover:border-[var(--brand-blue)]/35 hover:bg-[var(--blue-soft)]"
            >
              <p className="eyebrow">Evidence link</p>
              <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
                Open application outcomes
              </p>
              <p className="mt-1 text-xs leading-5 text-[var(--text-secondary)]">
                Compare interview, offer, rejection, and active pipeline signals.
              </p>
            </Link>
          </div>
        </div>
      ) : (
        <MemoryEmptyState message="No orchestrator snapshot has been generated yet." />
      )}
    </section>
  );
}

function StrategyMeta({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2">
      <p className="eyebrow">{label}</p>
      <p className="mt-1 truncate text-xs font-medium text-[var(--text-secondary)]">
        {value}
      </p>
    </div>
  );
}

function StrategyList({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
      <p className="eyebrow">{title}</p>
      {items.length > 0 ? (
        <ul className="mt-3 space-y-2 text-sm leading-6 text-[var(--text-secondary)]">
          {items.map((item) => (
            <li key={item} className="flex gap-2">
              <span className="mt-2 size-1.5 shrink-0 rounded-full bg-[var(--accent)]" />
              <span>{item}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-3 text-sm text-[var(--text-tertiary)]">None recorded.</p>
      )}
    </div>
  );
}

export function ProfileMemoryPanel({
  proxyPrefix,
  initialMemory,
  initialPendingMemory = [],
}: ProfileMemoryPanelProps) {
  const router = useRouter();
  const [confirmed, setConfirmed] = useState<UserMemoryEntry[]>(initialMemory);
  const [pending, setPending] = useState<UserMemoryEntry[]>(initialPendingMemory);
  const [strategyReview, setStrategyReview] = useState<StrategyReview | null>(() =>
    findLatestStrategyReview([...initialMemory, ...initialPendingMemory]),
  );
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<MemoryEditDraft | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isReviewingStrategy, setIsReviewingStrategy] = useState(false);
  const [memoryReferenceTimeMs] = useState(() => Date.now());
  const draftValueError = draft ? validateMemoryValue(draft.value) : null;

  const reload = useCallback(async () => {
    if (!proxyPrefix) return;
    setIsLoading(true);
    setError(null);
    try {
      const [confirmedResponse, pendingResponse] = await Promise.all([
        fetchWithInFlightDedup(`${proxyPrefix}/api/profile/memory`, {
          cache: "no-store",
        }),
        fetchWithInFlightDedup(`${proxyPrefix}/api/profile/memory/proposals`, {
          cache: "no-store",
        }),
      ]);

      if (!confirmedResponse.ok) {
        throw new Error(await readErrorMessage(confirmedResponse));
      }
      const confirmedEntries = (await confirmedResponse.json()) as UserMemoryEntry[];
      setConfirmed(confirmedEntries);

      let pendingEntries: UserMemoryEntry[] = [];
      if (pendingResponse.ok) {
        pendingEntries = (await pendingResponse.json()) as UserMemoryEntry[];
        setPending(pendingEntries);
      }
      setStrategyReview(findLatestStrategyReview([...confirmedEntries, ...pendingEntries]));
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Couldn't load memory.");
    } finally {
      setIsLoading(false);
    }
  }, [proxyPrefix]);

  const runStrategyReview = useCallback(async () => {
    if (!proxyPrefix) return;
    setIsReviewingStrategy(true);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/orchestrator/resume-mailbox-review`, {
        method: "POST",
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      const review = (await response.json()) as ResumeMailboxReview;
      setStrategyReview({ ...review, pendingEntryId: null });
      setNotice("Strategy snapshot updated.");
      await reload();
      router.refresh();
    } catch (reviewError) {
      setError(reviewError instanceof Error ? reviewError.message : "Couldn't run orchestrator review.");
    } finally {
      setIsReviewingStrategy(false);
    }
  }, [proxyPrefix, reload, router]);

  const confirmedReview = useMemo(
    () => confirmed.filter((entry) => !isRejectedOrExpired(entry, memoryReferenceTimeMs)),
    [confirmed, memoryReferenceTimeMs],
  );
  const pendingReview = useMemo(
    () =>
      pending.filter(
        (entry) => isPending(entry) && !isRejectedOrExpired(entry, memoryReferenceTimeMs),
      ),
    [pending, memoryReferenceTimeMs],
  );
  const rejectedOrExpired = useMemo(
    () =>
      [...pending, ...confirmed]
        .filter((entry) => isRejectedOrExpired(entry, memoryReferenceTimeMs))
        .sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)),
    [pending, confirmed, memoryReferenceTimeMs],
  );
  const evidenceEntries = useMemo(
    () =>
      [...pendingReview, ...confirmedReview, ...rejectedOrExpired].sort((a, b) =>
        b.updatedAtUtc.localeCompare(a.updatedAtUtc),
      ),
    [pendingReview, confirmedReview, rejectedOrExpired],
  );
  const sourceCount = useMemo(
    () => new Set(evidenceEntries.map((entry) => entry.sourceType)).size,
    [evidenceEntries],
  );

  const grouped = useMemo(() => {
    const buckets = new Map<string, UserMemoryEntry[]>();
    for (const entry of confirmedReview) {
      const key = entry.kind || "other";
      const bucket = buckets.get(key) ?? [];
      bucket.push(entry);
      buckets.set(key, bucket);
    }
    return Array.from(buckets.entries())
      .map(([kind, entries]) => ({
        kind,
        entries: entries.sort((a, b) => b.updatedAtUtc.localeCompare(a.updatedAtUtc)),
      }))
      .sort((a, b) => formatMemoryKind(a.kind).localeCompare(formatMemoryKind(b.kind)));
  }, [confirmedReview]);
  const confirmedByKindKey = useMemo(() => {
    const entries = new Map<string, UserMemoryEntry>();
    for (const entry of confirmedReview) {
      entries.set(`${entry.kind}\u0000${entry.key}`, entry);
    }
    return entries;
  }, [confirmedReview]);

  function startEdit(entry: UserMemoryEntry) {
    setEditingId(entry.id);
    setExpandedId(entry.id);
    setDraft({
      value: tryFormatMemoryJson(entry.value).pretty,
      summary: entry.summary ?? "",
      confidence: entry.confidence.toString(),
      expiresAtUtc: "",
    });
    setError(null);
    setNotice(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setDraft(null);
  }

  async function saveEdit(entryId: string) {
    if (!proxyPrefix || !draft) return;
    const valueError = validateMemoryValue(draft.value);
    if (valueError) {
      setError(valueError);
      return;
    }
    const confidence = Number.parseFloat(draft.confidence);
    if (!Number.isFinite(confidence) || confidence < 0 || confidence > 1) {
      setError("Confidence must be between 0 and 1.");
      return;
    }
    const expiresAtUtc = draft.expiresAtUtc.trim();
    if (expiresAtUtc.length > 0 && Number.isNaN(Date.parse(expiresAtUtc))) {
      setError("Expiration must be a valid ISO date or empty.");
      return;
    }
    setBusyId(entryId);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/profile/memory/${entryId}`, {
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          value: draft.value,
          summary: draft.summary.trim().length > 0 ? draft.summary.trim() : null,
          confidence,
          expiresAtUtc: expiresAtUtc.length > 0 ? new Date(expiresAtUtc).toISOString() : null,
        }),
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      const updated = (await response.json()) as UserMemoryEntry;
      setConfirmed((prev) => prev.map((entry) => (entry.id === updated.id ? updated : entry)));
      setEditingId(null);
      setDraft(null);
      setNotice("Memory saved.");
      router.refresh();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Couldn't save memory.");
    } finally {
      setBusyId(null);
    }
  }

  async function deleteEntry(entryId: string) {
    if (!proxyPrefix) return;
    if (!window.confirm("Remove this memory entry?")) return;
    setBusyId(entryId);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/profile/memory/${entryId}`, {
        method: "DELETE",
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      setConfirmed((prev) => prev.filter((entry) => entry.id !== entryId));
      setPending((prev) => prev.filter((entry) => entry.id !== entryId));
      if (expandedId === entryId) setExpandedId(null);
      if (editingId === entryId) {
        setEditingId(null);
        setDraft(null);
      }
      setNotice("Memory removed.");
      router.refresh();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Couldn't remove memory.");
    } finally {
      setBusyId(null);
    }
  }

  function exportMemory() {
    const exportedAtUtc = new Date().toISOString();
    downloadJsonFile("careeroc-memory-export.json", {
      exportedAtUtc,
      counts: {
        pending: pendingReview.length,
        confirmed: confirmedReview.length,
        rejectedOrExpired: rejectedOrExpired.length,
      },
      strategyReview,
      pending: pendingReview,
      confirmed: confirmedReview,
      rejectedOrExpired,
    });
    setNotice("Memory export downloaded.");
  }

  async function clearConfirmedMemory() {
    if (!proxyPrefix || confirmedReview.length === 0) return;
    if (
      !window.confirm(
        `Remove ${confirmedReview.length} confirmed memory item${
          confirmedReview.length === 1 ? "" : "s"
        }? This does not delete applications, resumes, or inbox records.`,
      )
    ) {
      return;
    }
    setBusyId("clear-confirmed");
    setError(null);
    setNotice(null);
    try {
      for (const entry of confirmedReview) {
        const response = await fetch(`${proxyPrefix}/api/profile/memory/${entry.id}`, {
          method: "DELETE",
        });
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
      }
      setConfirmed((prev) =>
        prev.filter(
          (entry) =>
            !confirmedReview.some((confirmedEntry) => confirmedEntry.id === entry.id),
        ),
      );
      setExpandedId(null);
      setEditingId(null);
      setDraft(null);
      setNotice("Confirmed memory cleared.");
      router.refresh();
    } catch (clearError) {
      setError(clearError instanceof Error ? clearError.message : "Couldn't clear memory.");
    } finally {
      setBusyId(null);
    }
  }

  async function rejectPendingMemory() {
    if (!proxyPrefix || pendingReview.length === 0) return;
    if (
      !window.confirm(
        `Reject ${pendingReview.length} pending memory proposal${
          pendingReview.length === 1 ? "" : "s"
        }? Confirmed memory will stay unchanged.`,
      )
    ) {
      return;
    }
    setBusyId("reject-pending");
    setError(null);
    setNotice(null);
    try {
      for (const entry of pendingReview) {
        const response = await fetch(
          `${proxyPrefix}/api/profile/memory/proposals/${entry.id}`,
          {
            method: "PATCH",
            headers: { "content-type": "application/json" },
            body: JSON.stringify({ action: "reject" }),
          },
        );
        if (!response.ok) {
          throw new Error(await readErrorMessage(response));
        }
      }
      setPending((prev) =>
        prev.filter(
          (entry) =>
            !pendingReview.some((pendingEntry) => pendingEntry.id === entry.id),
        ),
      );
      setNotice("Pending memory proposals rejected.");
      router.refresh();
    } catch (rejectError) {
      setError(
        rejectError instanceof Error
          ? rejectError.message
          : "Couldn't reject pending proposals.",
      );
    } finally {
      setBusyId(null);
    }
  }

  async function decideStrategyReview(action: "confirm" | "reject") {
    if (!strategyReview?.pendingEntryId) return;
    if (await resolveProposal(strategyReview.pendingEntryId, action)) {
      await reload();
      setNotice(action === "confirm" ? "Review approved." : "Review rejected.");
    }
  }

  async function resolveProposal(entryId: string, action: "confirm" | "reject") {
    if (!proxyPrefix) return false;
    setBusyId(entryId);
    setError(null);
    setNotice(null);
    try {
      const response = await fetch(`${proxyPrefix}/api/profile/memory/proposals/${entryId}`, {
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ action }),
      });
      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }
      const updated = (await response.json()) as UserMemoryEntry;
      setPending((prev) => prev.filter((entry) => entry.id !== entryId));
      if (action === "confirm") {
        setConfirmed((prev) => [updated, ...prev.filter((entry) => entry.id !== updated.id)]);
      }
      setNotice(action === "confirm" ? "Memory confirmed." : "Proposal rejected.");
      router.refresh();
      return true;
    } catch (resolveError) {
      setError(resolveError instanceof Error ? resolveError.message : "Couldn't resolve proposal.");
      return false;
    } finally {
      setBusyId(null);
    }
  }

  if (!proxyPrefix) {
    return (
      <section className="card app-workspace-panel p-5">
        <p className="eyebrow">Memory</p>
        <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">Long-term memory</h2>
        <p className="mt-3 text-sm text-[var(--text-secondary)]">
          Memory is unavailable until the live API is connected.
        </p>
      </section>
    );
  }

  return (
    <div className="space-y-4">
      <section className="card app-workspace-panel p-4">
        <header className="flex flex-wrap items-baseline justify-between gap-3">
          <div>
            <p className="eyebrow">Memory controls</p>
            <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
              Review queue
            </h2>
            <p className="mt-1 text-sm text-[var(--text-secondary)]">
              Agents can propose memory, but confirmed entries stay editable and removable here.
            </p>
          </div>
          <div className="flex flex-wrap items-center justify-end gap-2 text-xs text-[var(--text-tertiary)]">
            {isLoading ? <span>Refreshing...</span> : null}
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={exportMemory}
              disabled={isLoading}
            >
              Export JSON
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={rejectPendingMemory}
              disabled={isLoading || pendingReview.length === 0 || busyId === "reject-pending"}
            >
              {busyId === "reject-pending" ? "Rejecting..." : "Reject pending"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={clearConfirmedMemory}
              disabled={
                isLoading ||
                confirmedReview.length === 0 ||
                busyId === "clear-confirmed"
              }
            >
              {busyId === "clear-confirmed" ? "Clearing..." : "Clear confirmed"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={reload}
              disabled={isLoading}
            >
              Refresh
            </button>
          </div>
        </header>

        <div className="mt-4 grid gap-2 sm:grid-cols-4">
          <MemoryCountTile label="Pending" value={pendingReview.length} tone="var(--yellow)" />
          <MemoryCountTile label="Confirmed" value={confirmedReview.length} tone="var(--green)" />
          <MemoryCountTile label="Rejected/expired" value={rejectedOrExpired.length} tone="var(--red)" />
          <MemoryCountTile label="Sources" value={sourceCount} />
        </div>

        {error ? <div className="alert alert-error mt-4">{error}</div> : null}
        {notice && !error ? <div className="alert alert-success mt-4">{notice}</div> : null}
      </section>

      <section className="grid min-w-0 items-start gap-4 xl:grid-cols-[minmax(0,1fr)_28rem]">
        <div className="min-w-0 space-y-4">
          <StrategySnapshotPanel
            review={strategyReview}
            isReviewing={isReviewingStrategy}
            isDeciding={busyId !== null && busyId === strategyReview?.pendingEntryId}
            onRun={runStrategyReview}
            onDecide={decideStrategyReview}
          />

          <MemorySectionShell
            eyebrow="Pending proposals"
            title="Needs confirmation"
            count={pendingReview.length}
            empty="No pending proposals. New agent suggestions will wait here before becoming reusable memory."
          >
            {pendingReview.map((entry) => {
              const existing = confirmedByKindKey.get(`${entry.kind}\u0000${entry.key}`);
              return (
                <PendingProposalCard
                  key={entry.id}
                  entry={entry}
                  existing={existing}
                  busy={busyId === entry.id}
                  onResolve={resolveProposal}
                />
              );
            })}
          </MemorySectionShell>

          <MemorySectionShell
            eyebrow="Confirmed memory"
            title="Reusable facts and preferences"
            count={confirmedReview.length}
            empty="No confirmed memory yet. Confirmed proposals will appear here grouped by kind."
          >
            {grouped.map((group) => (
              <section key={group.kind} className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.32)] p-3">
                <h3 className="text-sm font-semibold text-[var(--text-primary)]">
                  {formatMemoryKind(group.kind)}
                  <span className="ml-2 text-xs font-normal text-[var(--text-tertiary)]">
                    {group.entries.length}
                  </span>
                </h3>
                <ul className="mt-3 space-y-2">
                  {group.entries.map((entry) => (
                    <MemoryEntryCard
                      key={entry.id}
                      entry={entry}
                      expanded={expandedId === entry.id}
                      editing={editingId === entry.id}
                      draft={draft}
                      draftValueError={draftValueError}
                      busy={busyId === entry.id}
                      onToggle={() =>
                        setExpandedId((current) =>
                          current === entry.id ? null : entry.id,
                        )
                      }
                      onStartEdit={() => startEdit(entry)}
                      onDelete={() => deleteEntry(entry.id)}
                      onDraftChange={setDraft}
                      onSave={() => saveEdit(entry.id)}
                      onCancel={cancelEdit}
                    />
                  ))}
                </ul>
              </section>
            ))}
          </MemorySectionShell>
        </div>

        <aside className="min-w-0 space-y-4 xl:sticky xl:top-20">
          <MemorySectionShell
            eyebrow="Rejected / expired"
            title="Not in active memory"
            count={rejectedOrExpired.length}
            empty="No rejected or expired memory."
          >
            {rejectedOrExpired.map((entry) => (
              <MemoryArchiveItem
                key={entry.id}
                entry={entry}
                referenceTimeMs={memoryReferenceTimeMs}
              />
            ))}
          </MemorySectionShell>

          <SourceEvidencePanel entries={evidenceEntries} />
        </aside>
      </section>
    </div>
  );
}
