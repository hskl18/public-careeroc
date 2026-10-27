"use client";

import { useEffect, useEffectEvent, useRef, useState } from "react";
import { fetchWithInFlightDedup } from "@/lib/client-fetch";
import { formatTimestampInTimeZone } from "@/lib/date-time";
import type { DashboardResponse } from "@/lib/dashboard";
import {
  activeMailSyncPollingIntervalMs,
  idleWorkspacePollingIntervalMs,
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

type ProgressSample = {
  processed: number;
  total: number;
  capturedAtMs: number;
};

type ProgressEstimate = {
  kind: "estimating" | "moving" | "stalled";
  label: string;
  detail: string;
};

type DashboardInboxCheckResponse = {
  dashboard: DashboardResponse;
};

type MailSyncState = {
  dashboard: DashboardResponse;
  connectedAccountCount: number;
};

const stalledWindowMs = 120000;
const progressWindowSize =
  Math.ceil(stalledWindowMs / activeMailSyncPollingIntervalMs) + 2;
const minimumMovingRate = 0.01;

function formatTimestamp(value: string | null, timeZoneId: string) {
  return formatTimestampInTimeZone(
    value,
    timeZoneId,
    "Waiting for first sync",
  );
}

function isConnectedGmailAccount(account: InboxAccountSetup) {
  return account.provider === "gmail" && account.connectionStatus === "Connected";
}

function isInvalidInboxMetadata(account: InboxAccountSetup) {
  return (
    account.isValid === false ||
    account.connectionStatus.toLowerCase() === "metadata invalid"
  );
}

function isReconnectableGmailAccount(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();

  return (
    account.provider === "gmail" &&
    account.connectionStatus !== "Connected" &&
    (isInvalidInboxMetadata(account) ||
      status.includes("reconnect") ||
      status.includes("not completed") ||
      status.includes("missing") ||
      status.includes("oauth"))
  );
}

function isActionableGmailSetup(account: InboxAccountSetup) {
  const status = account.connectionStatus.toLowerCase();
  const nextStep = account.nextStep.toLowerCase();

  return (
    account.provider === "gmail" &&
    account.connectionStatus !== "Connected" &&
    !status.includes("imported data deleted") &&
    !nextStep.includes("access was disconnected")
  );
}

function getDashboardProgress(dashboard: DashboardResponse) {
  const syncedEmails = Math.max(0, dashboard.inbox.syncedEmails);
  const processedEmails = Math.max(0, dashboard.inbox.processedEmails);
  const total = Math.max(syncedEmails, processedEmails);
  const processed = total === 0 ? 0 : Math.min(processedEmails, total);

  return {
    processed,
    total,
    percent: total === 0 ? 0 : Math.round((processed / total) * 100),
  };
}

function getPollingIntervalMs(
  dashboard: DashboardResponse,
  connectedAccountCount: number,
) {
  if (connectedAccountCount === 0) {
    return null;
  }

  const { processed, total } = getDashboardProgress(dashboard);
  const hasActiveAnalysis =
    (dashboard.inbox.pendingAnalysisEmails ?? 0) > 0 ||
    (dashboard.inbox.processingAnalysisEmails ?? 0) > 0 ||
    (total > 0 && processed < total);

  return hasActiveAnalysis || !dashboard.inbox.lastSyncAtUtc
    ? activeMailSyncPollingIntervalMs
    : idleWorkspacePollingIntervalMs;
}

function shouldAdvanceInboxProcessing(
  dashboard: DashboardResponse,
  connectedAccountCount: number,
) {
  return (
    getPollingIntervalMs(dashboard, connectedAccountCount) ===
    activeMailSyncPollingIntervalMs
  );
}

function updateProgressSamples(
  current: ProgressSample[],
  dashboard: DashboardResponse,
  capturedAtMs: number,
) {
  const { processed, total } = getDashboardProgress(dashboard);

  if (total === 0 || processed >= total) {
    return [];
  }

  const nextSample = { processed, total, capturedAtMs };
  const previous = current.at(-1);

  if (
    !previous ||
    processed < previous.processed ||
    total < previous.total ||
    capturedAtMs - previous.capturedAtMs < 1000
  ) {
    return [nextSample];
  }

  return [...current, nextSample].slice(-progressWindowSize);
}

function formatRemaining(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return "~1 min remaining";
  }

  if (seconds < 60) {
    const roundedSeconds = Math.max(5, Math.round(seconds / 5) * 5);
    return `~${roundedSeconds} sec remaining`;
  }

  const roundedMinutes = Math.max(1, Math.round(seconds / 60));
  return `~${roundedMinutes} min remaining`;
}

