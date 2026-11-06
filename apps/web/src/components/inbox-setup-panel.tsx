"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  ConnectedInboxCard,
  GmailConnectCard,
  InboxSetupNeededCard,
  SignedInGmailPrompt,
} from "@/components/inbox-setup-cards";
import {
  buildBackfillSelections,
  canUseSignedInEmail,
  defaultGmailBackfillMonths,
  formatBackfillWindow,
  getBackfillOptionValue,
  isConnectedAccount,
  needsGmailSetup,
} from "@/lib/inbox-account-status";
import type {
  InboxAccountSetup,
  InboxSetupResponse,
} from "@/lib/inbox-accounts";
import { readErrorMessage } from "@/lib/client-fetch";

type InboxSetupPanelProps = {
  initialSetup: InboxSetupResponse;
  canPersist: boolean;
  proxyPrefix: string | null;
  currentUserEmail: string;
  timeZoneId: string;
  onAccountsChanged?: () => Promise<unknown>;
};

export function InboxSetupPanel({
  initialSetup,
  canPersist,
  proxyPrefix,
  currentUserEmail,
  timeZoneId,
  onAccountsChanged,
}: InboxSetupPanelProps) {
  const router = useRouter();
  const [setup, setSetup] = useState(initialSetup);
  const signedInEmail = currentUserEmail.trim();
  const gmailLoginHint = canUseSignedInEmail(signedInEmail)
    ? signedInEmail
    : currentUserEmail;
  const [backfillSelections, setBackfillSelections] = useState<Record<string, string>>(
    () => buildBackfillSelections(initialSetup.accounts),
  );
  const [pendingAction, setPendingAction] = useState<
    "gmail" | `disconnect:${string}` | `backfill:${string}` | null
  >(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    queueMicrotask(() => {
      setSetup(initialSetup);
      setBackfillSelections(buildBackfillSelections(initialSetup.accounts));
    });
  }, [initialSetup]);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const gmail = params.get("gmail");
    const gmailStatus = params.get("gmail_status");
    const gmailMessage = params.get("gmail_message");

    if (gmail === "upstream_unavailable") {
      params.delete("gmail");

      const nextSearch = params.toString();
      window.history.replaceState(
        {},
        "",
        `${window.location.pathname}${nextSearch ? `?${nextSearch}` : ""}${window.location.hash}`,
      );

      queueMicrotask(() => {
        setNotice(null);
        setError(
          "The API is unavailable. Try again.",
        );
      });
      return;
    }

    if (!gmailStatus || !gmailMessage) {
      return;
    }

    const nextNotice = gmailStatus === "connected" ? gmailMessage : null;
    const nextError = gmailStatus === "connected" ? null : gmailMessage;

    params.delete("gmail_status");
    params.delete("gmail_message");

    const nextSearch = params.toString();
    window.history.replaceState(
      {},
      "",
      `${window.location.pathname}${nextSearch ? `?${nextSearch}` : ""}${window.location.hash}`,
    );

    queueMicrotask(() => {
      setNotice(nextNotice);
      setError(nextError);
    });
  }, []);

  function startGmailOAuth(emailAddress: string) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const trimmed = emailAddress.trim();
    if (!trimmed) {
      setError("Enter a Gmail address first.");
      return;
    }

    setNotice(null);
    setError(null);
    setPendingAction("gmail");

    const searchParams = new URLSearchParams({
      emailAddress: trimmed,
      returnUrl: `${window.location.pathname}${window.location.search}${window.location.hash}`,
    });

    searchParams.set(
      "backfillMonths",
      defaultGmailBackfillMonths,
    );

    // A full navigation is required: this API route redirects the browser to Google's consent screen.
    // eslint-disable-next-line @next/next/no-location-assign-relative-destination
    window.location.assign(
      `${proxyPrefix}/api/inbox-accounts/gmail/oauth/start?${searchParams.toString()}`,
    );
  }

  async function disconnectAccount(account: InboxAccountSetup) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const connected = isConnectedAccount(account);
    const confirmMessage = connected
      ? `Disconnect Gmail inbox ${account.emailAddress}?`
      : `Cancel Gmail setup for ${account.emailAddress}?`;

    if (!window.confirm(confirmMessage)) {
      return;
    }

    const requestKey = `disconnect:${account.id}` as const;
    setPendingAction(requestKey);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/inbox-accounts/${account.id}`, {
        method: "DELETE",
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const nextSetup = (await response.json()) as InboxSetupResponse;
      const visibleSetup = connected
        ? nextSetup
        : {
            ...nextSetup,
            accounts: nextSetup.accounts.filter((candidate) => candidate.id !== account.id),
          };
      setSetup(visibleSetup);
      setBackfillSelections(buildBackfillSelections(visibleSetup.accounts));
      setNotice(
        connected
          ? `Disconnected Gmail inbox ${account.emailAddress}. Your application history is preserved.`
          : `Cancelled Gmail setup for ${account.emailAddress}.`,
      );
      router.refresh();
      await onAccountsChanged?.();
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to update Gmail setup.",
      );
    } finally {
      setPendingAction(null);
    }
  }

  async function updateBackfill(account: InboxAccountSetup) {
    if (!proxyPrefix) {
      setError("API proxy is not configured.");
      return;
    }

    const selectedBackfill = backfillSelections[account.id] ?? getBackfillOptionValue(account);
    const requestKey = `backfill:${account.id}` as const;

    setPendingAction(requestKey);
    setNotice(null);
    setError(null);

    try {
      const response = await fetch(`${proxyPrefix}/api/inbox-accounts/${account.id}/backfill`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          backfillMonths: selectedBackfill === "all" ? 0 : Number(selectedBackfill),
        }),
      });

      if (!response.ok) {
        throw new Error(await readErrorMessage(response));
      }

      const nextSetup = (await response.json()) as InboxSetupResponse;
      setSetup(nextSetup);
      setBackfillSelections(buildBackfillSelections(nextSetup.accounts));
      setNotice(
        `Sync window updated for ${account.emailAddress}. It will run on the next sync.`,
      );
      router.refresh();
      await onAccountsChanged?.();
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Failed to update the backfill window.",
      );
    } finally {
      setPendingAction(null);
    }
  }

  const gmailAccounts = setup.accounts
    .filter((account) => account.provider === "gmail" && (isConnectedAccount(account) || needsGmailSetup(account)))
    .sort(
      (left, right) =>
        new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime(),
    );

  const connectedAccounts = gmailAccounts.filter(isConnectedAccount);
  const attentionAccounts = gmailAccounts.filter(needsGmailSetup);
  const actionsDisabled = !canPersist || pendingAction !== null;
  const hasAccounts = gmailAccounts.length > 0;

  return (
    <div id="connect-inbox" className="card scroll-mt-20 p-4">
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
        <h2 className="text-base font-semibold text-[var(--text-primary)]">
          {hasAccounts ? "Gmail" : "Connect Gmail"}
        </h2>
        {hasAccounts ? null : (
          <p className="mt-1 text-sm text-[var(--text-secondary)]">
            Reads recruiting mail only.
          </p>
        )}
        </div>
      </div>

      {!canPersist && (
        <div role="status" className="alert alert-warning mb-5">
          Offline.
        </div>
      )}
      {notice ? <div className="alert alert-success mb-5">{notice}</div> : null}
      <p role="status" className="sr-only">{notice ?? ""}</p>
      {error && <div role="alert" className="alert alert-error mb-5">{error}</div>}

      {hasAccounts && (
        <div className="mb-5 grid gap-2 sm:grid-cols-3">
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-4 py-3">
            <p className="eyebrow">Status</p>
            <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
              {connectedAccounts.length} connected
            </p>
          </div>
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-4 py-3">
            <p className="eyebrow">Import window</p>
            <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
              {connectedAccounts[0]
                ? formatBackfillWindow(
                    connectedAccounts[0].backfillMonths,
                    connectedAccounts[0].backfillAllHistory,
                  )
                : "Not selected"}
            </p>
          </div>
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-4 py-3">
            <p className="eyebrow">Setup issues</p>
            <p
              className="mt-2 text-sm font-semibold"
              style={{ color: attentionAccounts.length > 0 ? "var(--yellow)" : "var(--green)" }}
            >
              {attentionAccounts.length}
            </p>
          </div>
        </div>
      )}

      {!hasAccounts ? (
        <SignedInGmailPrompt
          emailAddress={signedInEmail}
          onUseSignedIn={() => startGmailOAuth(gmailLoginHint)}
          disabled={actionsDisabled}
          redirecting={pendingAction === "gmail"}
        />
      ) : null}

      {gmailAccounts.length === 0 ? null : (
        <div className="space-y-4">
          {connectedAccounts.length > 0 && (
            <section>
              <p className="eyebrow mb-3">Connected Gmail inboxes</p>
              <div className="space-y-2">
                {connectedAccounts.map((account) => {
                  const selectedBackfill =
                    backfillSelections[account.id] ?? getBackfillOptionValue(account);

                  return (
                    <ConnectedInboxCard
                      key={account.id}
                      account={account}
                      timeZoneId={timeZoneId}
                      selectedBackfill={selectedBackfill}
                      backfillChanged={selectedBackfill !== getBackfillOptionValue(account)}
                      disabled={actionsDisabled}
                      backfillPending={pendingAction === `backfill:${account.id}`}
                      disconnectPending={pendingAction === `disconnect:${account.id}`}
                      onSelectBackfill={(value) =>
                        setBackfillSelections((current) => ({ ...current, [account.id]: value }))
                      }
                      onSaveBackfill={() => void updateBackfill(account)}
                      onDisconnect={() => void disconnectAccount(account)}
                    />
                  );
                })}
              </div>
            </section>
          )}

          {attentionAccounts.length > 0 && (
            <section className="mt-2">
              <div className="mb-3 flex items-center gap-2">
                <p className="eyebrow">Finish setup</p>
                <span className="badge bg-[var(--yellow-soft)] text-[var(--yellow)]">
                  {attentionAccounts.length}
                </span>
              </div>
              <div className="space-y-3">
                {attentionAccounts.map((account) => (
                  <InboxSetupNeededCard
                    key={account.id}
                    account={account}
                    disabled={actionsDisabled}
                    redirecting={pendingAction === "gmail"}
                    cancelPending={pendingAction === `disconnect:${account.id}`}
                    onContinue={() => startGmailOAuth(account.emailAddress)}
                    onCancel={() => void disconnectAccount(account)}
                  />
                ))}
              </div>
            </section>
          )}

          <details className="card-elevated overflow-hidden group">
            <summary className="px-5 py-4">
              <div className="flex-1">
                <p className="eyebrow">Use a different Gmail account</p>
                <p className="mt-1 text-sm font-medium text-[var(--text-primary)]">
                  Switch the mailbox only if your applications are in another inbox.
                </p>
              </div>
            </summary>
            <div className="border-t border-[var(--border)] p-5">
              <GmailConnectCard
                onSubmit={() => startGmailOAuth(gmailLoginHint)}
                disabled={actionsDisabled}
                redirecting={pendingAction === "gmail"}
                title="Connect another Gmail"
                description="Open Google to connect or switch the mailbox careeroc should track."
              />
            </div>
          </details>
        </div>
      )}
    </div>
  );
}
