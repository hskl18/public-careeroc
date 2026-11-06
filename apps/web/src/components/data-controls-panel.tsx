"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { components } from "@/lib/api-schema";
import type { InboxAccountSetup } from "@/lib/inbox-accounts";
import { readErrorMessage } from "@/lib/client-fetch";
import { SvgAsset } from "@/components/svg-asset";

// Generated from the API's OpenAPI document, so CI's drift check covers them.
type DeleteImportedDataResponse = components["schemas"]["DeleteImportedInboxDataResponse"];
type DeleteInboxPurgeResponse = components["schemas"]["DeleteInboxPurgeResponse"];

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
  // Falls back to the first account when the chosen one disappears after a refresh.
  const selectedAccount = useMemo(
    () => accounts.find((account) => account.id === selectedAccountId) ?? accounts[0] ?? null,
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
        `Deleted ${result.deletedEmails ?? 0} imported email${result.deletedEmails === 1 ? "" : "s"} from ${result.emailAddress ?? "the inbox"}. Applications stay.`,
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
        `Deleted ${result.emailAddress ?? "the inbox"}, ${result.deletedEmails ?? 0} email${result.deletedEmails === 1 ? "" : "s"}, and ${result.deletedApplications ?? 0} application${result.deletedApplications === 1 ? "" : "s"}.`,
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
    // The panel fills the page; both forms sit in its middle under a mascot instead of leaving a band.
    <section className="card flex h-full flex-col justify-center space-y-5 p-4">
      <div className="mx-auto grid w-full max-w-5xl gap-6 lg:grid-cols-2">
        <div className="space-y-3">
          <SvgAsset src="/profile-icons/profile-inbox-alert.svg" decorative className="h-14 w-14" />
          <h2 className="text-base font-semibold text-[var(--text-primary)]">Delete imported data</h2>
          <label className="block text-xs font-medium text-[var(--text-secondary)]">
            Inbox
            <select
              className="input mt-1"
              value={selectedAccount?.id ?? ""}
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
          <label className="block text-xs font-medium text-[var(--text-secondary)]">
            Type the inbox address to confirm
            <input
              className="input mt-1"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              placeholder={selectedAccount?.emailAddress ?? "inbox@example.com"}
              disabled={controlsDisabled || !selectedAccount}
            />
          </label>
          <p className="text-sm text-[var(--text-secondary)]">
            Removes this inbox&apos;s emails, analysis jobs, and Gmail tokens. Applications and
            reminders stay.
          </p>
          <button
            type="button"
            className="btn btn-danger btn-sm"
            disabled={controlsDisabled || !confirmationMatches}
            onClick={() => void deleteImportedData()}
          >
            {pending ? <><span className="spinner" /> Deleting...</> : "Delete imported data"}
          </button>
        </div>

        <div className="space-y-3 border-t border-[var(--border)] pt-4 lg:border-t-0 lg:border-l lg:pt-0 lg:pl-6">
          <SvgAsset src="/profile-icons/profile-inbox-night.svg" decorative className="h-14 w-14" />
          <h2 className="text-base font-semibold text-[var(--text-primary)]">Delete inbox</h2>
          <label className="block text-xs font-medium text-[var(--text-secondary)]">
            Type {purgePhrase || "DELETE inbox@example.com"} to confirm
            <input
              className="input mt-1"
              value={purgeConfirmation}
              onChange={(event) => setPurgeConfirmation(event.target.value)}
              placeholder={purgePhrase || "DELETE inbox@example.com"}
              disabled={controlsDisabled || !selectedAccount}
            />
          </label>
          <p className="text-sm text-[var(--text-secondary)]">
            Also removes applications found only in this inbox. Applications with mail in another
            inbox stay.
          </p>
          <button
            type="button"
            className="btn btn-danger btn-sm"
            disabled={controlsDisabled || !purgeConfirmationMatches}
            onClick={() => void purgeInboxAndApplications()}
          >
            {purgePending ? <><span className="spinner" /> Deleting...</> : "Delete inbox"}
          </button>
        </div>
      </div>
      {notice ? <p className="alert alert-success mt-4">{notice}</p> : null}
      {/* Mounted from the start, so screen readers announce each new notice. */}
      <p role="status" className="sr-only">{notice ?? ""}</p>
      {error ? (
        <p role="alert" className="alert alert-error mt-4">
          {error}
        </p>
      ) : null}
    </section>
  );
}
