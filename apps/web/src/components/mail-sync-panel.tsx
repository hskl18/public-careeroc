"use client";

import { useEffect, useEffectEvent, useRef, useState } from "react";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import type { DashboardInboxCheckResponse, DashboardResponse } from "@/lib/dashboard";
import {
  isConnectedAccount,
  isInvalidInboxMetadata,
  isReconnectableGmailAccount,
  needsGmailSetup,
} from "@/lib/inbox-account-status";
import {
  buildMailSyncStatus,
  buildProgressEstimate,
  formatCount,
  getDashboardProgress,
  getPollingIntervalMs,
  shouldAdvanceInboxProcessing,
  updateProgressSamples,
  type ProgressSample,
} from "@/lib/mail-sync-progress";
import {
  activeMailSyncPollingIntervalMs,
  isInboxAnalysisActive,
} from "@/lib/workspace-polling";
import { SvgAsset, mascotAssets } from "./svg-asset";
import type {
  InboxAccountSetup,
  InboxSetupResponse,
} from "@/lib/inbox-accounts";

type MailSyncPanelProps = {
  dashboard: DashboardResponse;
  dashboardSource: "api" | "fallback";
  setup: InboxSetupResponse;
  inboxSetupSource: "api" | "fallback";
  timeZoneId: string;
};

type MailSyncState = {
  dashboard: DashboardResponse;
  connectedAccountCount: number;
};

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(value, timeZoneId, "Waiting for first sync");
}

function SyncMetric({
  label,
  value,
  hint,
  tone = "neutral",
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: "neutral" | "good" | "bad";
}) {
  const valueColor =
    tone === "bad"
      ? "var(--red)"
      : tone === "good"
        ? "var(--green)"
        : "var(--text-primary)";

  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-strong)] px-3 py-3">
      <p className="eyebrow">{label}</p>
      <p className="mt-2 text-sm font-semibold" style={{ color: valueColor }}>
        {value}
      </p>
      {hint ? <p className="mt-1 text-xs text-[var(--text-tertiary)]">{hint}</p> : null}
    </div>
  );
}

function FinishSetupList({
  accounts,
  className = "",
}: {
  accounts: InboxAccountSetup[];
  className?: string;
}) {
  return (
    <div className={`info-tile border-[var(--border)] bg-[var(--yellow-soft)] ${className}`}>
      <p className="eyebrow text-[var(--yellow)]">Finish setup</p>
      <div className="mt-3 space-y-3">
        {accounts.map((account) => (
          <div
            key={account.id}
            className="grid gap-1 border-t border-[var(--border)] pt-3 first:border-t-0 first:pt-0"
          >
            <p className="break-all text-sm font-medium text-[var(--text-primary)]">
              {account.emailAddress}
            </p>
            <p className="text-xs leading-5 text-[var(--text-secondary)]">
              {isInvalidInboxMetadata(account)
                ? "Reconnect Google to repair stored Gmail access metadata."
                : account.nextStep}
            </p>
          </div>
        ))}
      </div>
    </div>
  );
}

