import type { ReactNode } from "react";
import { LocalTime } from "@/components/local-time";
import { SvgAsset, mascotAssets } from "@/components/svg-asset";
import type { UserMemoryEntry } from "@/lib/profile";
import { formatConfidence, formatEnumLabel } from "@/lib/labels";

const kindLabels: Record<string, string> = {
  preference: "Preferences",
  fact: "Facts",
  goal: "Goals",
  resume: "Resume",
  orchestrator: "Orchestrator",
  workflow: "Workflow",
  insight: "Insights",
};

const sourceLabels: Record<string, string> = {
  user: "You",
  resume_evaluation: "Resume agent",
  career_orchestrator: "Career orchestrator",
  inbox_agent: "Inbox agent",
};

export function formatMemoryKind(kind: string): string {
  return kindLabels[kind] ?? kind.replace(/_/g, " ");
}

function formatSource(sourceType: string | null | undefined): string {
  if (!sourceType) return "Workspace evidence";
  return sourceLabels[sourceType] ?? sourceType.replace(/_/g, " ");
}

function tryFormatMemoryJson(value: string): { isJson: boolean; preview: string; pretty: string } {
  const trimmed = value.trim();
  if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) {
    return { isJson: false, preview: value, pretty: value };
  }
  try {
    const parsed = JSON.parse(trimmed);
    const pretty = JSON.stringify(parsed, null, 2);
    if (Array.isArray(parsed)) {
      return { isJson: true, preview: `${parsed.length} item${parsed.length === 1 ? "" : "s"}`, pretty };
    }
    const keys = Object.keys(parsed);
    return {
      isJson: true,
      preview: keys.slice(0, 3).join(", ") + (keys.length > 3 ? `, +${keys.length - 3} more` : ""),
      pretty,
    };
  } catch {
    return { isJson: false, preview: value, pretty: value };
  }
}

function formatMemoryDate(value: string | null | undefined) {
  return value && Number.isFinite(Date.parse(value)) ? <LocalTime value={value} /> : "Not recorded";
}

export function MemorySectionShell({
  title,
  count,
  empty,
  action,
  children,
}: {
  title: string;
  count: number;
  empty: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="card flex min-h-0 flex-col p-0">
      <div className="flex min-h-10 shrink-0 items-center gap-2 px-4 pt-3">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">{title}</h2>
        <span className="text-xs tabular-nums text-[var(--text-tertiary)]">{count}</span>
        <div className="ml-auto">{action}</div>
      </div>
      <div className="workspace-scroll-region flex min-h-0 flex-1 flex-col gap-2 px-4 pb-4 pt-2">
        {count > 0 ? children : <MemoryEmptyState message={empty} />}
      </div>
    </section>
  );
}

function MemoryEmptyState({ message }: { message: string }) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-2 py-6 text-center">
      <SvgAsset src={mascotAssets.resumeBuddyCard} decorative className="h-16 w-16" />
      <p className="text-sm text-[var(--text-tertiary)]">{message}</p>
    </div>
  );
}

export function PendingProposalCard({
  entry,
  existing,
  busy,
  onResolve,
}: {
  entry: UserMemoryEntry;
  existing: UserMemoryEntry | undefined;
  busy: boolean;
  onResolve: (entryId: string, action: "confirm" | "reject") => void;
}) {
  const formatted = tryFormatMemoryJson(entry.value);
  const existingFormatted = existing ? tryFormatMemoryJson(existing.value) : null;

  return (
    <article className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-semibold text-[var(--text-primary)]">
            {entry.summary || entry.key}
          </p>
          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
            {formatMemoryKind(entry.kind)} · {formatSource(entry.sourceType)} ·{" "}
            {formatConfidence(entry.confidence)}
          </p>
        </div>
        <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
          Pending
        </span>
      </div>

      {formatted.isJson ? (
        <pre className="mt-3 max-h-64 overflow-auto rounded-md bg-[var(--surface-strong)] p-3 text-xs text-[var(--text-secondary)]">
          {formatted.pretty}
        </pre>
      ) : (
        <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-[var(--text-secondary)]">
          {entry.value}
        </p>
      )}

      {existing ? (
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-3">
            <p className="eyebrow">Current</p>
            <p className="mt-2 text-xs leading-5 text-[var(--text-secondary)]">
              {existingFormatted?.preview ?? existing.value}
            </p>
          </div>
          <div className="rounded-md border border-[var(--accent)]/30 bg-[var(--blue-soft)] p-3">
            <p className="eyebrow">Proposed</p>
            <p className="mt-2 text-xs leading-5 text-[var(--text-secondary)]">
              {formatted.preview}
            </p>
          </div>
        </div>
      ) : null}

      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          className="btn btn-primary btn-sm"
          onClick={() => onResolve(entry.id, "confirm")}
          disabled={busy}
        >
          {busy ? "Saving..." : "Confirm"}
        </button>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={() => onResolve(entry.id, "reject")}
          disabled={busy}
        >
          Reject
        </button>
      </div>
    </article>
  );
}

