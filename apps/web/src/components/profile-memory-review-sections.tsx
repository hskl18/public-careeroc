import type { ReactNode } from "react";
import { LocalTime } from "@/components/local-time";
import type { UserMemoryEntry } from "@/lib/profile";

export type MemoryEditDraft = {
  value: string;
  summary: string;
  confidence: string;
  expiresAtUtc: string;
};

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

export function tryFormatMemoryJson(value: string): { isJson: boolean; preview: string; pretty: string } {
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

export function MemoryCountTile({
  label,
  value,
  tone = "var(--text-tertiary)",
}: {
  label: string;
  value: number;
  tone?: string;
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-2">
      <p className="eyebrow">{label}</p>
      <p className="mt-1 text-lg font-semibold tabular-nums" style={{ color: tone }}>
        {value}
      </p>
    </div>
  );
}

export function MemorySectionShell({
  eyebrow,
  title,
  count,
  empty,
  children,
}: {
  eyebrow: string;
  title: string;
  count: number;
  empty: string;
  children: ReactNode;
}) {
  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="eyebrow">{eyebrow}</p>
          <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
            {title}
          </h2>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          {count}
        </span>
      </div>
      <div className="mt-4 space-y-3">
        {count > 0 ? children : <MemoryEmptyState message={empty} />}
      </div>
    </section>
  );
}

