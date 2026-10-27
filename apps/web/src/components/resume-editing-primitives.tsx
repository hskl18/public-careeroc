"use client";

import { useState } from "react";
import type { ResumeStructuredDataPatchOperation } from "@/lib/resumes";

export type ResumePatchFn = (
  operations: ResumeStructuredDataPatchOperation[],
  note?: string | null,
) => Promise<boolean>;

export function EditableText({
  label,
  value,
  path,
  canEdit,
  onPatch,
  noteLabel,
  multiline = false,
  placeholder = "Not detected",
  maxLength = 300,
}: {
  label?: string;
  value: string;
  path: string;
  canEdit: boolean;
  onPatch: ResumePatchFn;
  noteLabel?: string;
  multiline?: boolean;
  placeholder?: string;
  maxLength?: number;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(value);
  const [isSaving, setIsSaving] = useState(false);

  function startEdit() {
    setDraft(value);
    setIsEditing(true);
  }

  function cancelEdit() {
    setDraft(value);
    setIsEditing(false);
  }

  async function save() {
    const trimmed = draft.trim();
    if (trimmed === (value ?? "").trim()) {
      setIsEditing(false);
      return;
    }
    setIsSaving(true);
    const ok = await onPatch(
      [{ op: "replace", path, value: trimmed }],
      `Edited ${noteLabel ?? label ?? path}`,
    );
    setIsSaving(false);
    if (ok) setIsEditing(false);
  }

  return (
    <div className="rounded-md border border-[var(--border)] bg-white/50 px-3 py-2">
      <div className="flex items-center justify-between gap-2">
        {label ? (
          <p className="eyebrow">{label}</p>
        ) : (
          <span className="eyebrow opacity-0">.</span>
        )}
        {canEdit && !isEditing ? (
          <button
            type="button"
            onClick={startEdit}
            className="text-xs text-[var(--accent)] hover:underline"
          >
            Edit
          </button>
        ) : null}
      </div>
      {isEditing ? (
        <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-start">
          {multiline ? (
            <textarea
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              disabled={isSaving}
              className="min-w-0 flex-1 resize-y rounded-md border border-[var(--border)] bg-white/85 px-2 py-1 text-sm leading-6 text-[var(--text-primary)]"
              maxLength={maxLength}
              rows={3}
              autoFocus
            />
          ) : (
            <input
              type="text"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              disabled={isSaving}
              className="min-w-0 flex-1 rounded-md border border-[var(--border)] bg-white/85 px-2 py-1 text-sm text-[var(--text-primary)]"
              maxLength={maxLength}
              autoFocus
            />
          )}
          <div className="flex shrink-0 gap-2">
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={() => void save()}
              disabled={isSaving}
            >
              {isSaving ? "Saving..." : "Save"}
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={cancelEdit}
              disabled={isSaving}
            >
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <p className="mt-1 break-words text-sm leading-6 text-[var(--text-primary)]">
          {value ? (
            value
          ) : (
            <span className="text-[var(--text-tertiary)]">{placeholder}</span>
          )}
        </p>
      )}
    </div>
  );
}

export function EditableStringList({
  items,
  pathPrefix,
  canEdit,
  onPatch,
  noteLabel,
  variant = "pill",
  multiline = false,
  emptyLabel,
  addPlaceholder = "Add new",
  maxLength = 500,
}: {
  items: string[];
  pathPrefix: string;
  canEdit: boolean;
  onPatch: ResumePatchFn;
  noteLabel: string;
  variant?: "pill" | "row";
  multiline?: boolean;
  emptyLabel?: string;
  addPlaceholder?: string;
  maxLength?: number;
}) {
  const [draft, setDraft] = useState("");
  const [isAdding, setIsAdding] = useState(false);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState("");
  const [busyIndex, setBusyIndex] = useState<number | null>(null);

  async function addItem() {
    const trimmed = draft.trim();
    if (!trimmed) return;
    setIsAdding(true);
    const ok = await onPatch(
      [{ op: "add", path: `${pathPrefix}/-`, value: trimmed }],
      `Added ${noteLabel}`,
    );
    setIsAdding(false);
    if (ok) setDraft("");
  }

  async function removeItem(index: number) {
    setBusyIndex(index);
    await onPatch(
      [{ op: "remove", path: `${pathPrefix}/${index}` }],
      `Removed ${noteLabel}`,
    );
    setBusyIndex(null);
  }

  async function saveEdit(index: number) {
    const trimmed = editDraft.trim();
    const original = (items[index] ?? "").trim();
    if (!trimmed || trimmed === original) {
      setEditingIndex(null);
      return;
    }
    setBusyIndex(index);
    const ok = await onPatch(
      [{ op: "replace", path: `${pathPrefix}/${index}`, value: trimmed }],
      `Edited ${noteLabel}`,
    );
    setBusyIndex(null);
    if (ok) setEditingIndex(null);
  }

  async function move(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= items.length) return;
    const next = items.slice();
    [next[index], next[target]] = [next[target] as string, next[index] as string];
    setBusyIndex(index);
    await onPatch(
      [{ op: "replace", path: pathPrefix, value: next }],
      `Reordered ${noteLabel}`,
    );
    setBusyIndex(null);
  }

  function startEdit(index: number) {
    setEditDraft(items[index] ?? "");
    setEditingIndex(index);
  }

  function cancelEdit() {
    setEditingIndex(null);
    setEditDraft("");
  }

  if (variant === "pill") {
    return (
      <div>
        {items.length === 0 && !canEdit ? (
          <p className="text-sm text-[var(--text-secondary)]">
            {emptyLabel ?? "Nothing recorded."}
          </p>
        ) : null}
        {items.length > 0 ? (
          <ul className="flex flex-wrap gap-1.5">
            {items.map((item, index) => (
              <li
                key={`${index}-${item}`}
                className="inline-flex max-w-full items-center gap-1 rounded-full border border-[var(--border)] bg-white/65 py-1 pl-3 pr-1 text-xs text-[var(--text-secondary)]"
              >
                {editingIndex === index ? (
                  <>
                    <input
                      type="text"
                      value={editDraft}
                      onChange={(event) => setEditDraft(event.target.value)}
                      disabled={busyIndex === index}
                      className="min-w-[8rem] rounded-md border border-[var(--border)] bg-white/85 px-2 py-0.5 text-xs text-[var(--text-primary)]"
                      maxLength={maxLength}
                      autoFocus
                    />
                    <button
                      type="button"
                      className="btn btn-primary btn-sm"
                      onClick={() => void saveEdit(index)}
                      disabled={busyIndex === index}
                    >
                      Save
                    </button>
                    <button
                      type="button"
                      className="rounded-full px-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)]"
                      onClick={cancelEdit}
                      disabled={busyIndex === index}
                      aria-label="Cancel"
                    >
                      ✕
                    </button>
                  </>
                ) : (
                  <>
                    <span className="min-w-0 break-words">{item}</span>
                    {canEdit ? (
                      <span className="ml-1 inline-flex items-center gap-0.5">
                        <button
                          type="button"
                          aria-label="Move left"
                          className="hidden rounded-full px-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-30 sm:inline"
                          onClick={() => void move(index, -1)}
                          disabled={busyIndex !== null || index === 0}
                        >
                          ‹
                        </button>
                        <button
                          type="button"
                          aria-label="Move right"
                          className="hidden rounded-full px-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-30 sm:inline"
                          onClick={() => void move(index, 1)}
                          disabled={busyIndex !== null || index === items.length - 1}
                        >
                          ›
                        </button>
                        <button
                          type="button"
                          aria-label={`Edit ${noteLabel}`}
                          className="rounded-full px-1.5 py-0.5 text-[var(--accent)] hover:bg-[var(--blue-soft)] sm:px-1 sm:hover:bg-transparent sm:hover:underline"
                          onClick={() => startEdit(index)}
                          disabled={busyIndex !== null}
                        >
                          <span className="sm:hidden">✎</span>
                          <span className="hidden sm:inline">Edit</span>
                        </button>
                        <button
                          type="button"
                          aria-label={`Remove ${noteLabel}`}
                          className="ml-0.5 rounded-full bg-[var(--surface-elevated)] px-1.5 py-0.5 text-[var(--text-tertiary)] hover:text-[var(--red)] disabled:opacity-40"
                          onClick={() => void removeItem(index)}
                          disabled={busyIndex !== null}
                        >
                          ×
                        </button>
                      </span>
                    ) : null}
                  </>
                )}
              </li>
            ))}
          </ul>
        ) : null}
        {canEdit ? (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void addItem();
            }}
            className="mt-2 flex gap-2"
          >
            <input
              type="text"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder={addPlaceholder}
              disabled={isAdding}
              className="min-w-0 flex-1 rounded-md border border-[var(--border)] bg-white/85 px-3 py-2 text-sm text-[var(--text-primary)] sm:px-2 sm:py-1 sm:text-xs"
              maxLength={maxLength}
            />
            <button
              type="submit"
              className="btn btn-secondary btn-sm"
              disabled={isAdding || !draft.trim()}
            >
              {isAdding ? "..." : "Add"}
            </button>
          </form>
        ) : null}
      </div>
    );
  }

  return (
    <div className="grid gap-2">
      {items.length === 0 && !canEdit ? (
        <p className="text-sm text-[var(--text-secondary)]">
          {emptyLabel ?? "Nothing recorded."}
        </p>
      ) : null}
      {items.map((item, index) => (
        <div
          key={`${index}-${item}`}
          className="flex flex-col gap-2 rounded-md border border-[var(--border)] bg-white/55 px-3 py-3 text-sm leading-6 text-[var(--text-secondary)] sm:flex-row sm:items-start sm:py-2"
        >
          {editingIndex === index ? (
            <div className="flex flex-1 flex-col gap-2 sm:flex-row sm:items-start">
              {multiline ? (
                <textarea
                  value={editDraft}
                  onChange={(event) => setEditDraft(event.target.value)}
                  disabled={busyIndex === index}
                  className="min-w-0 flex-1 resize-y rounded-md border border-[var(--border)] bg-white/85 px-2 py-1 text-sm leading-6 text-[var(--text-primary)]"
                  maxLength={maxLength}
                  rows={3}
                  autoFocus
                />
              ) : (
                <input
                  type="text"
                  value={editDraft}
                  onChange={(event) => setEditDraft(event.target.value)}
                  disabled={busyIndex === index}
                  className="min-w-0 flex-1 rounded-md border border-[var(--border)] bg-white/85 px-2 py-1 text-sm text-[var(--text-primary)]"
                  maxLength={maxLength}
                  autoFocus
                />
              )}
              <div className="flex shrink-0 gap-2">
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  onClick={() => void saveEdit(index)}
                  disabled={busyIndex === index}
                >
                  Save
                </button>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  onClick={cancelEdit}
                  disabled={busyIndex === index}
                >
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <>
              <span className="flex min-w-0 flex-1 items-start gap-2">
                <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--accent)]" />
                <span className="min-w-0 flex-1 break-words">{item}</span>
              </span>
              {canEdit ? (
                <span className="flex shrink-0 items-center justify-end gap-1 pt-1 text-[11px] sm:ml-2 sm:pt-0">
                  <button
                    type="button"
                    aria-label="Move up"
                    className="rounded-full px-1.5 py-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-30"
                    onClick={() => void move(index, -1)}
                    disabled={busyIndex !== null || index === 0}
                  >
                    ▲
                  </button>
                  <button
                    type="button"
                    aria-label="Move down"
                    className="rounded-full px-1.5 py-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-30"
                    onClick={() => void move(index, 1)}
                    disabled={busyIndex !== null || index === items.length - 1}
                  >
                    ▼
                  </button>
                  <button
                    type="button"
                    className="rounded-full px-2 py-1 text-[var(--accent)] hover:bg-[var(--blue-soft)]"
                    onClick={() => startEdit(index)}
                    disabled={busyIndex !== null}
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    className="rounded-full px-2 py-1 text-[var(--text-tertiary)] hover:bg-[var(--surface-elevated)] hover:text-[var(--red)]"
                    onClick={() => void removeItem(index)}
                    disabled={busyIndex !== null}
                  >
                    <span className="sm:hidden">×</span>
                    <span className="hidden sm:inline">Remove</span>
                  </button>
                </span>
              ) : null}
            </>
          )}
        </div>
      ))}
      {canEdit ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void addItem();
          }}
          className="flex flex-col gap-2 sm:flex-row"
        >
          {multiline ? (
            <textarea
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder={addPlaceholder}
              disabled={isAdding}
              className="min-w-0 flex-1 resize-y rounded-md border border-[var(--border)] bg-white/85 px-3 py-2 text-sm leading-6 text-[var(--text-primary)] sm:px-2 sm:py-1"
              maxLength={maxLength}
              rows={2}
            />
          ) : (
            <input
              type="text"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
              placeholder={addPlaceholder}
              disabled={isAdding}
              className="min-w-0 flex-1 rounded-md border border-[var(--border)] bg-white/85 px-3 py-2 text-sm text-[var(--text-primary)] sm:px-2 sm:py-1"
              maxLength={maxLength}
            />
          )}
          <button
            type="submit"
            className="btn btn-secondary btn-sm justify-center"
            disabled={isAdding || !draft.trim()}
          >
            {isAdding ? "..." : "Add"}
          </button>
        </form>
      ) : null}
    </div>
  );
}

