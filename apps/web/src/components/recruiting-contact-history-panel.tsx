"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { fetchWithInFlightDedup, readErrorMessage } from "@/lib/client-fetch";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import type {
  RecruitingContact,
  UpdateRecruitingContactRequest,
} from "@/lib/recruiting-contacts";

type RecruitingContactHistoryPanelProps = {
  initialContacts: RecruitingContact[];
  source: "api" | "fallback";
  proxyPrefix: string | null;
  timeZoneId: string;
};

const contactTypeOptions = [
  ["", "Unclassified"],
  ["Recruiter", "Recruiter"],
  ["Coordinator", "Coordinator"],
  ["HiringManager", "Hiring manager"],
  ["Interviewer", "Interviewer"],
] as const;

function formatTime(value: string, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "No timestamp");
}

function getContactLabel(contact: RecruitingContact) {
  return contact.displayName || contact.email;
}

function toReadableType(value: string | null) {
  if (!value) return "Unclassified";
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function RecruitingContactHistoryPanel({
  initialContacts,
  source,
  proxyPrefix,
  timeZoneId,
}: RecruitingContactHistoryPanelProps) {
  const [contacts, setContacts] = useState(initialContacts);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<UpdateRecruitingContactRequest>({
    displayName: null,
    companyHint: null,
    roleHint: null,
    contactType: null,
  });
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const sortedContacts = useMemo(
    () =>
      [...contacts].sort(
        (left, right) =>
          new Date(right.lastSeenAtUtc).getTime() -
          new Date(left.lastSeenAtUtc).getTime(),
      ),
    [contacts],
  );
  const canPersist = source === "api" && Boolean(proxyPrefix);

  function startEditing(contact: RecruitingContact) {
    setEditingId(contact.id);
    setDraft({
      email: contact.email,
      displayName: contact.displayName ?? "",
      companyHint: contact.companyHint ?? "",
      roleHint: contact.roleHint ?? "",
      contactType: contact.contactType ?? "",
    });
    setNotice(null);
    setError(null);
  }

  async function saveContact(contact: RecruitingContact) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    setPendingId(contact.id);
    setNotice(null);
    setError(null);

    try {
      const response = await fetchWithInFlightDedup(
        `${proxyPrefix}/api/recruiting-contacts/${contact.id}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(draft),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const updated = (await response.json()) as RecruitingContact;

      setContacts((current) => {
        const sourceRecentEmails =
          current.find((candidate) => candidate.id === contact.id)?.recentEmails ?? [];
        const existingRecentEmails =
          current.find((candidate) => candidate.id === updated.id)?.recentEmails ?? [];

        return [
          ...current.filter(
            (candidate) => candidate.id !== contact.id && candidate.id !== updated.id,
          ),
          {
            ...updated,
            recentEmails:
              updated.recentEmails.length > 0
                ? updated.recentEmails
                : existingRecentEmails.length > 0
                  ? existingRecentEmails
                  : sourceRecentEmails,
          },
        ];
      });
      setEditingId(null);
      setNotice("Recruiting contact alias updated.");
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Contact update failed.");
    } finally {
      setPendingId(null);
    }
  }

  return (
    <section className="card p-4">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">Contacts</h2>
        <span className="text-xs tabular-nums text-[var(--text-tertiary)]">{contacts.length}</span>
      </div>

      {!canPersist ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-3 py-2 text-xs leading-5 text-[var(--text-tertiary)]">
          Read-only.
        </p>
      ) : null}
      {notice ? (
        <p className="mt-4 rounded-md border border-[var(--green-soft)] bg-[var(--green-soft)] px-3 py-2 text-xs font-medium text-[var(--green)]">
          {notice}
        </p>
      ) : null}
      {error ? (
        <p className="mt-4 rounded-md border border-[var(--red-soft)] bg-[var(--red-soft)] px-3 py-2 text-xs font-medium text-[var(--red)]">
          {error}
        </p>
      ) : null}

      {sortedContacts.length === 0 ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-4 py-3 text-sm text-[var(--text-tertiary)]">
          No contacts yet.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          {sortedContacts.map((contact) => {
            const isEditing = editingId === contact.id;
            const isPending = pendingId === contact.id;

            return (
              <article
                key={contact.id}
                className="py-2.5"
              >
                <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <h3 className="text-sm font-medium text-[var(--text-primary)]">
                        {getContactLabel(contact)}
                      </h3>
                      {contact.contactType && toReadableType(contact.contactType) !== "Unclassified" ? (
                        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                          {toReadableType(contact.contactType)}
                        </span>
                      ) : null}
                    </div>
                    <p className="break-all text-xs text-[var(--text-tertiary)]">
                      {getContactLabel(contact) === contact.email ? "" : `${contact.email} · `}
                      {contact.messageCount} message{contact.messageCount === 1 ? "" : "s"} ·{" "}
                      {formatTime(contact.lastSeenAtUtc, timeZoneId)}
                    </p>
                  </div>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={!canPersist || isPending}
                    onClick={() => (isEditing ? setEditingId(null) : startEditing(contact))}
                  >
                    {isEditing ? "Cancel" : "Edit"}
                  </button>
                </div>

                {isEditing ? (
                  <div className="mt-4 grid gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] p-3 md:grid-cols-2">
                    <label className="space-y-1 text-xs font-medium text-[var(--text-secondary)]">
                      Email alias
                      <input
                        className="input w-full"
                        value={draft.email ?? ""}
                        onChange={(event) =>
                          setDraft((current) => ({
                            ...current,
                            email: event.target.value,
                          }))
                        }
                      />
                    </label>
                    <label className="space-y-1 text-xs font-medium text-[var(--text-secondary)]">
                      Display name
                      <input
                        className="input w-full"
                        value={draft.displayName ?? ""}
                        onChange={(event) =>
                          setDraft((current) => ({
                            ...current,
                            displayName: event.target.value,
                          }))
                        }
                      />
                    </label>
                    <label className="space-y-1 text-xs font-medium text-[var(--text-secondary)]">
                      Contact type
                      <select
                        className="input w-full"
                        value={draft.contactType ?? ""}
                        onChange={(event) =>
                          setDraft((current) => ({
                            ...current,
                            contactType: event.target.value,
                          }))
                        }
                      >
                        {contactTypeOptions.map(([value, label]) => (
                          <option key={value} value={value}>
                            {label}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label className="space-y-1 text-xs font-medium text-[var(--text-secondary)]">
                      Company alias
                      <input
                        className="input w-full"
                        value={draft.companyHint ?? ""}
                        onChange={(event) =>
                          setDraft((current) => ({
                            ...current,
                            companyHint: event.target.value,
                          }))
                        }
                      />
                    </label>
                    <label className="space-y-1 text-xs font-medium text-[var(--text-secondary)]">
                      Role alias
                      <input
                        className="input w-full"
                        value={draft.roleHint ?? ""}
                        onChange={(event) =>
                          setDraft((current) => ({
                            ...current,
                            roleHint: event.target.value,
                          }))
                        }
                      />
                    </label>
                    <div className="flex flex-wrap gap-2 md:col-span-2">
                      <button
                        type="button"
                        className="btn btn-primary btn-sm"
                        disabled={isPending}
                        onClick={() => saveContact(contact)}
                      >
                        {isPending ? "Saving" : "Save correction"}
                      </button>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        disabled={isPending}
                        onClick={() => setEditingId(null)}
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                ) : null}

                {contact.recentEmails[0]?.jobApplicationId ? (
                  <Link
                    href={`/app/applications/${contact.recentEmails[0].jobApplicationId}`}
                    className="mt-0.5 block truncate max-sm:min-h-6 max-sm:leading-6 text-xs text-[var(--accent)] hover:underline"
                  >
                    {contact.recentEmails[0].subject}
                  </Link>
                ) : null}
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
