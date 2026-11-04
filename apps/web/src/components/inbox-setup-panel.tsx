"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import type {
  InboxAccountSetup,
  InboxSetupResponse,
} from "@/lib/inbox-accounts";

type InboxSetupPanelProps = {
  initialSetup: InboxSetupResponse;
  canPersist: boolean;
  proxyPrefix: string | null;
  currentUserEmail: string;
  currentUserName?: string | null;
  timeZoneId: string;
  onAccountsChanged?: () => Promise<unknown>;
};

async function readErrorMessage(response: Response) {
  try {
    const payload = (await response.json()) as { error?: string };
    if (payload.error) return payload.error;
  } catch {}

  return `Request failed with ${response.status}`;
}

const gmailBackfillOptions = [
  { value: "1", label: "Last 1 month" },
  { value: "3", label: "Last 3 months (90 days)" },
  { value: "6", label: "Last 6 months" },
  { value: "12", label: "Last 12 months" },
  { value: "24", label: "Last 24 months" },
  { value: "all", label: "All history" },
] as const;
const defaultGmailBackfillMonths = "3";

function formatBackfillWindow(backfillMonths: number | null, backfillAllHistory: boolean) {
  if (backfillAllHistory) return "all available history";
  const months = backfillMonths ?? Number(defaultGmailBackfillMonths);
  return `the last ${months} month${months === 1 ? "" : "s"}`;
}

function getBackfillOptionValue(account: InboxAccountSetup) {
  return account.backfillAllHistory
    ? "all"
    : String(account.backfillMonths ?? Number(defaultGmailBackfillMonths));
}

function buildBackfillSelections(accounts: InboxAccountSetup[]) {
  return Object.fromEntries(
    accounts.map((account) => [account.id, getBackfillOptionValue(account)]),
  );
}

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "Not synced yet");
}

function isConnectedAccount(account: InboxAccountSetup) {
  return account.connectionStatus === "Connected";
}

function isInvalidInboxMetadata(account: InboxAccountSetup) {
  return (
    account.isValid === false ||
    account.connectionStatus.toLowerCase() === "metadata invalid"
  );
}

function isCancelledOrDisconnectedSetup(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();
  const nextStep = account.nextStep.toLowerCase();

  return (
    status.includes("imported data deleted") ||
    nextStep.includes("access was disconnected")
  );
}

function getStatusMeta(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();

  if (status === "connected") {
    return {
      label: "Connected",
      className: "bg-[var(--green-soft)] text-[var(--green)]",
    };
  }

  if (
    isInvalidInboxMetadata(account) ||
    status.includes("reconnect") ||
    status.includes("not completed") ||
    status.includes("missing")
  ) {
    return {
      label:
        isInvalidInboxMetadata(account) || status.includes("reconnect")
          ? "Reconnect"
          : "Finish setup",
      className: "bg-[var(--red-soft)] text-[var(--red)]",
    };
  }

  return {
    label: "Finish setup",
    className: "bg-[var(--yellow-soft)] text-[var(--yellow)]",
  };
}

function getAccountSummary(account: InboxAccountSetup) {
  if (isConnectedAccount(account)) {
    return account.initialBackfillCompletedAtUtc
      ? `Synced ${formatBackfillWindow(account.backfillMonths, account.backfillAllHistory)} and keeping Gmail up to date.`
      : `Connected and ready to sync ${formatBackfillWindow(account.backfillMonths, account.backfillAllHistory)}.`;
  }

  if (account.connectionStatus === "Waiting for Google") {
    return "Your Gmail setup is almost complete. Continue to Google to approve access.";
  }

  if (isInvalidInboxMetadata(account)) {
    return "Reconnect Google to repair stored Gmail access metadata and resume syncing.";
  }

  if (account.connectionStatus.includes("Reconnect")) {
    return "Reconnect Google to restore Gmail syncing and resume application tracking.";
  }

  return "Continue to Google so careeroc can import recruiting emails from this inbox.";
}