export function ArrayCardControls<T>({
  index,
  items,
  pathPrefix,
  canEdit,
  onPatch,
  itemLabel,
}: {
  index: number;
  items: readonly T[];
  pathPrefix: string;
  canEdit: boolean;
  onPatch: ResumePatchFn;
  itemLabel: string;
}) {
  const [busy, setBusy] = useState(false);
  if (!canEdit) return null;

  async function move(direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= items.length) return;
    const next = items.slice();
    [next[index], next[target]] = [next[target] as T, next[index] as T];
    setBusy(true);
    await onPatch(
      [{ op: "replace", path: pathPrefix, value: next }],
      `Reordered ${itemLabel}`,
    );
    setBusy(false);
  }

  async function remove() {
    setBusy(true);
    await onPatch(
      [{ op: "remove", path: `${pathPrefix}/${index}` }],
      `Removed ${itemLabel}`,
    );
    setBusy(false);
  }

  return (
    <div className="flex flex-wrap items-center justify-end gap-1 text-[11px]">
      <button
        type="button"
        className="rounded-full border border-[var(--border)] bg-white/65 px-2 py-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-40"
        onClick={() => void move(-1)}
        disabled={busy || index === 0}
        title="Move up"
      >
        ▲
      </button>
      <button
        type="button"
        className="rounded-full border border-[var(--border)] bg-white/65 px-2 py-1 text-[var(--text-tertiary)] hover:text-[var(--text-primary)] disabled:opacity-40"
        onClick={() => void move(1)}
        disabled={busy || index === items.length - 1}
        title="Move down"
      >
        ▼
      </button>
      <button
        type="button"
        className="rounded-full border border-[var(--border)] bg-white/65 px-2 py-1 text-[var(--text-tertiary)] hover:text-[var(--red)] disabled:opacity-40"
        onClick={() => void remove()}
        disabled={busy}
        title={`Remove ${itemLabel}`}
      >
        <span className="sm:hidden">×</span>
        <span className="hidden sm:inline">Remove</span>
      </button>
    </div>
  );
}

export function AddCardButton({
  canEdit,
  onAdd,
  label,
}: {
  canEdit: boolean;
  onAdd: () => Promise<void>;
  label: string;
}) {
  const [busy, setBusy] = useState(false);
  if (!canEdit) return null;

  async function handleClick() {
    setBusy(true);
    try {
      await onAdd();
    } finally {
      setBusy(false);
    }
  }

  return (
    <button
      type="button"
      onClick={() => void handleClick()}
      disabled={busy}
      className="rounded-md border border-dashed border-[var(--border-hover)] bg-white/45 px-4 py-3 text-sm font-medium text-[var(--text-secondary)] transition hover:border-[var(--accent)] hover:bg-white/70 hover:text-[var(--text-primary)] disabled:opacity-50"
    >
      {busy ? "Adding..." : label}
    </button>
  );
}