function formatRate(rate: number) {
  return `${rate < 0.1 ? rate.toFixed(2) : rate.toFixed(1)} emails/s`;
}

function formatElapsed(seconds: number) {
  if (seconds < 60) {
    return `${Math.max(5, Math.round(seconds / 5) * 5)} sec`;
  }

  return `${Math.max(1, Math.round(seconds / 60))} min`;
}

function formatCount(value: number | null | undefined) {
  return Math.max(0, value ?? 0);
}

function buildAnalysisBacklogDetail(dashboard: DashboardResponse) {
  const pending = formatCount(dashboard.inbox.pendingAnalysisEmails);
  const processing = formatCount(dashboard.inbox.processingAnalysisEmails);
  const failed = formatCount(dashboard.inbox.failedAnalysisEmails);
  const lastError = dashboard.inbox.lastAnalysisError?.trim();

  if (failed > 0) {
    return `${failed} analysis job${failed === 1 ? "" : "s"} failed.${lastError ? ` Latest error: ${lastError}` : ""}`;
  }

  if (processing > 0) {
    return `${processing} email${processing === 1 ? " is" : "s are"} locked in analysis; the worker may be stuck if this count does not change.`;
  }

  if (pending > 0) {
    return `${pending} email${pending === 1 ? " is" : "s are"} waiting for analysis; the worker may be idle or blocked.`;
  }

  return "No pending analysis job is visible, so the dashboard may be waiting on stale processing state.";
}