export function MailSyncPanel({
  dashboard: initialDashboard,
  dashboardSource,
  setup,
  inboxSetupSource,
  timeZoneId,
}: MailSyncPanelProps) {
  const [dashboard, setDashboard] = useState(initialDashboard);
  const [liveDashboardSource, setLiveDashboardSource] = useState(dashboardSource);
  const [progressSamples, setProgressSamples] = useState<ProgressSample[]>(() =>
    updateProgressSamples([], initialDashboard, Date.now()),
  );
  const initialActivePollStarted = useRef(false);
  const setupLive = inboxSetupSource === "api";
  const dashboardLive = liveDashboardSource === "api";
  const connectedAccounts = setup.accounts.filter(
    (account) => account.provider === "gmail" && isConnectedAccount(account),
  );
  const reconnectableAccounts = setup.accounts.filter(isReconnectableGmailAccount);
  const mailSyncStateRef = useRef<MailSyncState>({
    dashboard,
    connectedAccountCount: connectedAccounts.length,
  });
  const attentionAccounts = setup.accounts.filter(needsGmailSetup);
  const { processed, total, percent } = getDashboardProgress(dashboard);
  const remaining = Math.max(0, total - processed);
  const pendingAnalysis = formatCount(dashboard.inbox.pendingAnalysisEmails);
  const processingAnalysis = formatCount(dashboard.inbox.processingAnalysisEmails);
  const failedAnalysis = formatCount(dashboard.inbox.failedAnalysisEmails);
  const progressEstimate = buildProgressEstimate(
    progressSamples,
    processed,
    total,
    dashboard,
  );
  const isStalled = progressEstimate?.kind === "stalled";
  const status = buildMailSyncStatus({
    setupLive,
    dashboardLive,
    connectedCount: connectedAccounts.length,
    reconnectableCount: reconnectableAccounts.length,
    setupNeededCount: attentionAccounts.length,
    lastSyncAtUtc: dashboard.inbox.lastSyncAtUtc,
    analyzing: isInboxAnalysisActive(dashboard),
    estimate: progressEstimate,
  });

  useEffect(() => {
    queueMicrotask(() => {
      setDashboard(initialDashboard);
      setLiveDashboardSource(dashboardSource);
      setProgressSamples(updateProgressSamples([], initialDashboard, Date.now()));
    });
  }, [dashboardSource, initialDashboard]);

  useEffect(() => {
    mailSyncStateRef.current = {
      dashboard,
      connectedAccountCount: connectedAccounts.length,
    };
  }, [connectedAccounts.length, dashboard]);

  const refreshDashboard = useEffectEvent(async () => {
    if (document.visibilityState === "hidden") {
      return;
    }

    const current = mailSyncStateRef.current;
    const advanceProcessing = shouldAdvanceInboxProcessing(
      current.dashboard,
      current.connectedAccountCount,
    );
    const endpoint = advanceProcessing
      ? "/api/careeroc/api/dashboard/inbox-check"
      : "/api/careeroc/api/dashboard";

    const response = await fetchWithInFlightDedup(endpoint, {
      method: advanceProcessing ? "POST" : "GET",
      cache: "no-store",
    }, advanceProcessing ? { dedupeKey: `POST:${endpoint}` } : undefined);

    if (!response.ok) {
      throw new Error("Inbox mail sync request failed.");
    }

    const payload = advanceProcessing
      ? ((await response.json()) as DashboardInboxCheckResponse).dashboard
      : ((await response.json()) as DashboardResponse);

    setDashboard(payload);
    setProgressSamples((current) =>
      updateProgressSamples(current, payload, Date.now()),
    );
    setLiveDashboardSource("api");
  });

  useEffect(() => {
    if (!setupLive) {
      return;
    }

    let cancelled = false;
    let timeoutId: number | null = null;

    async function refresh() {
      try {
        await refreshDashboard();
      } catch {
        if (cancelled) return;
        setLiveDashboardSource("fallback");
      }
    }

    function scheduleNextPoll() {
      const current = mailSyncStateRef.current;
      const pollingIntervalMs = getPollingIntervalMs(
        current.dashboard,
        current.connectedAccountCount,
      );

      if (cancelled || pollingIntervalMs === null) {
        return;
      }

      timeoutId = window.setTimeout(() => {
        void refresh().finally(() => {
          if (!cancelled) {
            scheduleNextPoll();
          }
        });
      }, pollingIntervalMs);
    }

    const current = mailSyncStateRef.current;
    const pollingIntervalMs = getPollingIntervalMs(
      current.dashboard,
      current.connectedAccountCount,
    );
    const shouldRefreshImmediately =
      pollingIntervalMs === activeMailSyncPollingIntervalMs &&
      !initialActivePollStarted.current;

    if (shouldRefreshImmediately) {
      initialActivePollStarted.current = true;
      void refresh();
    }

    scheduleNextPoll();

    return () => {
      cancelled = true;
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId);
      }
    };
  }, [connectedAccounts.length, setupLive]);

  const shouldCollapseSyncDetails =
    connectedAccounts.length === 0 && reconnectableAccounts.length === 0;

  return (
    <section id="mail-sync" className="card scroll-mt-20 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <div className="min-w-0">
            <h2 className="text-base font-semibold text-[var(--text-primary)]">
              Gmail sync
            </h2>
            <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
              {shouldCollapseSyncDetails
                ? "Connect Gmail to start."
                : status.detail}
            </p>
          </div>
        </div>
        <span className={`inline-flex shrink-0 items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium ${status.tone}`}>
          <span className={`sync-dot ${status.pulse ? "sync-dot-live" : ""} ${status.dot}`} />
          {status.label}
        </span>
      </div>

      {shouldCollapseSyncDetails ? (
        <details className="mt-5 overflow-hidden rounded-md border border-[var(--border)] bg-[var(--surface-strong)]">
          <summary className="px-4 py-3">
            <div className="flex-1">
              <p className="eyebrow">Advanced sync details</p>
            </div>
          </summary>
          <div className="border-t border-[var(--border)] p-4">
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              <SyncMetric label="Last sync" value={formatTimestamp(dashboard.inbox.lastSyncAtUtc, timeZoneId)} />
              <SyncMetric
                label="Emails processed"
                value={total > 0 ? `${processed}/${total} emails analyzed` : "No synced mail yet"}
              />
              <SyncMetric
                label="Sync queue"
                value={`${pendingAnalysis + processingAnalysis} active jobs`}
                hint={`${pendingAnalysis} pending · ${processingAnalysis} processing`}
              />
              <SyncMetric
                label="Issues"
                value={String(failedAnalysis)}
                tone={failedAnalysis > 0 ? "bad" : "good"}
              />
            </div>

            {attentionAccounts.length > 0 ? (
              <FinishSetupList accounts={attentionAccounts} className="mt-3" />
            ) : null}
          </div>
        </details>
      ) : (
        <>
      <div className="mt-5 rounded-md border border-[var(--border)] bg-[var(--surface-strong)] p-4">
        <div className="grid gap-4 lg:grid-cols-[10rem_minmax(0,1fr)] lg:items-center">
          <div className="flex items-center gap-3 lg:block">
            <div className="grid h-24 w-24 shrink-0 place-items-center rounded-full border border-[var(--border)] bg-[var(--surface-elevated)]">
              <div className="text-center">
                <p className="text-xl font-semibold text-[var(--text-primary)]">{percent}%</p>
                <p className="eyebrow mt-1">Analyzed</p>
              </div>
            </div>
            {processed < total ? (
              <SvgAsset
                src={mascotAssets.loadingProgress}
                label="Mail sync loading"
                className="h-8 w-20 lg:mt-3"
              />
            ) : null}
          </div>
          <div className="min-w-0">
            <div className="flex flex-wrap items-end justify-between gap-2">
              <div>
                <p className="text-sm font-semibold text-[var(--text-primary)]">
                  {total > 0 ? `${processed.toLocaleString("en-US")}/${total.toLocaleString("en-US")} emails analyzed` : "No synced mail yet"}
                </p>
                <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                  {remaining > 0
                    ? `${remaining.toLocaleString("en-US")} email${remaining === 1 ? "" : "s"} remaining`
                    : "Mailbox analysis is complete"}
                </p>
              </div>
              {processed < total && progressEstimate ? (
                <span
                  className={`badge ${
                    progressEstimate.kind === "stalled"
                      ? "bg-[var(--yellow-soft)] text-[var(--yellow)]"
                      : "bg-[var(--blue-soft)] text-[var(--blue)]"
                  }`}
                >
                  {progressEstimate.label}
                </span>
              ) : null}
            </div>
            <div className="mt-3 h-2.5 overflow-hidden rounded-full bg-[var(--surface-overlay)]">
              <div
                className={`h-full rounded-full transition-all duration-700 ${
                  isStalled ? "bg-[var(--yellow)]" : "bg-[var(--blue)]"
                }`}
                style={{ width: `${total > 0 ? Math.min(100, Math.max(4, percent)) : 0}%` }}
              />
            </div>
          </div>
        </div>
      </div>

      <div className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
        <SyncMetric label="Last sync" value={formatTimestamp(dashboard.inbox.lastSyncAtUtc, timeZoneId)} />
        <SyncMetric label="Remaining" value={remaining > 0 ? remaining.toLocaleString("en-US") : "0"} hint="emails left" />
        <SyncMetric
          label="Queue"
          value={`${pendingAnalysis + processingAnalysis} active`}
          hint={`${pendingAnalysis} pending · ${processingAnalysis} processing`}
        />
        <SyncMetric
          label="Issues"
          value={failedAnalysis.toLocaleString("en-US")}
          hint="latest analysis errors"
          tone={failedAnalysis > 0 ? "bad" : "good"}
        />
      </div>

      <div className="mt-4 grid gap-3">
        <div className="info-tile">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <p className="eyebrow">Mailbox</p>
              <p className="mt-2 break-all text-sm font-medium text-[var(--text-primary)]">
                {connectedAccounts.length > 0
                  ? connectedAccounts.map((account) => account.emailAddress).join(", ")
                  : "No connected Gmail mailbox."}
              </p>
            </div>
            <span className="badge bg-[var(--surface-overlay)] text-[var(--text-tertiary)]">
              {connectedAccounts.length} connected
            </span>
          </div>
        </div>

        {attentionAccounts.length > 0 ? <FinishSetupList accounts={attentionAccounts} /> : null}
      </div>
        </>
      )}
    </section>
  );
}