export function MemoryEmptyState({ message }: { message: string }) {
  return (
    <p className="rounded-md border border-dashed border-[var(--border)] bg-[rgba(255,255,255,0.28)] p-4 text-sm leading-6 text-[var(--text-secondary)]">
      {message}
    </p>
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
            {Math.round(entry.confidence * 100)}%
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
          <div className="rounded-md border border-[var(--accent)]/30 bg-[rgba(80,160,255,0.08)] p-3">
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
  editing,
  draft,
  draftValueError,
  busy,
  onToggle,
  onStartEdit,
  onDelete,
  onDraftChange,
  onSave,
  onCancel,
}: {
  entry: UserMemoryEntry;
  expanded: boolean;
  editing: boolean;
  draft: MemoryEditDraft | null;
  draftValueError: string | null;
  busy: boolean;
  onToggle: () => void;
  onStartEdit: () => void;
  onDelete: () => void;
  onDraftChange: (draft: MemoryEditDraft) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const formatted = tryFormatMemoryJson(entry.value);

  return (
    <li className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-3">
      <button
        type="button"
        className="flex w-full items-start justify-between gap-3 text-left"
        onClick={onToggle}
        disabled={editing}
      >
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-[var(--text-primary)]">
            {entry.summary || entry.key}
          </p>
          <p className="mt-0.5 truncate text-xs text-[var(--text-tertiary)]">
            {formatted.preview}
          </p>
        </div>
        <span className="shrink-0 text-right text-xs text-[var(--text-tertiary)]">
          {formatSource(entry.sourceType)}
          <br />
          {Math.round(entry.confidence * 100)}%
        </span>
      </button>

      {expanded && !editing ? (
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
            <p>Status {entry.confirmationStatus || "confirmed"}</p>
            <p>Key {entry.key}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onStartEdit}
              disabled={busy}
            >
              Edit
            </button>
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

      {editing && draft ? (
        <div className="mt-3 space-y-3">
          <label className="block text-xs text-[var(--text-tertiary)]">
            Summary
            <input
              type="text"
              value={draft.summary}
              onChange={(event) => onDraftChange({ ...draft, summary: event.target.value })}
              className="input mt-1"
              maxLength={1000}
            />
          </label>
          <label className="block text-xs text-[var(--text-tertiary)]">
            Value
            <textarea
              value={draft.value}
              onChange={(event) => onDraftChange({ ...draft, value: event.target.value })}
              rows={8}
              className="input mt-1 font-mono text-xs"
              maxLength={2000}
            />
            {draftValueError ? (
              <span className="mt-1 block text-xs text-[var(--red)]">
                {draftValueError}
              </span>
            ) : draft.value.trim().startsWith("{") ||
              draft.value.trim().startsWith("[") ? (
              <span className="mt-1 block text-xs text-[var(--green)]">
                Valid JSON memory.
              </span>
            ) : null}
          </label>
          <div className="grid grid-cols-2 gap-2">
            <label className="block text-xs text-[var(--text-tertiary)]">
              Confidence (0-1)
              <input
                type="number"
                value={draft.confidence}
                onChange={(event) =>
                  onDraftChange({ ...draft, confidence: event.target.value })
                }
                min={0}
                max={1}
                step={0.05}
                className="input mt-1"
              />
            </label>
            <label className="block text-xs text-[var(--text-tertiary)]">
              Expires (optional)
              <input
                type="datetime-local"
                value={draft.expiresAtUtc}
                onChange={(event) =>
                  onDraftChange({ ...draft, expiresAtUtc: event.target.value })
                }
                className="input mt-1"
              />
            </label>
          </div>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={onSave}
              disabled={busy || Boolean(draftValueError)}
            >
              {busy ? "Saving..." : "Save"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onCancel}
              disabled={busy}
            >
              Cancel
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
  referenceTimeMs: number;
}) {
  const formatted = tryFormatMemoryJson(entry.value);
  const expiresAt = entry.expiresAtUtc ? Date.parse(entry.expiresAtUtc) : Number.NaN;
  const expired = Number.isFinite(expiresAt) && expiresAt < referenceTimeMs;

  return (
    <article className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3">
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
          {expired ? "Expired" : entry.confirmationStatus || "Rejected"}
        </span>
      </div>
      <p className="mt-3 line-clamp-3 text-xs leading-5 text-[var(--text-secondary)]">
        {formatted.preview}
      </p>
      <p className="mt-2 text-[11px] text-[var(--text-tertiary)]">
        Updated {formatMemoryDate(entry.updatedAtUtc)}
      </p>
    </article>
  );
}

export function SourceEvidencePanel({ entries }: { entries: UserMemoryEntry[] }) {
  const sources = Array.from(
    entries.reduce((buckets, entry) => {
      const source = entry.sourceType || "unknown";
      const bucket = buckets.get(source) ?? [];
      bucket.push(entry);
      buckets.set(source, bucket);
      return buckets;
    }, new Map<string, UserMemoryEntry[]>()),
  ).sort((a, b) => b[1].length - a[1].length);

  return (
    <section className="card app-workspace-panel p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="eyebrow">Source evidence</p>
          <h2 className="mt-2 text-lg font-semibold text-[var(--text-primary)]">
            Memory provenance
          </h2>
        </div>
        <span className="badge bg-[var(--surface-elevated)] text-[var(--text-tertiary)]">
          {entries.length}
        </span>
      </div>

      {entries.length === 0 ? (
        <MemoryEmptyState message="No memory evidence metadata is available yet." />
      ) : (
        <div className="mt-4 space-y-3">
          {sources.map(([source, sourceEntries]) => (
            <article
              key={source}
              className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-3"
            >
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-semibold text-[var(--text-primary)]">
                  {formatSource(source)}
                </p>
                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                  {sourceEntries.length}
                </span>
              </div>
              <ul className="mt-3 space-y-2">
                {sourceEntries.slice(0, 4).map((entry) => (
                  <li key={entry.id} className="text-xs leading-5 text-[var(--text-secondary)]">
                    <span className="font-medium text-[var(--text-primary)]">
                      {entry.summary || entry.key}
                    </span>
                  <br />
                  {entry.confirmationStatus || "confirmed"} ·{" "}
                  {Math.round(entry.confidence * 100)}% ·{" "}
                  {formatMemoryDate(entry.updatedAtUtc)}
                  {entry.sourceId ? (
                    <>
                      <br />
                      Source ID {entry.sourceId}
                    </>
                  ) : null}
                </li>
              ))}
              </ul>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
