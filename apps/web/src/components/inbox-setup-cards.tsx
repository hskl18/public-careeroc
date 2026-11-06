"use client";

import { formatTimestampInTimeZone } from "@/lib/date-time";
import {
  getAccountSummary,
  getActionLabel,
  getStatusMeta,
  gmailBackfillOptions,
} from "@/lib/inbox-account-status";
import type { InboxAccountSetup } from "@/lib/inbox-accounts";

function GmailTrustList() {
  const items = [
    "Import recent recruiting emails",
    "Detect applications, recruiter replies, interviews, and decisions",
    "Keep new recruiting messages synced",
    "Let you disconnect Gmail and delete imported data anytime",
    "Never send, delete, or modify emails",
  ];

  return (
    <div className="grid gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4">
      <p className="eyebrow">What happens when you connect Gmail</p>
      <div className="grid gap-2 sm:grid-cols-2">
        {items.map((item) => (
          <div key={item} className="flex min-w-0 items-start gap-2 text-xs leading-5 text-[var(--text-secondary)]">
            <span className="mt-0.5 text-[var(--green)]">OK</span>
            <span>{item}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function ContinueWithGoogleButton({
  onClick,
  disabled,
  redirecting,
  label = "Continue with Google",
  className = "btn btn-primary btn-block",
}: {
  onClick: () => void;
  disabled: boolean;
  redirecting: boolean;
  label?: string;
  className?: string;
}) {
  return (
    <button type="button" className={className} onClick={onClick} disabled={disabled}>
      {redirecting ? <><span className="spinner" /> Redirecting...</> : label}
    </button>
  );
}

export function SignedInGmailPrompt({
  emailAddress,
  onUseSignedIn,
  disabled,
  redirecting,
}: {
  emailAddress: string;
  onUseSignedIn: () => void;
  disabled: boolean;
  redirecting: boolean;
}) {
  return (
    <div className="card-elevated relative space-y-4 overflow-hidden p-5">
      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--accent)]" />
      <div className="flex flex-col gap-3 pt-1 lg:flex-row lg:items-start lg:justify-between">
        <div className="flex min-w-0 gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-md border border-[var(--border)] bg-[var(--surface-strong)] text-xs font-semibold text-[var(--accent)]">
            G
          </div>
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-[var(--text-primary)]">
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

      <ContinueWithGoogleButton onClick={onUseSignedIn} disabled={disabled} redirecting={redirecting} />

      <GmailTrustList />
    </div>
  );
}

export function GmailConnectCard({
  onSubmit,
  disabled,
  redirecting,
  title,
  description,
}: {
  onSubmit: () => void;
  disabled: boolean;
  redirecting: boolean;
  title: string;
  description: string;
}) {
  return (
    <div className="card-elevated p-5 space-y-4 relative overflow-hidden">
      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--accent)]" />
      <div className="flex items-center gap-2.5 pt-1">
        <div className="flex h-8 w-8 items-center justify-center rounded-md border border-[var(--border)] bg-[var(--surface-strong)] text-xs font-semibold text-[var(--accent)]">
          G
        </div>
        <div>
          <p className="text-sm font-semibold text-[var(--text-primary)]">{title}</p>
          <p className="text-xs text-[var(--text-tertiary)]">{description}</p>
        </div>
      </div>

      <ContinueWithGoogleButton onClick={onSubmit} disabled={disabled} redirecting={redirecting} />
    </div>
  );
}

function GmailAccountHeading({ account, live }: { account: InboxAccountSetup; live: boolean }) {
  const statusMeta = getStatusMeta(account);

  return (
    <div className="mb-3 flex items-center gap-2.5">
      <div className="flex h-8 w-8 items-center justify-center rounded-md bg-[var(--red)] text-xs font-semibold text-[var(--on-accent)]">
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
            {live ? <span className="sync-dot sync-dot-live mr-1.5 bg-[var(--green)]" /> : null}
            {statusMeta.label}
          </span>
        </div>
      </div>
    </div>
  );
}

export function ConnectedInboxCard({
  account,
  timeZoneId,
  selectedBackfill,
  backfillChanged,
  disabled,
  backfillPending,
  disconnectPending,
  onSelectBackfill,
  onSaveBackfill,
  onDisconnect,
}: {
  account: InboxAccountSetup;
  timeZoneId: string;
  selectedBackfill: string;
  backfillChanged: boolean;
  disabled: boolean;
  backfillPending: boolean;
  disconnectPending: boolean;
  onSelectBackfill: (value: string) => void;
  onSaveBackfill: () => void;
  onDisconnect: () => void;
}) {
  return (
    <article className="card-elevated relative overflow-hidden p-5">
      <div className="absolute top-0 left-0 h-[2px] w-full bg-[var(--green)]" />
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div className="min-w-0 flex-1">
          <GmailAccountHeading account={account} live />
          <p className="text-sm text-[var(--text-secondary)]">{getAccountSummary(account)}</p>
        </div>
        <button
          type="button"
          className="btn btn-danger btn-sm w-full sm:w-auto xl:shrink-0"
          onClick={onDisconnect}
          disabled={disabled}
        >
          {disconnectPending ? <><span className="spinner" /> Disconnecting...</> : "Disconnect"}
        </button>
      </div>

      <div className="mt-4 grid gap-3 border-t border-[var(--border)] pt-4 md:grid-cols-[minmax(0,0.82fr)_minmax(0,1.18fr)]">
        <div className="info-tile border-l-2 border-l-[var(--blue)]">
          <p className="text-xs text-[var(--text-tertiary)]">Last sync</p>
          <p className="mt-1 text-sm font-semibold text-[var(--text-primary)]">
            {formatTimestampInTimeZone(account.lastSyncedAtUtc, timeZoneId, "Not synced yet")}
          </p>
        </div>

        <div className="info-tile border-l-2 border-l-[var(--accent)]">
          <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
            <label className="block min-w-0">
              <span className="text-xs text-[var(--text-tertiary)]">Sync window</span>
              <select
                value={selectedBackfill}
                onChange={(event) => onSelectBackfill(event.target.value)}
                className="input mt-2 text-sm"
                disabled={disabled}
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
              onClick={onSaveBackfill}
              disabled={disabled || !backfillChanged}
            >
              {backfillPending ? <><span className="spinner" /> Updating...</> : "Save window"}
            </button>
          </div>
        </div>
      </div>
    </article>
  );
}

export function InboxSetupNeededCard({
  account,
  disabled,
  redirecting,
  cancelPending,
  onContinue,
  onCancel,
}: {
  account: InboxAccountSetup;
  disabled: boolean;
  redirecting: boolean;
  cancelPending: boolean;
  onContinue: () => void;
  onCancel: () => void;
}) {
  return (
    <article className="card-elevated relative overflow-hidden border-l-2 border-l-[var(--yellow)] p-5">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start xl:justify-between">
        <div className="min-w-0 flex-1">
          <GmailAccountHeading account={account} live={false} />
          <p className="text-sm text-[var(--text-secondary)]">{getAccountSummary(account)}</p>
          <p className="mt-1 text-xs text-[var(--text-tertiary)]">{account.nextStep}</p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2 xl:justify-end">
          <ContinueWithGoogleButton
            onClick={onContinue}
            disabled={disabled}
            redirecting={redirecting}
            label={getActionLabel(account)}
            className="btn btn-primary"
          />
          <button type="button" className="btn btn-secondary btn-sm" onClick={onCancel} disabled={disabled}>
            {cancelPending ? <><span className="spinner" /> Cancelling...</> : "Cancel setup"}
          </button>
        </div>
      </div>
    </article>
  );
}