export function MemoryEntryCard({
  entry,
  expanded,
  busy,
  onToggle,
  onDelete,
}: {
  entry: UserMemoryEntry;
  expanded: boolean;
  busy: boolean;
  onToggle: () => void;
  onDelete: () => void;
}) {
  const formatted = tryFormatMemoryJson(entry.value);

  return (
    <li className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-3">
      <button
        type="button"
        className="flex w-full items-start justify-between gap-3 text-left"
        onClick={onToggle}
      >
        <div className="min-w-0 flex-1">
          <p className="line-clamp-2 text-sm font-medium text-[var(--text-primary)]">
            {entry.summary || entry.key}
          </p>
          <p className="mt-0.5 truncate text-xs text-[var(--text-tertiary)]">
            {formatted.preview}
          </p>
        </div>
        <span className="shrink-0 text-right text-xs text-[var(--text-tertiary)]">
          {formatSource(entry.sourceType)}
          <br />
          {formatConfidence(entry.confidence)}
        </span>
      </button>

      {expanded ? (
        <div className="mt-3 space-y-3">
          {formatted.isJson ? (
            <pre className="max-h-72 overflow-x-auto rounded-md bg-[var(--surface-strong)] p-3 text-xs text-[var(--text-secondary)]">
              {formatted.pretty}
            </pre>
          ) : (
            <p className="whitespace-pre-wrap text-sm leading-6 text-[var(--text-secondary)]">
              {entry.value}
            </p>
          )}
          <div className="grid gap-2 text-xs text-[var(--text-tertiary)] sm:grid-cols-3">
            <p>Updated {formatMemoryDate(entry.updatedAtUtc)}</p>
            <p>Status {formatEnumLabel(entry.confirmationStatus || "confirmed")}</p>
            <p>Key {entry.key}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onDelete}
              disabled={busy}
            >
              Remove
            </button>
          </div>
        </div>
      ) : null}
    </li>
  );
}

export function MemoryArchiveItem({
  entry,
  referenceTimeMs,
}: {
  entry: UserMemoryEntry;
  referenceTimeMs: number | null;
}) {
  const formatted = tryFormatMemoryJson(entry.value);
  const expiresAt = entry.expiresAtUtc ? Date.parse(entry.expiresAtUtc) : Number.NaN;
  const expired =
    referenceTimeMs !== null && Number.isFinite(expiresAt) && expiresAt < referenceTimeMs;

  return (
    <article className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium text-[var(--text-primary)]">
            {entry.summary || entry.key}
          </p>
          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
            {formatMemoryKind(entry.kind)} · {formatSource(entry.sourceType)}
          </p>
        </div>
        <span className="badge bg-[var(--red-soft)] text-[var(--red)]">
          {expired ? "Expired" : formatEnumLabel(entry.confirmationStatus || "rejected")}
        </span>
      </div>
      <p className="mt-3 line-clamp-3 text-xs leading-5 text-[var(--text-secondary)]">
        {formatted.preview}
      </p>
      <p className="mt-2 text-xs text-[var(--text-tertiary)]">
        Updated {formatMemoryDate(entry.updatedAtUtc)}
      </p>
    </article>
  );
}