function getActionLabel(account: InboxAccountSetup) {
  if (
    isInvalidInboxMetadata(account) ||
    account.connectionStatus.includes("Reconnect") ||
    account.connectionStatus === "OAuth not completed"
  ) {
    return "Reconnect with Google";
  }

  if (account.connectionStatus === "Waiting for Google") {
    return "Continue with Google";
  }

  return "Continue with Google";
}

function canUseSignedInEmail(emailAddress: string) {
  const trimmed = emailAddress.trim().toLowerCase();

  return Boolean(trimmed) && trimmed.includes("@") && trimmed !== "local@careeroc.dev";
}

function GmailTrustList() {
  const items = [
    "Import recent recruiting emails",
    "Detect applications, recruiter replies, interviews, and decisions",
    "Keep new recruiting messages synced",
    "Let you disconnect Gmail and delete imported data anytime",
    "Never send, delete, or modify emails",
  ];

  return (
    <div className="grid gap-2 rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] p-4">
      <p className="eyebrow">What happens when you connect Gmail</p>
      <div className="grid gap-2 sm:grid-cols-2">
        {items.map((item) => (
          <div key={item} className="flex min-w-0 items-start gap-2 text-xs leading-5 text-[var(--text-secondary)]">
            <span className="mt-0.5 font-mono text-[var(--green)]">OK</span>
            <span>{item}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function SignedInGmailPrompt({
  emailAddress,
  onUseSignedIn,
  canPersist,
  pendingAction,
}: {
  emailAddress: string;
  onUseSignedIn: () => void;
  canPersist: boolean;
  pendingAction: string | null;
}) {
  return (
    <div className="card-elevated relative space-y-4 overflow-hidden p-5">
      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--accent)]" />
      <div className="flex flex-col gap-3 pt-1 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex min-w-0 gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-[var(--border)] bg-white text-xs font-bold text-[var(--accent)]">
            G
          </div>
          <div className="min-w-0">
            <p className="eyebrow">Current Google account</p>
            <h3 className="mt-1 text-lg font-semibold text-[var(--text-primary)]">
              Connect this Gmail inbox
            </h3>
            <p className="mt-1 break-all text-sm font-medium text-[var(--text-primary)]">
              {emailAddress}
            </p>
            <p className="mt-1 text-xs leading-relaxed text-[var(--text-tertiary)]">
              Make sure this is the inbox you use for job applications.
            </p>
          </div>
        </div>
        <span className="badge self-start bg-[var(--green-soft)] text-[var(--green)]">
          90 days
        </span>
      </div>

      <button
        type="button"
        className="btn btn-primary btn-block"
        onClick={onUseSignedIn}
        disabled={!canPersist || pendingAction !== null}
      >
        {pendingAction === "gmail"
          ? <><span className="spinner" /> Redirecting...</>
          : "Continue with Google"}
      </button>

      <GmailTrustList />
    </div>
  );
}

function GmailConnectCard({
  onSubmit,
  canPersist,
  pendingAction,
  title,
  description,
}: {
  onSubmit: () => void;
  canPersist: boolean;
  pendingAction: string | null;
  title: string;
  description: string;
}) {
  return (
    <div className="card-elevated p-5 space-y-4 relative overflow-hidden">
      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--accent)]" />
      <div className="flex items-center gap-2.5 pt-1">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg border border-[var(--border)] bg-white text-xs font-bold text-[var(--accent)]">
          G
        </div>
        <div>
          <p className="text-sm font-semibold text-[var(--text-primary)]">{title}</p>
          <p className="text-xs text-[var(--text-tertiary)]">{description}</p>
        </div>
      </div>

      <button
        type="button"
        className="btn btn-primary btn-block"
        onClick={onSubmit}
        disabled={!canPersist || pendingAction !== null}
      >
        {pendingAction === "gmail"
          ? <><span className="spinner" /> Redirecting...</>
          : "Continue with Google"}
      </button>
    </div>
  );
}

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
          "Gmail connection could not finish because the careeroc API is unavailable. Start the API and retry from this page.",
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

  const gmailAccounts = [...setup.accounts]
    .filter(
      (account) =>
        account.provider === "gmail" &&
        (isConnectedAccount(account) || !isCancelledOrDisconnectedSetup(account)),
    )
    .sort(
      (left, right) =>
        new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime(),
    );

  const connectedAccounts = gmailAccounts.filter(isConnectedAccount);
  const attentionAccounts = gmailAccounts.filter((account) => !isConnectedAccount(account));
  const hasAccounts = gmailAccounts.length > 0;

  return (
    <div id="connect-inbox" className="card app-workspace-panel scroll-mt-20 p-5">
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
        <p className="eyebrow">Inbox setup</p>
        <h2 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
          {hasAccounts ? "Gmail connection" : "Connect Gmail to start tracking applications"}
        </h2>
        <p className="mt-2 text-sm text-[var(--text-secondary)]">
          {hasAccounts
            ? "Manage the Gmail inbox used for recruiting mail and choose how far back the importer should scan."
            : "careeroc finds recruiting emails, application confirmations, interview invites, and recruiter updates from your Gmail inbox."}
        </p>
        </div>
        {hasAccounts ? (
          <span className="badge w-fit bg-[var(--green-soft)] text-[var(--green)]">
            {connectedAccounts.length} connected
          </span>
        ) : null}
      </div>

      {!canPersist && (
        <div className="alert alert-warning mb-5">
          Live Gmail setup is unavailable right now. Connection changes are
          disabled until the API comes back.
        </div>
      )}
      {notice && <div className="alert alert-success mb-5">{notice}</div>}
      {error && <div className="alert alert-error mb-5">{error}</div>}

      {hasAccounts && (
        <div className="mb-5 grid gap-2 sm:grid-cols-3">
          <div className="rounded-md border border-[var(--border)] bg-white/45 px-4 py-3">
            <p className="eyebrow">Status</p>
            <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
              {connectedAccounts.length} connected
            </p>
          </div>
          <div className="rounded-md border border-[var(--border)] bg-white/45 px-4 py-3">
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
          <div className="rounded-md border border-[var(--border)] bg-white/45 px-4 py-3">
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
          canPersist={canPersist}
          pendingAction={pendingAction}
        />
      ) : null}

      {gmailAccounts.length === 0 ? (
        <div
          className="card-elevated p-10 text-center"
          style={{ borderStyle: "dashed", borderColor: "var(--border-hover)" }}
        >
          <div className="mx-auto mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-lg font-bold text-[var(--accent)]">
            +
          </div>
          <p className="text-sm font-medium text-[var(--text-primary)]">
            No Gmail inbox connected yet
          </p>
          <p className="mx-auto mt-2 max-w-xs text-xs leading-relaxed text-[var(--text-tertiary)]">
            Connect Gmail above to import recruiting history, monitor new mail,
            and keep the pipeline current.
          </p>
        </div>
      ) : (
        <div className="space-y-4">
          {connectedAccounts.length > 0 && (
            <section>
              <p className="eyebrow mb-3">Connected Gmail inboxes</p>
              <div className="space-y-2">
                {connectedAccounts.map((account) => {
                  const statusMeta = getStatusMeta(account);
                  const selectedBackfill =
                    backfillSelections[account.id] ?? getBackfillOptionValue(account);
                  const backfillChanged = selectedBackfill !== getBackfillOptionValue(account);
                  const backfillPending = pendingAction === `backfill:${account.id}`;

                  return (
                    <article
                      key={account.id}
                      className="card-elevated relative overflow-hidden p-5"
                    >
                      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--green)]" />
                      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                        <div className="min-w-0 flex-1">
                          <div className="mb-3 flex items-center gap-2.5">
                            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--red)] text-xs font-bold text-white">
                              G
                            </div>
                            <div className="min-w-0 flex-1">
                              <h4 className="break-all text-base font-semibold leading-tight text-[var(--text-primary)]">
                                {account.emailAddress}
                              </h4>
                              <div className="mt-1 flex flex-wrap items-center gap-1.5">
                                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-secondary)]">
                                  Gmail
                                </span>
                                <span className={`badge ${statusMeta.className}`}>
                                  <span className="sync-dot sync-dot-live mr-1.5 bg-[var(--green)]" />
                                  {statusMeta.label}
                                </span>
                              </div>
                            </div>
                          </div>
                          <p className="text-sm text-[var(--text-secondary)]">
                            {getAccountSummary(account)}
                          </p>
                        </div>
                        <button
                          type="button"
                          className="btn btn-danger btn-sm w-full sm:w-auto xl:shrink-0"
                          onClick={() => void disconnectAccount(account)}
                          disabled={!canPersist || pendingAction !== null}
                        >
                          {pendingAction === `disconnect:${account.id}`
                            ? <><span className="spinner" /> Disconnecting...</>
                            : "Disconnect"}
                        </button>
                      </div>

                      <div className="mt-4 grid gap-3 border-t border-[var(--border)] pt-4 md:grid-cols-[minmax(0,0.82fr)_minmax(0,1.18fr)]">
                        <div className="info-tile border-l-2 border-l-[var(--blue)]">
                          <p className="text-[11px] font-mono text-[var(--text-tertiary)]">
                            Last sync
                          </p>
                          <p className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
                            {formatTimestamp(account.lastSyncedAtUtc, timeZoneId)}
                          </p>
                        </div>

                        <div className="info-tile border-l-2 border-l-[var(--accent)]">
                          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
                            <label className="block min-w-0">
                              <span className="text-[11px] font-mono text-[var(--text-tertiary)]">
                                Sync window
                              </span>
                              <select
                                value={selectedBackfill}
                                onChange={(event) =>
                                  setBackfillSelections((current) => ({
                                    ...current,
                                    [account.id]: event.target.value,
                                  }))
                                }
                                className="input mt-2 text-sm"
                                disabled={!canPersist || pendingAction !== null}
                              >
                                {gmailBackfillOptions.map((option) => (
                                  <option key={option.value} value={option.value}>
                                    {option.label}
                                  </option>
                                ))}
                              </select>
                            </label>
                            <button
                              type="button"
                              className="btn btn-secondary btn-sm w-full sm:w-auto"
                              onClick={() => void updateBackfill(account)}
                              disabled={!canPersist || pendingAction !== null || !backfillChanged}
                            >
                              {backfillPending
                                ? <><span className="spinner" /> Updating...</>
                                : "Save window"}
                            </button>
                          </div>
                        </div>
                      </div>
                    </article>
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
                {attentionAccounts.map((account) => {
                  const statusMeta = getStatusMeta(account);
                  const actionLabel = getActionLabel(account);

                  return (
                    <article
                      key={account.id}
                      className="card-elevated relative overflow-hidden border-l-2 border-l-[var(--yellow)] p-5"
                    >
                      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
                        <div className="min-w-0 flex-1">
                          <div className="mb-3 flex items-center gap-2.5">
                            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--red)] text-xs font-bold text-white">
                              G
                            </div>
                            <div className="min-w-0 flex-1">
                              <h4 className="break-all text-base font-semibold leading-tight text-[var(--text-primary)]">
                                {account.emailAddress}
                              </h4>
                              <div className="mt-1 flex flex-wrap items-center gap-1.5">
                                <span className="badge bg-[var(--surface-overlay)] text-[var(--text-secondary)]">
                                  Gmail
                                </span>
                                <span className={`badge ${statusMeta.className}`}>
                                  {statusMeta.label}
                                </span>
                              </div>
                            </div>
                          </div>
                          <p className="text-sm text-[var(--text-secondary)]">
                            {getAccountSummary(account)}
                          </p>
                          <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                            {account.nextStep}
                          </p>
                        </div>
                        <div className="flex shrink-0 flex-wrap gap-2 xl:justify-end">
                          <button
                            type="button"
                            className="btn btn-primary"
                            onClick={() => startGmailOAuth(account.emailAddress)}
                            disabled={!canPersist || pendingAction !== null}
                          >
                            {pendingAction === "gmail"
                              ? <><span className="spinner" /> Redirecting...</>
                              : actionLabel}
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => void disconnectAccount(account)}
                          disabled={!canPersist || pendingAction !== null}
                        >
                          {pendingAction === `disconnect:${account.id}`
                            ? <><span className="spinner" /> Cancelling...</>
                            : "Cancel setup"}
                        </button>
                      </div>
                      </div>
                    </article>
                  );
                })}
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
                canPersist={canPersist}
                pendingAction={pendingAction}
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
