"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { UserMemoryEntry } from "@/lib/profile";

type InlineMemoryProposalsProps = {
  proposals?: UserMemoryEntry[] | null;
  proxyPrefix: string | null;
  title?: string;
  emptyCopy?: string;
};

async function readErrorMessage(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

function formatMemoryValue(entry: UserMemoryEntry) {
  if (entry.summary?.trim()) {
    return entry.summary.trim();
  }

  const rawValue = entry.value?.trim();
  if (!rawValue) {
    return "No value recorded.";
  }

  if (!rawValue.startsWith("{") && !rawValue.startsWith("[")) {
    return rawValue;
  }

  try {
    const parsed = JSON.parse(rawValue) as unknown;
    if (typeof parsed === "string") return parsed;
    if (Array.isArray(parsed)) {
      return parsed
        .filter((item): item is string => typeof item === "string")
        .slice(0, 3)
        .join(", ");
    }
    if (parsed && typeof parsed === "object") {
      return Object.entries(parsed)
        .slice(0, 4)
        .map(([key, value]) => `${key}: ${String(value)}`)
        .join(" · ");
    }
  } catch {}

  return rawValue;
}

function formatConfidence(value: number) {
  if (!Number.isFinite(value)) return "n/a";
  return `${Math.round(value * 100)}%`;
}

function formatKind(value: string) {
  return value.replace(/[_-]+/g, " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function InlineMemoryProposals({
  proposals,
  proxyPrefix,
  title = "Memory proposals",
  emptyCopy = "No scoped memory proposals are waiting here.",
}: InlineMemoryProposalsProps) {
  const router = useRouter();
  const initialProposals = useMemo(
    () =>
      (proposals ?? []).filter(
        (proposal) => proposal.confirmationStatus.toLowerCase() === "pending",
      ),
    [proposals],
  );
  const [items, setItems] = useState(initialProposals);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function resolveProposal(entry: UserMemoryEntry, action: "confirm" | "reject") {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    setBusyId(entry.id);
    setError(null);
    setNotice(null);
    setItems((current) => current.filter((item) => item.id !== entry.id));

    try {
      const response = await fetch(`${proxyPrefix}/api/profile/memory/proposals/${entry.id}`, {
        method: "PATCH",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ action }),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      setNotice(action === "confirm" ? "Memory confirmed." : "Proposal rejected.");
      router.refresh();
    } catch (resolveError) {
      setItems((current) =>
        current.some((item) => item.id === entry.id) ? current : [entry, ...current],
      );
      setError(
        resolveError instanceof Error
          ? resolveError.message
          : "Couldn't resolve memory proposal.",
      );
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="eyebrow">{title}</p>
          <h2 className="mt-1.5 text-base font-semibold text-[var(--text-primary)]">
            Confirm reusable facts
          </h2>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          {items.length}
        </span>
      </div>

      {notice ? <p className="mt-3 text-sm font-medium text-[var(--green)]">{notice}</p> : null}
      {error ? <p className="mt-3 text-sm font-medium text-[var(--red)]">{error}</p> : null}

      {items.length === 0 ? (
        <p className="mt-3 text-sm leading-6 text-[var(--text-tertiary)]">
          {emptyCopy}
        </p>
      ) : (
        <div className="mt-4 grid gap-3">
          {items.map((proposal) => (
            <article key={proposal.id} className="card-elevated p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="eyebrow">{formatKind(proposal.kind || "memory")}</p>
                  <h3 className="mt-1 break-words text-sm font-semibold text-[var(--text-primary)]">
                    {proposal.key}
                  </h3>
                </div>
                <span className="badge bg-[var(--accent-soft)] text-[var(--accent)]">
                  {formatConfidence(proposal.confidence)}
                </span>
              </div>
              <p className="mt-3 text-sm leading-6 text-[var(--text-secondary)]">
                {formatMemoryValue(proposal)}
              </p>
              <p className="mt-2 break-all text-xs leading-5 text-[var(--text-tertiary)]">
                Source: {proposal.sourceType ?? "workspace evidence"}
                {proposal.sourceId ? ` · ${proposal.sourceId}` : ""}
              </p>
              <div className="mt-3 flex flex-wrap gap-2">
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  disabled={!proxyPrefix || busyId !== null}
                  onClick={() => void resolveProposal(proposal, "confirm")}
                >
                  {busyId === proposal.id ? "Saving..." : "Confirm"}
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={!proxyPrefix || busyId !== null}
                  onClick={() => void resolveProposal(proposal, "reject")}
                >
                  Reject
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
