"use client";

import { useCallback, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  formatMemoryKind,
  MemoryArchiveItem,
  MemoryCountTile,
  MemoryEntryCard,
  MemorySectionShell,
  PendingProposalCard,
  SourceEvidencePanel,
} from "@/components/profile-memory-review-sections";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import { isAgentReviewEntry } from "@/lib/agent-review";
import type { UserMemoryEntry } from "@/lib/profile";

type ProfileMemoryPanelProps = {
  proxyPrefix: string | null;
  email: string;
  displayName: string;
  initialMemory: UserMemoryEntry[];
  initialPendingMemory?: UserMemoryEntry[];
};

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
  const [isLoading, setIsLoading] = useState(false);
  const [memoryReferenceTimeMs] = useState(() => Date.now());

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

      if (pendingResponse.ok) {
        setPending((await pendingResponse.json()) as UserMemoryEntry[]);
      }
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Couldn't load memory.");
    } finally {
      setIsLoading(false);
    }
  }, [proxyPrefix]);

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
      }
      setConfirmed((prev) =>
        prev.filter(
          (entry) =>
            !confirmedReview.some((confirmedEntry) => confirmedEntry.id === entry.id),
        ),
      );
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
              Agents can propose memory, and you can remove any confirmed entry here.
            </p>
          </div>
          <div className="flex flex-wrap items-center justify-end gap-2 text-xs text-[var(--text-tertiary)]">
            {isLoading ? <span>Refreshing...</span> : null}
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
