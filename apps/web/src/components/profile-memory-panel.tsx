"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  formatMemoryKind,
  MemoryArchiveItem,
  MemoryEntryCard,
  MemorySectionShell,
  PendingProposalCard,
} from "@/components/profile-memory-review-sections";
import { isAgentReviewEntry } from "@/lib/agent-review";
import type { UserMemoryEntry } from "@/lib/profile";
import { readErrorMessage } from "@/lib/client-fetch";
import { useHydrated } from "@/lib/use-hydrated";

type ProfileMemoryPanelProps = {
  proxyPrefix: string | null;
  initialMemory: UserMemoryEntry[];
  initialPendingMemory?: UserMemoryEntry[];
};

// referenceTimeMs is null until hydration: the server's clock and the browser's can disagree.
function isRejectedOrExpired(entry: UserMemoryEntry, referenceTimeMs: number | null) {
  const status = entry.confirmationStatus.toLowerCase();
  if (status === "rejected" || status === "expired") return true;
  if (!entry.expiresAtUtc || referenceTimeMs === null) return false;
  const expiresAt = Date.parse(entry.expiresAtUtc);
  return Number.isFinite(expiresAt) && expiresAt < referenceTimeMs;
}

function isPending(entry: UserMemoryEntry) {
  return entry.confirmationStatus.toLowerCase() === "pending";
}

export function ProfileMemoryPanel({
  proxyPrefix,
  initialMemory,
  initialPendingMemory = [],
}: ProfileMemoryPanelProps) {
  const router = useRouter();
  const [confirmed, setConfirmed] = useState<UserMemoryEntry[]>(initialMemory);
  const [pending, setPending] = useState<UserMemoryEntry[]>(initialPendingMemory);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const hydrated = useHydrated();
  const [browserTimeMs] = useState(() => Date.now());
  const memoryReferenceTimeMs = hydrated ? browserTimeMs : null;

  const confirmedReview = useMemo(
    () =>
      confirmed.filter(
        (entry) => !isAgentReviewEntry(entry) && !isRejectedOrExpired(entry, memoryReferenceTimeMs),
      ),
    [confirmed, memoryReferenceTimeMs],
  );
  const pendingReview = useMemo(
    () =>
      pending.filter(
        (entry) =>
          isPending(entry) &&
          !isAgentReviewEntry(entry) &&
          !isRejectedOrExpired(entry, memoryReferenceTimeMs),
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
      setNotice("Memory removed.");
      router.refresh();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Couldn't remove memory.");
    } finally {
      setBusyId(null);
    }
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
        // One at a time, so a later failure still shows the ones already removed.
        setConfirmed((prev) => prev.filter((item) => item.id !== entry.id));
      }
      setExpandedId(null);
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
        setPending((prev) => prev.filter((item) => item.id !== entry.id));
      }
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
      <section className="card p-4">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">Long-term memory</h2>
        <p className="mt-3 text-sm text-[var(--text-secondary)]">
          Memory is unavailable until the live API is connected.
        </p>
      </section>
    );
  }

  return (
    <>
      {error ? <div role="alert" className="alert alert-error shrink-0">{error}</div> : null}
      {notice && !error ? <div className="alert alert-success shrink-0">{notice}</div> : null}
      <p role="status" className="sr-only">{notice ?? ""}</p>

      <div className="grid min-h-0 flex-1 gap-3 sm:gap-4 md:grid-cols-3 md:[grid-template-rows:minmax(0,1fr)]">
        <MemorySectionShell
          title="Pending"
          count={pendingReview.length}
          empty="Nothing pending."
          action={
            pendingReview.length > 0 ? (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={rejectPendingMemory}
                disabled={busyId === "reject-pending"}
              >
                {busyId === "reject-pending" ? "Rejecting..." : "Reject all"}
              </button>
            ) : null
          }
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
          title="Confirmed"
          count={confirmedReview.length}
          empty="Nothing yet."
          action={
            confirmedReview.length > 0 ? (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                onClick={clearConfirmedMemory}
                disabled={busyId === "clear-confirmed"}
              >
                {busyId === "clear-confirmed" ? "Clearing..." : "Clear all"}
              </button>
            ) : null
          }
        >
          {grouped.map((group) => (
            <section key={group.kind}>
              <h3 className="text-sm font-semibold text-[var(--text-primary)]">
                {formatMemoryKind(group.kind)}
                <span className="ml-2 text-xs text-[var(--text-tertiary)]">
                  {group.entries.length}
                </span>
              </h3>
              <ul className="mt-3 space-y-2">
                {group.entries.map((entry) => (
                  <MemoryEntryCard
                    key={entry.id}
                    entry={entry}
                    expanded={expandedId === entry.id}
                    busy={busyId === entry.id}
                    onToggle={() =>
                      setExpandedId((current) =>
                        current === entry.id ? null : entry.id,
                      )
                    }
                    onDelete={() => deleteEntry(entry.id)}
                  />
                ))}
              </ul>
            </section>
          ))}
        </MemorySectionShell>

        <MemorySectionShell
          title="Rejected"
          count={rejectedOrExpired.length}
          empty="Nothing rejected."
        >
          {rejectedOrExpired.map((entry) => (
            <MemoryArchiveItem
              key={entry.id}
              entry={entry}
              referenceTimeMs={memoryReferenceTimeMs}
            />
          ))}
        </MemorySectionShell>
      </div>
    </>
  );
}