function buildProgressEstimate(
  samples: ProgressSample[],
  processed: number,
  total: number,
  dashboard: DashboardResponse,
): ProgressEstimate | null {
  if (total === 0 || processed >= total) {
    return null;
  }

  const latest = samples.at(-1);
  const oldest = samples[0];

  if (!latest || !oldest || samples.length < 2) {
    return {
      kind: "estimating",
      label: "Checking analysis worker...",
      detail: `${total - processed} email${total - processed === 1 ? "" : "s"} remain. ${buildAnalysisBacklogDetail(dashboard)}`,
    };
  }

  const elapsedSeconds = (latest.capturedAtMs - oldest.capturedAtMs) / 1000;
  const processedDelta = latest.processed - oldest.processed;
  const rate = elapsedSeconds > 0 ? processedDelta / elapsedSeconds : 0;
  const remaining = Math.max(0, total - processed);

  if (rate >= minimumMovingRate) {
    return {
      kind: "moving",
      label: `${formatRemaining(remaining / rate)} · ${formatRate(rate)}`,
      detail: `Analysis is moving. ${remaining} email${remaining === 1 ? "" : "s"} remain at ${formatRate(rate)}.`,
    };
  }

  if (latest.capturedAtMs - oldest.capturedAtMs < stalledWindowMs) {
    const processing = formatCount(dashboard.inbox.processingAnalysisEmails);

    return {
      kind: "estimating",
      label: processing > 0 ? "Model analysis running..." : "Checking for progress...",
      detail: processing > 0
        ? `${remaining} email${remaining === 1 ? "" : "s"} remain. ${processing} email${processing === 1 ? " is" : "s are"} currently in analysis; Gemma/Ollama calls can take up to the configured timeout before falling back.`
        : `${remaining} email${remaining === 1 ? "" : "s"} remain. No completed analysis has appeared in the latest samples yet.`,
    };
  }

  return {
    kind: "stalled",
    label: `Stalled at ${processed}/${total}`,
    detail: `Analysis is stuck at ${processed}/${total}. No email finished processing for about ${formatElapsed(elapsedSeconds)}. ${buildAnalysisBacklogDetail(dashboard)} It retries automatically; if it stays stuck, check the queue on the Admin page.`,
  };
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
    <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-3">
      <p className="eyebrow">{label}</p>
      <p className="mt-2 text-sm font-semibold" style={{ color: valueColor }}>
        {value}
      </p>
      {hint ? <p className="mt-1 text-xs text-[var(--text-tertiary)]">{hint}</p> : null}
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
  const connectedAccounts = setup.accounts.filter(isConnectedGmailAccount);
  const reconnectableAccounts = setup.accounts.filter(isReconnectableGmailAccount);
  const mailSyncStateRef = useRef<MailSyncState>({
    dashboard,
    connectedAccountCount: connectedAccounts.length,
  });
  const attentionAccounts = setup.accounts.filter(isActionableGmailSetup);
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
  const progressDetail =
    isStalled
      ? progressEstimate.detail
      : progressEstimate
        ? progressEstimate.detail
        : "New analysis is running in the background.";

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

  const status =
    !setupLive
      ? {
          label: "API offline",
          tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
          dot: "bg-[var(--yellow)]",
          pulse: false,
          detail: "Gmail setup status is unavailable right now.",
        }
      : connectedAccounts.length === 0 && reconnectableAccounts.length === 0
        ? {
            label: "Setup incomplete",
            tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
            dot: "bg-[var(--yellow)]",
            pulse: false,
            detail: "Connect Gmail to start the first import.",
          }
        : reconnectableAccounts.length > 0
          ? {
              label: "Reconnect Gmail",
              tone: "border-[var(--red)]/25 bg-[var(--red-soft)] text-[var(--red)]",
              dot: "bg-[var(--red)]",
              pulse: false,
              detail: `${reconnectableAccounts.length} Gmail connection${reconnectableAccounts.length === 1 ? "" : "s"} needs Google reconnection before sync can continue.`,
            }
        : !dashboardLive
          ? {
              label: "API offline",
              tone: "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]",
              dot: "bg-[var(--yellow)]",
              pulse: false,
              detail: "Live sync status is unavailable right now.",
            }
          : attentionAccounts.length > 0
          ? {
              label: "Finish setup",
              tone: "border-[var(--red)]/25 bg-[var(--red-soft)] text-[var(--red)]",
              dot: "bg-[var(--red)]",
              pulse: false,
              detail: `${attentionAccounts.length} Gmail connection${attentionAccounts.length === 1 ? "" : "s"} needs setup.`,
            }
          : dashboard.inbox.lastSyncAtUtc
            ? {
                label: processed < total
                  ? isStalled
                    ? "Analysis stalled"
                    : "Analyzing mail"
                  : "Synced",
                tone: processed < total
                  ? isStalled
                    ? "border-[var(--yellow)]/25 bg-[var(--yellow-soft)] text-[var(--yellow)]"
                    : "border-[var(--blue)]/25 bg-[var(--blue-soft)] text-[var(--blue)]"
                  : "border-[var(--green)]/25 bg-[var(--green-soft)] text-[var(--green)]",
                dot: processed < total
                  ? isStalled
                    ? "bg-[var(--yellow)]"
                    : "bg-[var(--blue)]"
                  : "bg-[var(--green)]",
                pulse: processed < total && !isStalled,
                detail: processed < total
                  ? progressDetail
                  : "Last sync and analysis are complete. New Gmail messages will sync automatically.",
              }
            : {
                label: "Waiting for first sync",
                tone: "border-[var(--blue)]/25 bg-[var(--blue-soft)] text-[var(--blue)]",
                dot: "bg-[var(--blue)]",
                pulse: true,
                detail: "Gmail is connected and waiting for the first sync to finish.",
              };

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
    <section id="mail-sync" className="card app-workspace-panel scroll-mt-20 p-5">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <div className="min-w-0">
            <p className="eyebrow">Gmail sync</p>
            <h2 className="mt-2 text-xl font-semibold text-[var(--text-primary)]">
              {shouldCollapseSyncDetails ? "No Gmail inbox connected yet" : "Gmail sync"}
            </h2>
            <p className="mt-2 text-sm leading-6 text-[var(--text-secondary)]">
              {shouldCollapseSyncDetails
                ? "Connect Gmail to import the last 3 months of recruiting emails and start building your application tracker."
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
        <details className="mt-5 overflow-hidden rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.32)]">
          <summary className="px-4 py-3">
            <div className="flex-1">
              <p className="eyebrow">Advanced sync details</p>
              <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                Import history, sync queue, and issue counters.
              </p>
            </div>
          </summary>
          <div className="border-t border-[var(--border)] p-4">
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-3">
                <p className="eyebrow">Last sync</p>
                <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
                  {formatTimestamp(dashboard.inbox.lastSyncAtUtc, timeZoneId)}
                </p>
              </div>
              <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-3">
                <p className="eyebrow">Emails processed</p>
                <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
                  {total > 0 ? `${processed}/${total} emails analyzed` : "No synced mail yet"}
                </p>
              </div>
              <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-3">
                <p className="eyebrow">Sync queue</p>
                <p className="mt-2 text-sm font-semibold text-[var(--text-primary)]">
                  {pendingAnalysis + processingAnalysis} active jobs
                </p>
                <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                  {pendingAnalysis} pending · {processingAnalysis} processing
                </p>
              </div>
              <div className="rounded-md border border-[var(--border)] bg-[rgba(255,255,255,0.42)] px-3 py-3">
                <p className="eyebrow">Issues</p>
                <p
                  className="mt-2 text-sm font-semibold"
                  style={{ color: failedAnalysis > 0 ? "var(--red)" : "var(--green)" }}
                >
                  {failedAnalysis}
                </p>
              </div>
            </div>

            {attentionAccounts.length > 0 ? (
              <div className="info-tile mt-3 border-[rgba(138,90,9,0.32)] bg-[var(--yellow-soft)]">
                <p className="eyebrow text-[var(--yellow)]">Finish setup</p>
                <div className="mt-3 space-y-3">
                  {attentionAccounts.map((account) => (
                    <div
                      key={account.id}
                      className="grid gap-1 border-t border-[rgba(138,90,9,0.18)] pt-3 first:border-t-0 first:pt-0"
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
            ) : null}
          </div>
        </details>
      ) : (
        <>
      <div className="mt-5 rounded-md border border-[var(--border)] bg-white/45 p-4">
        <div className="grid gap-4 lg:grid-cols-[10rem_minmax(0,1fr)] lg:items-center">
          <div className="flex items-center gap-3 lg:block">
            <div className="grid h-24 w-24 shrink-0 place-items-center rounded-full border border-[var(--border)] bg-[var(--surface-elevated)]">
              <div className="text-center">
                <p className="text-2xl font-semibold text-[var(--text-primary)]">{percent}%</p>
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
                  {total > 0 ? `${processed.toLocaleString()}/${total.toLocaleString()} emails analyzed` : "No synced mail yet"}
                </p>
                <p className="mt-1 text-xs text-[var(--text-tertiary)]">
                  {remaining > 0
                    ? `${remaining.toLocaleString()} email${remaining === 1 ? "" : "s"} remaining`
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
        <SyncMetric label="Remaining" value={remaining > 0 ? remaining.toLocaleString() : "0"} hint="emails left" />
        <SyncMetric
          label="Queue"
          value={`${pendingAnalysis + processingAnalysis} active`}
          hint={`${pendingAnalysis} pending · ${processingAnalysis} processing`}
        />
        <SyncMetric
          label="Issues"
          value={failedAnalysis.toLocaleString()}
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

        {attentionAccounts.length > 0 ? (
          <div className="info-tile border-[rgba(138,90,9,0.32)] bg-[var(--yellow-soft)]">
            <p className="eyebrow text-[var(--yellow)]">Finish setup</p>
            <div className="mt-3 space-y-3">
              {attentionAccounts.map((account) => (
                <div
                  key={account.id}
                  className="grid gap-1 border-t border-[rgba(138,90,9,0.18)] pt-3 first:border-t-0 first:pt-0"
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
        ) : null}
      </div>
        </>
      )}
    </section>
  );
}
