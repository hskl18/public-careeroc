"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import {
  mapRecruitingContact,
  type RecruitingContact,
  type UpdateRecruitingContactRequest,
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

function formatConfidence(value: number | null) {
  if (value === null) return "n/a";
  return `${Math.round(value * 100)}%`;
}

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
  const [draft, setDraft] = useState<UpdateRecruitingContactRequest>({});
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
        throw new Error(`Contact update failed with ${response.status}`);
      }

      const updated = mapRecruitingContact(await response.json());
      if (!updated) {
        throw new Error("Contact update returned an invalid response.");
      }

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
    <section className="card app-workspace-panel p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="eyebrow">Recruiting contacts</p>
          <h2 className="mt-1.5 text-base font-semibold text-[var(--text-primary)]">
            Contact history and aliases
          </h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-[var(--text-secondary)]">
            Review the recruiters, coordinators, interviewers, and hiring managers
            inferred from recruiting mail. Alias corrections stay scoped to your
            workspace and do not expose raw email bodies.
          </p>
        </div>
        <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
          {contacts.length} contacts
        </span>
      </div>

      {!canPersist ? (
        <p className="mt-4 rounded-md border border-[var(--border)] bg-[var(--surface-overlay)] px-3 py-2 text-xs leading-5 text-[var(--text-tertiary)]">
          Contact aliases are read-only until the API proxy is available.
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
          No recruiting contacts yet. They appear after accepted recruiting
          messages are analyzed and linked to your workspace.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          {sortedContacts.map((contact) => {
            const isEditing = editingId === contact.id;
            const isPending = pendingId === contact.id;

            return (
              <article
                key={contact.id}
                className="rounded-[var(--radius-md)] border border-[var(--border)] bg-[var(--surface)] p-4"
              >
                <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <h3 className="text-sm font-semibold text-[var(--text-primary)]">
                        {getContactLabel(contact)}
                      </h3>
                      <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                        {toReadableType(contact.contactType)}
                      </span>
                    </div>
                    <p className="mt-1 break-all text-xs text-[var(--text-tertiary)]">
                      {contact.email}
                    </p>
                    <div className="mt-3 flex flex-wrap gap-1.5">
                      <span className="badge bg-[var(--accent-soft)] text-[var(--accent)]">
                        confidence {formatConfidence(contact.confidence)}
                      </span>
                      <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                        {contact.messageCount} messages
                      </span>
                      <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
                        last seen {formatTime(contact.lastSeenAtUtc, timeZoneId)}
                      </span>
                    </div>
                  </div>
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={!canPersist || isPending}
                    onClick={() => (isEditing ? setEditingId(null) : startEditing(contact))}
                  >
                    {isEditing ? "Cancel" : "Correct alias"}
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

                <div className="mt-4 grid gap-2">
                  {(contact.recentEmails.length > 0
                    ? contact.recentEmails
                    : []
                  ).map((email) => (
                    <div
                      key={email.id}
                      className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2"
                    >
                      <div className="flex flex-col gap-1 sm:flex-row sm:items-start sm:justify-between">
                        <div className="min-w-0">
                          <p className="truncate text-sm font-medium text-[var(--text-primary)]">
                            {email.subject}
                          </p>
                          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                            {email.company || "Unknown company"}
                            {email.role ? ` · ${email.role}` : ""}
                          </p>
                        </div>
                        {email.jobApplicationId ? (
                          <Link
                            href={`/app/applications/${email.jobApplicationId}`}
                            className="btn btn-secondary btn-sm shrink-0"
                          >
                            Open app
                          </Link>
                        ) : null}
                      </div>
                    </div>
                  ))}
                  {contact.recentEmails.length === 0 ? (
                    <p className="rounded-md border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2 text-xs text-[var(--text-tertiary)]">
                      No recent linked messages returned for this contact.
                    </p>
                  ) : null}
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
