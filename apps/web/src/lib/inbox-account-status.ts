import type { InboxAccountSetup } from "./inbox-accounts";

export const gmailBackfillOptions = [
  { value: "1", label: "Last 1 month" },
  { value: "3", label: "Last 3 months (90 days)" },
  { value: "6", label: "Last 6 months" },
  { value: "12", label: "Last 12 months" },
  { value: "24", label: "Last 24 months" },
  { value: "all", label: "All history" },
] as const;
export const defaultGmailBackfillMonths = "3";

export function formatBackfillWindow(backfillMonths: number | null, backfillAllHistory: boolean) {
  if (backfillAllHistory) return "all available history";
  const months = backfillMonths ?? Number(defaultGmailBackfillMonths);
  return `the last ${months} month${months === 1 ? "" : "s"}`;
}

export function getBackfillOptionValue(account: InboxAccountSetup) {
  return account.backfillAllHistory
    ? "all"
    : String(account.backfillMonths ?? Number(defaultGmailBackfillMonths));
}

export function buildBackfillSelections(accounts: InboxAccountSetup[]) {
  return Object.fromEntries(
    accounts.map((account) => [account.id, getBackfillOptionValue(account)]),
  );
}

export function isConnectedAccount(account: InboxAccountSetup) {
  return account.connectionStatus === "Connected";
}

export function isInvalidInboxMetadata(account: InboxAccountSetup) {
  return (
    account.isValid === false ||
    account.connectionStatus.toLowerCase() === "metadata invalid"
  );
}

// The person cancelled setup or deleted the imported data; neither needs another prompt.
export function isCancelledOrDisconnectedSetup(account: InboxAccountSetup) {
  return (
    account.connectionStatus.toLowerCase().includes("imported data deleted") ||
    account.nextStep.toLowerCase().includes("access was disconnected")
  );
}

export function needsGmailSetup(account: InboxAccountSetup) {
  return (
    account.provider === "gmail" &&
    !isConnectedAccount(account) &&
    !isCancelledOrDisconnectedSetup(account)
  );
}

export function isReconnectableGmailAccount(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();

  return (
    account.provider === "gmail" &&
    !isConnectedAccount(account) &&
    (isInvalidInboxMetadata(account) ||
      status.includes("reconnect") ||
      status.includes("not completed") ||
      status.includes("missing") ||
      status.includes("oauth"))
  );
}

export function getStatusMeta(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();

  if (status === "connected") {
    return { label: "Connected", className: "bg-[var(--green-soft)] text-[var(--green)]" };
  }

  if (isInvalidInboxMetadata(account) || status.includes("reconnect")) {
    return { label: "Reconnect", className: "bg-[var(--red-soft)] text-[var(--red)]" };
  }

  if (status.includes("not completed") || status.includes("missing")) {
    return { label: "Finish setup", className: "bg-[var(--red-soft)] text-[var(--red)]" };
  }

  return { label: "Finish setup", className: "bg-[var(--yellow-soft)] text-[var(--yellow)]" };
}

export function getAccountSummary(account: InboxAccountSetup) {
  if (isConnectedAccount(account)) {
    const window = formatBackfillWindow(account.backfillMonths, account.backfillAllHistory);
    return account.initialBackfillCompletedAtUtc
      ? `Synced ${window} and keeping Gmail up to date.`
      : `Connected and ready to sync ${window}.`;
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

export function getActionLabel(account: InboxAccountSetup) {
  return isInvalidInboxMetadata(account) ||
    account.connectionStatus.includes("Reconnect") ||
    account.connectionStatus === "OAuth not completed"
    ? "Reconnect with Google"
    : "Continue with Google";
}

export function canUseSignedInEmail(emailAddress: string) {
  const trimmed = emailAddress.trim();
  return Boolean(trimmed) && trimmed.includes("@");
}
