"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { InboxAccountSetup } from "@/lib/inbox-accounts";

type DeleteImportedDataResponse = {
  emailAddress: string;
  deletedEmails: number;
  deletedProcessingJobs: number;
  unlinkedEvents: number;
  preservedApplications: number;
};

type DeleteInboxPurgeResponse = {
  emailAddress: string;
  deletedEmails: number;
  deletedProcessingJobs: number;
  deletedApplications: number;
  deletedReminders: number;
  deletedEvents: number;
  unlinkedEvents: number;
  deletedInboxAccounts: number;
};

type DataControlsPanelProps = {
  accounts: InboxAccountSetup[];
  proxyPrefix: string | null;
  canPersist: boolean;
};

export function DataControlsPanel({
  accounts,
  proxyPrefix,
  canPersist,
}: DataControlsPanelProps) {
  const router = useRouter();
  const [selectedAccountId, setSelectedAccountId] = useState(accounts[0]?.id ?? "");
  const [confirmation, setConfirmation] = useState("");
  const [purgeConfirmation, setPurgeConfirmation] = useState("");
  const [pending, setPending] = useState(false);
  const [purgePending, setPurgePending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const selectedAccount = useMemo(
    () => accounts.find((account) => account.id === selectedAccountId) ?? null,
    [accounts, selectedAccountId],
  );
  const purgePhrase = selectedAccount ? `DELETE ${selectedAccount.emailAddress}` : "";
  const confirmationMatches =
    selectedAccount !== null &&
    confirmation.trim().toLowerCase() === selectedAccount.emailAddress.toLowerCase();
  const purgeConfirmationMatches =
    selectedAccount !== null &&
    purgeConfirmation.trim().toLowerCase() === purgePhrase.toLowerCase();
  const controlsDisabled = !canPersist || pending || purgePending;

  useEffect(() => {
    queueMicrotask(() => {
      setSelectedAccountId((currentAccountId) => {
        if (accounts.length === 0) {
          return "";
        }

        return accounts.some((account) => account.id === currentAccountId)
          ? currentAccountId
          : accounts[0].id;
      });
    });
  }, [accounts]);

  async function deleteImportedData() {
    if (!proxyPrefix || !selectedAccount) {
      setError("API is not available for data deletion.");
      return;
    }

    setPending(true);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/inbox-accounts/${selectedAccount.id}/imported-data`,
        {
          method: "DELETE",
          headers: {
            "content-type": "application/json",
          },
          body: JSON.stringify({ confirmation }),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const result = (await response.json()) as DeleteImportedDataResponse;
      setNotice(
        `Deleted ${result.deletedEmails} imported email${result.deletedEmails === 1 ? "" : "s"} from ${result.emailAddress}. Preserved ${result.preservedApplications} application${result.preservedApplications === 1 ? "" : "s"}.`,
      );
      setConfirmation("");
      router.refresh();
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to delete imported data.",
      );
    } finally {
      setPending(false);
    }
  }

  async function purgeInboxAndApplications() {
    if (!proxyPrefix || !selectedAccount) {
      setError("API is not available for inbox purge.");
      return;
    }

    setPurgePending(true);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(
        `${proxyPrefix}/api/inbox-accounts/${selectedAccount.id}/purge`,
        {
          method: "DELETE",
          headers: {
            "content-type": "application/json",
          },
          body: JSON.stringify({ confirmation: purgeConfirmation }),
        },
      );

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const result = (await response.json()) as DeleteInboxPurgeResponse;
      setNotice(
        `Purged ${result.emailAddress}: deleted ${result.deletedInboxAccounts} inbox, ${result.deletedEmails} email${result.deletedEmails === 1 ? "" : "s"}, and ${result.deletedApplications} imported application${result.deletedApplications === 1 ? "" : "s"}.`,
      );
      setConfirmation("");
      setPurgeConfirmation("");
      router.refresh();
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to purge inbox data.",
      );
    } finally {
      setPurgePending(false);
    }
  }

  return (
    <section className="card space-y-6 p-5">
      <p className="eyebrow">Deletion gates</p>
      <h2 className="text-xl font-semibold">Imported data deletion</h2>
      <div className="mt-4 grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
        <div className="space-y-3">
          <label className="block text-sm font-medium text-[var(--text-primary)]">
            Gmail inbox
            <select
              className="mt-2 w-full rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2 text-sm"
              value={selectedAccountId}
              onChange={(event) => {
                setSelectedAccountId(event.target.value);
                setConfirmation("");
                setPurgeConfirmation("");
                setNotice(null);
                setError(null);
              }}
              disabled={controlsDisabled || accounts.length === 0}
            >
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.emailAddress}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm font-medium text-[var(--text-primary)]">
            Type the inbox email address
            <input
              className="mt-2 w-full rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] px-3 py-2 text-sm"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              placeholder={selectedAccount?.emailAddress ?? "inbox@example.com"}
              disabled={controlsDisabled || !selectedAccount}
            />
          </label>
          <button
            type="button"
            className="btn btn-danger"
            disabled={controlsDisabled || !confirmationMatches}
            onClick={() => void deleteImportedData()}
          >
            {pending ? <><span className="spinner" /> Deleting...</> : "Delete Imported Data"}
          </button>
        </div>
        <div className="rounded-lg border border-[var(--border)] bg-[var(--surface-elevated)] p-4">
          <h3 className="text-base font-semibold">What this removes</h3>
          <ul className="mt-3 space-y-2 text-sm text-[var(--text-secondary)]">
            <li>Imported Gmail messages and source thread links for the selected inbox.</li>
            <li>Pending, processing, and failed analysis jobs tied to those messages.</li>
            <li>OAuth tokens and history cursor, so sync remains paused until reconnect.</li>
          </ul>
          <h3 className="mt-4 text-base font-semibold">What stays</h3>
          <ul className="mt-3 space-y-2 text-sm text-[var(--text-secondary)]">
            <li>Applications, reminders, and application events.</li>
            <li>Durable audit rows recording the deletion request.</li>
          </ul>
        </div>
      </div>
      <div className="border-t border-[var(--border)] pt-5">
        <div className="grid gap-4 lg:grid-cols-[0.9fr_1.1fr]">
          <div className="space-y-3">
            <p className="eyebrow text-[var(--red)]">Explicit purge</p>
            <h3 className="text-base font-semibold">Delete inbox and imported applications</h3>
            <label className="block text-sm font-medium text-[var(--text-primary)]">
              Type the purge phrase
              <input
                className="mt-2 w-full rounded-lg border border-[var(--red)]/40 bg-[var(--surface-elevated)] px-3 py-2 text-sm"
                value={purgeConfirmation}
                onChange={(event) => setPurgeConfirmation(event.target.value)}
                placeholder={purgePhrase || "DELETE inbox@example.com"}
                disabled={controlsDisabled || !selectedAccount}
              />
            </label>
            <button
              type="button"
              className="btn btn-danger"
              disabled={controlsDisabled || !purgeConfirmationMatches}
              onClick={() => void purgeInboxAndApplications()}
            >
              {purgePending ? <><span className="spinner" /> Purging...</> : "Purge Inbox"}
            </button>
          </div>
          <div className="rounded-lg border border-[var(--red)]/25 bg-[var(--red-soft)]/40 p-4">
            <h3 className="text-base font-semibold">What this permanently removes</h3>
            <ul className="mt-3 space-y-2 text-sm text-[var(--text-secondary)]">
              <li>The selected inbox account and imported emails.</li>
              <li>Processing jobs, reminders, and events for applications only linked to this inbox.</li>
              <li>Applications created from this inbox when no other inbox has evidence for them.</li>
            </ul>
            <p className="mt-3 text-sm text-[var(--text-secondary)]">
              Cross-inbox applications stay in place, and their events are unlinked from deleted source emails.
            </p>
          </div>
        </div>
      </div>
      {notice ? (
        <p className="mt-4 rounded-lg bg-[var(--green-soft)] px-3 py-2 text-sm text-[var(--green)]">
          {notice}
        </p>
      ) : null}
      {error ? (
        <p className="mt-4 rounded-lg bg-[var(--red-soft)] px-3 py-2 text-sm text-[var(--red)]">
          {error}
        </p>
      ) : null}
    </section>
  );
}

async function readErrorMessage(response: Response) {
  try {
    const body = (await response.json()) as { error?: string };
    return body.error ?? `Request failed with status ${response.status}.`;
  } catch {
    return `Request failed with status ${response.status}.`;
  }
}
